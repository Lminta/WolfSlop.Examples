using System.Net;
using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Game.Player.Components;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var webRoot = Path.Combine(root, "WolfNET.Game", "wwwroot");
using var http = new HttpClient(new FileHandler(webRoot)) { BaseAddress = new Uri("https://test.local/") };
int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (InvalidDataException) { assertions++; return; }
    throw new Exception(message);
}

// Load every shipped map, without pinning its authored scenery or dimensions.
var workspace = new WolfNET.Editor.MapWorkspace(webRoot);
foreach (var name in workspace.Maps())
{
    var loaded = await GameMap.LoadAsync(http, $"maps/{name}.json", 512, 512);
    Check(loaded.RendererMap.Cells.Count == loaded.RendererMap.Width * loaded.RendererMap.Height,
        $"{name}: render grid is incomplete");
    Check(loaded.RendererMap.Sprites.All(sprite => sprite.OwnerCell > 0 &&
        sprite.OwnerCell <= loaded.RendererMap.Cells.Count &&
        loaded.RendererMap.Textures.Layers.ContainsKey(sprite.TextureLayer)),
        $"{name}: sprite ownership or texture is invalid");
}

var definition = new MapDefinition
{
    WorldMap = Enumerable.Range(0, 9).Select(x => Enumerable.Range(0, 9)
        .Select(y => x == 0 || y == 0 || x == 8 || y == 8 ? 1 : 0).ToArray()).ToArray(),
    PlayerStart = new() { X = 4.5f, Y = 4.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 5, RotSpeed = 2 }
};
var objects = new Dictionary<int, MapObjectDefinition>
{
    [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false },
    [1] = new WallDefinition { Id = 1, PassageId = 0, RaycastTransparent = false, Collides = true },
    [2] = new SpriteDefinition { Id = 2, PassageId = 0, RaycastTransparent = true, Collides = true,
        Texture = "textures/wolf/barrel.png", TextureLayer = 8 }
};
GameMap Compile(MapDefinition? source = null) => new(source ?? definition, objects, 64, 64);
var map = Compile();
var navigation = map.NavigationMap;
var snapshot = map.RendererMap;
map.SetCell(5, 4, 0);
Check(ReferenceEquals(snapshot, map.RendererMap), "No-op edit rebuilt the map");
map.SetCell(5, 4, 2);
Check(ReferenceEquals(navigation, map.NavigationMap) && !navigation.IsWalkable(5, 4) &&
    map.RendererMap.Sprites.Count == 1, "Edit did not update navigation and rendering together");
snapshot = map.RendererMap;
Reject(() => map.SetCell(5, 4, 999), "Unknown object accepted");
Reject(() => map.SetCell(0, 0, 0), "Open boundary accepted");
Check(ReferenceEquals(snapshot, map.RendererMap), "Invalid edit partially published");
var copy = map.WorldMap;
copy[5][4] = 0;
Check(!map.NavigationMap.IsWalkable(5, 4), "External grid mutation changed the map");
map.ReplaceMap(definition.WorldMap);
Check(navigation.IsWalkable(5, 4) && map.RendererMap.Sprites.Count == 0, "Replacement retained stale state");
Reject(() => Compile(definition with { WorldMap = [[1], [1, 0]] }), "Ragged map accepted");
Reject(() => Compile(definition with { PlayerStart = definition.PlayerStart with { X = 0, Y = 0 } }), "Blocked spawn accepted");
Reject(() => Compile(definition with { PlayerStart = definition.PlayerStart with { DirectionX = 0 } }), "Zero heading accepted");
using (var malformedHttp = new HttpClient(new FileHandler(webRoot, path => path == "maps/level1.json" ? "{}" : null))
       { BaseAddress = http.BaseAddress })
{
    try
    {
        await GameMap.LoadAsync(malformedHttp, "maps/level1.json", 64, 64);
        throw new Exception("Malformed level accepted");
    }
    catch (JsonException) { assertions++; }
}

// Exercise the real walking controller against a wall, then jump and crouch.
using (var player = new TestObject { Transform = new() { Position = new(4.5f), Rotation = Vector2.UnitX } })
{
    var keys = new FakeKeyboard { KeyState = new() { Forward = true } };
    var camera = new CameraComponent(player) { Plane = new(0, -.66f) };
    var controller = new PlayerController(keys, new FakeMouse(), navigation, camera, map.PlayerStart, player);
    for (var frame = 0; frame < 120; frame++) controller.Update(1000f / 60);
    Check(player.Transform.Position.X > 7 && player.Transform.Position.X < 8 &&
        navigation.IsWalkable((int)player.Transform.Position.X, (int)player.Transform.Position.Y),
        "Walking failed to advance or passed through the boundary wall");
    player.Transform = player.Transform with { Position = new(4.5f) };
    keys.KeyState = new() { Jump = true };
    controller.Update(0);
    controller.Update(100);
    Check(controller.InAir && camera.Elevation > 0, "Jump did not raise the player");
    for (var frame = 0; frame < 60; frame++) controller.Update(1000f / 60);
    Check(!controller.InAir && camera.Elevation == 0, "Jump did not land");
    keys.KeyState = new() { Crouch = true };
    controller.Update(16);
    Check(controller.Crouching && camera.Elevation < 0, "Crouch did not lower the camera");
    keys.KeyState = new();
    controller.Update(16);
    Check(!controller.Crouching && camera.Elevation == 0, "Crouch did not release");
}

// One save/load/export round trip covers the editor's persistence boundary.
var document = workspace.Load("showcase") with { Name = "editor-roundtrip" };
Reject(() => workspace.Load("../showcase"), "Editor accepted path traversal");
Reject(() => workspace.Validate(document with { Name = "CON" }), "Editor accepted a reserved filename");
var editorRoot = Path.Combine(root, "artifacts", "editor-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(editorRoot, "maps"));
try
{
    var store = new WolfNET.Editor.MapWorkspace(editorRoot);
    await store.SaveAsync(document, false, CancellationToken.None);
    var persisted = store.Load(document.Name);
    Check(persisted.Map.WorldMap.SelectMany(column => column).SequenceEqual(document.Map.WorldMap.SelectMany(column => column)) &&
        persisted.Map.PlayerStart == document.Map.PlayerStart && persisted.Map.Sky == document.Map.Sky &&
        persisted.Objects.Count == document.Objects.Count, "Editor round trip changed map data");
    using var savedHttp = new HttpClient(new FileHandler(editorRoot)) { BaseAddress = http.BaseAddress };
    var loaded = await GameMap.LoadAsync(savedHttp, $"maps/{document.Name}.json", 64, 64);
    Check(loaded.RendererMap.Cells.Count == persisted.Map.WorldMap.Sum(column => column.Length),
        "Engine could not read the editor's saved map");
    try { await store.SaveAsync(document, false, CancellationToken.None); throw new Exception("Editor silently overwrote a map"); }
    catch (InvalidDataException) { assertions++; }
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(store.Export(document)));
    Check(zip.GetEntry($"maps/{document.Name}.json") != null &&
        persisted.Map.Objects.Values.All(path => zip.GetEntry(path[3..]) != null), "Export lost map dependencies");
    var unsafeMap = definition with { Objects = new() { [1] = "../../outside.json" } };
    File.WriteAllText(Path.Combine(editorRoot, "maps/unsafe.json"), JsonSerializer.Serialize(unsafeMap,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Reject(() => store.Load("unsafe"), "Editor followed a path outside engine assets");
}
finally { Directory.Delete(editorRoot, true); }

assertions += await EngineChecks.RunAsync(http);
assertions += await CarInteractionChecks.RunAsync(http);
assertions += CarPhysicsChecks.Run();
assertions += CarCameraChecks.Run();
assertions += SpriteAnimationChecks.Run();
await SpriteSelectionFixtures.WriteAsync(http, webRoot);
Console.WriteLine($"PASS: {assertions} assertions (map loading/editing, walking, driving, scenes, animation, and editor persistence).");

sealed class FileHandler(string root, Func<string, string?>? replacement = null) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests.Add(path);
        var value = replacement?.Invoke(path);
        var file = Path.Combine(root, path);
        return Task.FromResult(value != null || File.Exists(file)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value ?? File.ReadAllText(file), System.Text.Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
