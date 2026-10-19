using System.Net;
using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Graphics;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var webRoot = Path.Combine(root, "WolfNET.Game", "wwwroot");
var fileHandler = new FileHandler(webRoot);
using var http = new HttpClient(fileHandler) { BaseAddress = new Uri("https://test.local/") };
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
async Task RejectLoad<T>(HttpClient client, string message, CancellationToken token = default) where T : Exception
{
    try { await GameMap.LoadAsync(client, "maps/level1.json", 64, 64, token); }
    catch (T) { assertions++; return; }
    throw new Exception(message);
}

var map = await GameMap.LoadAsync(http, "maps/level1.json", 64, 64);
using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Tests/Fixtures/original-level.json")));
var originalGrid = original.RootElement.GetProperty("worldMap").Deserialize<int[][]>()!;
string[] originalWallTextures = ["eagle", "redbrick", "purplestone", "greystone", "bluestone", "mossy", "wood", "colorstone"];
Check(map.Revision == 1 && map.RendererMap.Width == 24 && map.RendererMap.Height == 24, "Initial revision/dimensions changed");
for (int x = 0; x < 24; x++)
for (int y = 0; y < 24; y++)
{
    Check(map.NavigationMap.IsWalkable(x, y) == (originalGrid[x][y] == 0), $"Walkability changed at {x},{y}");
    Check(map.RendererMap.Cells[y * 24 + x].Flags.HasFlag(RenderCellFlags.Wall) == (originalGrid[x][y] > 0), "Wall layout changed");
    var cell = map.RendererMap.Cells[y * 24 + x];
    if (originalGrid[x][y] > 0)
    {
        var faces = map.RendererMap.Materials[(int)cell.MaterialIndex];
        var texture = "textures/" + originalWallTextures[originalGrid[x][y] - 1] + ".png";
        Check(new[] { faces.North, faces.East, faces.South, faces.West }.All(layer => map.RendererMap.Textures.Layers[layer] == texture), "Migrated wall texture changed");
    }
    else
        Check(map.RendererMap.Textures.Layers[cell.FloorTextureLayer] == "textures/mossy.png" &&
              map.RendererMap.Textures.Layers[cell.CeilingTextureLayer] == "textures/wood.png", "Migrated passage surfaces changed");
}
var expectedSprites = original.RootElement.GetProperty("sprites").EnumerateArray().Select(value =>
    (new Vector2(value.GetProperty("x").GetSingle(), value.GetProperty("y").GetSingle()), value.GetProperty("textureLayer").GetUInt32())).ToList();
Check(map.RendererMap.Sprites.Count == 19, "Sprite count changed");
foreach (var sprite in map.RendererMap.Sprites)
{
    int match = expectedSprites.FindIndex(expected => expected.Item2 == sprite.TextureLayer && Vector2.Distance(expected.Item1, sprite.Position) < 0.0001);
    Check(match >= 0, "Sprite position/texture changed");
    expectedSprites.RemoveAt(match);
    Check(sprite.OwnerCell > 0 && sprite.OwnerCell <= 576, "Sprite owner cell invalid");
}
Check(map.PlayerStartPosition == new Vector2(22, 11.5f) && map.PlayerStartDirection == new Vector2(-1, 0), "Spawn changed");
Check(map.PlayerStart.MoveSpeed == 5 && map.PlayerStart.RotSpeed == 3, "Map player speeds not loaded");
Check(!map.NavigationMap.IsWalkable(-1, 0) && !map.NavigationMap.IsWalkable(24, 24), "Outside map is walkable");
var material = map.RendererMap.Materials[(int)map.RendererMap.Cells[0].MaterialIndex];
Check(map.RendererMap.Textures.Layers[material.North] == "textures/colorstone.png", "Wall material changed");
Check(material.North == material.East && material.East == material.South && material.South == material.West, "Wall faces changed");

Check(fileHandler.Requests.All(path => !path.Contains("showcase") && !path.EndsWith("index.json")), "Loaded another map or global index");
Check(fileHandler.Requests.Count == map.Objects.Count + 1, "An object was loaded more than once");
Check(map.RendererMap.Textures.Layers.Values.All(path => path == null || !path.Contains("showcase-missing")), "Unrelated map textures leaked");
var showcase = await GameMap.LoadAsync(http, "maps/showcase.json", 64, 64);
Check(showcase.RendererMap.Width == 25 && showcase.RendererMap.Height == 25 && showcase.Revision == 1, "Showcase dimensions/revision invalid");
Check(showcase.PlayerStartPosition == new Vector2(6.5f, 10.5f) && showcase.PlayerStartDirection == new Vector2(0, -1), "Showcase spawn invalid");
Check(showcase.RendererMap.Sprites.Count == 24, "Showcase sprite demonstrations missing");
RenderCell ShowcaseCell(int x, int y) => showcase.RendererMap.Cells[y * 25 + x];
Check(showcase.Objects.Values.Where(value => value is SpriteDefinition or SpriteGroupDefinition).All(value => value.RaycastTransparent), "Showcase contains ray-blocking sprites");
Check(!showcase.Objects.ContainsKey(113) && !showcase.Objects.ContainsKey(122), "Removed sprite examples still loaded");
foreach (var x in new[] { 15, 17, 19, 21 })
{
    var cell = ShowcaseCell(x, 6);
    Check(showcase.RendererMap.Textures.Layers[cell.FloorTextureLayer] == "textures/colorstone.png" &&
          showcase.RendererMap.Textures.Layers[cell.CeilingTextureLayer] == "textures/wood.png", "Lane wall surfaces missing");
}

foreach (var (x, blocksRays, walkable) in new[] { (15, true, false), (17, true, true), (19, false, false), (21, false, true) })
{
    Check(ShowcaseCell(x, 6).Flags.HasFlag(RenderCellFlags.StopsRay) == blocksRays, "Showcase ray-blocking lane incorrect");
    Check(showcase.NavigationMap.IsWalkable(x, 6) == walkable, "Showcase collision lane incorrect");
}
Check(!showcase.NavigationMap.IsWalkable(3, 21) && showcase.NavigationMap.IsWalkable(6, 21), "Showcase group walkability incorrect");
foreach (var sprite in showcase.RendererMap.Sprites)
{
    var definition = showcase.Objects.Values.OfType<SpriteDefinition>().First(value => value.TextureLayer == sprite.TextureLayer);
    if (definition.Texture is "textures/barrel.png" or "textures/greenlight.png" or "textures/pillar.png")
        Check(showcase.NavigationMap.IsWalkable((int)sprite.Position.X, (int)sprite.Position.Y) == (definition.Texture != "textures/pillar.png"),
            "Showcase sprite walkability disagrees with its appearance");
}
Check(ShowcaseCell(19, 6).Flags.HasFlag(RenderCellFlags.Wall) && ShowcaseCell(21, 6).Flags.HasFlag(RenderCellFlags.Wall), "Showcase transparent wall demonstrations missing");
Check(showcase.NavigationMap.IsWalkable(13, 8) && !ShowcaseCell(13, 8).Flags.HasFlag(RenderCellFlags.StopsRay) &&
      !ShowcaseCell(13, 8).Flags.HasFlag(RenderCellFlags.Wall), "Open floor outside the test lanes is blocked");
Check(!showcase.Objects.ContainsKey(26), "Stray invisible passage barrier is still loaded");
var fourFaces = showcase.RendererMap.Materials[(int)ShowcaseCell(6, 5).MaterialIndex];
Check(new[] { fourFaces.North, fourFaces.East, fourFaces.South, fourFaces.West }.Distinct().Count() == 4, "Showcase wall needs four different faces");
Check(ShowcaseCell(3, 4).FloorTextureLayer != ShowcaseCell(3, 3).FloorTextureLayer, "Showcase floor variation missing");
Check(ShowcaseCell(15, 21).FloorTextureLayer == TextureCatalog.MissingLayer && ShowcaseCell(15, 21).CeilingTextureLayer == TextureCatalog.MissingLayer, "Showcase missing surface fields not demonstrated");
Check(showcase.RendererMap.Materials[(int)ShowcaseCell(18, 17).MaterialIndex].West == TextureCatalog.MissingLayer, "Showcase missing wall face not demonstrated");
Check(showcase.RendererMap.Sprites.Any(sprite => sprite.TextureLayer == 11) && showcase.RendererMap.Sprites.Any(sprite => sprite.TextureLayer == 12), "Showcase missing sprites not demonstrated");
var reachable = new HashSet<(int X, int Y)> { (6, 10) };
var frontier = new Queue<(int X, int Y)>(); frontier.Enqueue((6, 10));
while (frontier.TryDequeue(out var cell))
foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
{
    var next = (X: cell.X + dx, Y: cell.Y + dy);
    if (showcase.NavigationMap.IsWalkable(next.X, next.Y) && reachable.Add(next)) frontier.Enqueue(next);
}
for (int x = 0; x < 25; x++)
for (int y = 0; y < 25; y++)
    Check(!showcase.NavigationMap.IsWalkable(x, y) || reachable.Contains((x, y)), $"Unreachable showcase cell at {x},{y}");

PassageDefinition Passage(int id, bool transparent = true, bool collides = false) => new()
{ Id = id, RaycastTransparent = transparent, Collides = collides, FloorTexture = "textures/mossy.png", CeilingTexture = "textures/wood.png" };
var wall = new WallDefinition { Id = 1, RaycastTransparent = false, Collides = true, PassageId = 0, Textures = new WallTextures
{ North = "textures/north.png", East = "textures/east.png", South = "textures/south.png" } };
var spriteDef = new SpriteDefinition { Id = 2, RaycastTransparent = false, Collides = true, PassageId = 0, Texture = "textures/barrel.png", TextureLayer = 8 };
var groupDef = new SpriteGroupDefinition { Id = 3, RaycastTransparent = true, Collides = false, PassageId = 0,
    Sprites = [new() { SpriteId = 2, Offset = new() { X = -0.25f, Y = 0 } }, new() { SpriteId = 2, Offset = new() { X = 0.75f, Y = 0.1f } }] };
var definitions = new Dictionary<int, MapObjectDefinition> { [0] = Passage(0), [1] = wall, [2] = spriteDef, [3] = groupDef };
MapDefinition SmallMap(int center = 0) => new()
{
    WorldMap = [[1, 1, 1, 1, 1], [1, 0, 0, 0, 1], [1, 0, center, 0, 1], [1, 0, 0, 0, 1], [1, 1, 1, 1, 1]],
    PlayerStart = new() { X = 1.5f, Y = 1.5f, DirectionX = -1, DirectionY = 0, MoveSpeed = 5, RotSpeed = 2 }
};
GameMap Compile(MapDefinition? definition = null, Dictionary<int, MapObjectDefinition>? objects = null) => new(definition ?? SmallMap(), objects ?? definitions, 64, 64);
foreach (bool transparent in new[] { false, true })
foreach (bool collides in new[] { false, true })
foreach (int type in new[] { 0, 1, 2, 3 })
{
    const int testId = 10;
    MapObjectDefinition value = type switch
    {
        0 => Passage(testId, transparent, collides),
        1 => wall with { Id = testId, RaycastTransparent = transparent, Collides = collides },
        2 => spriteDef with { Id = testId, RaycastTransparent = transparent, Collides = collides },
        _ => groupDef with { Id = testId, RaycastTransparent = transparent, Collides = collides }
    };
    var objects = new Dictionary<int, MapObjectDefinition>(definitions) { [testId] = value };
    var compiled = Compile(SmallMap(testId), objects);
    Check(compiled.NavigationMap.IsWalkable(2, 2) == !collides, "Collision flag ignored");
    Check(compiled.RendererMap.Cells[12].Flags.HasFlag(RenderCellFlags.StopsRay) == !transparent, "Ray transparency flag ignored");
}
var group = Compile(SmallMap(3));
Check(group.NavigationMap.IsWalkable(2, 2), "Child flags overrode group flags");
Check(group.RendererMap.Sprites.Count == 2, "Repeated sprite ID collapsed");
Check(group.RendererMap.Sprites[0].Position == new Vector2(2.25f, 2.5f) && group.RendererMap.Sprites[1].Position == new Vector2(3.25f, 2.6f), "Group offsets incorrect");
Check(group.RendererMap.Sprites.All(sprite => sprite.OwnerCell == 13), "Cross-cell correction changed owner");
Check(group.RendererMap.Sprites.All(sprite => sprite.Scale == Vector2.One && sprite.Elevation == 0), "Legacy member transforms changed");
var transformedGroup = Compile(SmallMap(3), new(definitions)
{
    [3] = groupDef with { Sprites = [
        groupDef.Sprites[0] with { Scale = new() { X = 0.5f, Y = 1.25f }, Offset = groupDef.Sprites[0].Offset with { Z = 0.375f } },
        groupDef.Sprites[1] with { Scale = new() { X = 2, Y = 0.25f }, Offset = groupDef.Sprites[1].Offset with { Z = -0.125f } }
    ] }
});
Check(transformedGroup.RendererMap.Sprites[0].Scale == new Vector2(0.5f, 1.25f) && transformedGroup.RendererMap.Sprites[0].Elevation == 0.375f, "First member transform was not compiled");
Check(transformedGroup.RendererMap.Sprites[1].Scale == new Vector2(2, 0.25f) && transformedGroup.RendererMap.Sprites[1].Elevation == -0.125f, "Second member transform was not compiled independently");
Check(transformedGroup.RendererMap.Sprites.Select(sprite => sprite.Position).SequenceEqual(group.RendererMap.Sprites.Select(sprite => sprite.Position)), "Scale/elevation changed member XY positions");
Check(transformedGroup.RendererMap.Sprites.All(sprite => sprite.OwnerCell == 13) && transformedGroup.NavigationMap.IsWalkable(2, 2), "Member transforms changed ownership or navigation");
foreach (var invalidScale in new[] { -1f, float.NaN, float.PositiveInfinity })
{
    Reject(() => Compile(SmallMap(3), new(definitions) { [3] = groupDef with { Sprites = [groupDef.Sprites[0] with { Scale = new() { X = invalidScale } }] } }), "Invalid member scale accepted");
}
foreach (var invalidZ in new[] { float.NaN, float.PositiveInfinity })
    Reject(() => Compile(SmallMap(3), new(definitions) { [3] = groupDef with { Sprites = [groupDef.Sprites[0] with { Offset = groupDef.Sprites[0].Offset with { Z = invalidZ } }] } }), "Invalid member elevation accepted");
var hiddenMember = Compile(SmallMap(3), new(definitions) { [3] = groupDef with { Sprites = [groupDef.Sprites[0] with { Scale = new() { X = 0, Y = 0 } }] } });
Check(hiddenMember.RendererMap.Sprites.Single().Scale == Vector2.Zero, "Zero scale should allow a hidden billboard");
Check(group.RendererMap.Textures.Layers[8] == "textures/barrel.png", "Explicit sprite layer not reserved");
var groupPassage = Compile(SmallMap(3), new(definitions)
{
    [4] = Passage(4, false, true) with { FloorTexture = "textures/bluestone.png", CeilingTexture = "textures/greystone.png" },
    [3] = groupDef with { PassageId = 4 }
});
Check(groupPassage.NavigationMap.IsWalkable(2, 2) && !groupPassage.RendererMap.Cells[12].Flags.HasFlag(RenderCellFlags.StopsRay), "Passage flags overrode group flags");
Check(groupPassage.RendererMap.Textures.Layers[groupPassage.RendererMap.Cells[12].FloorTextureLayer] == "textures/bluestone.png" &&
      groupPassage.RendererMap.Textures.Layers[groupPassage.RendererMap.Cells[12].CeilingTextureLayer] == "textures/greystone.png", "Group did not use its own passage reference");
var walls = group.RendererMap.Materials[1];
Check(walls.West == TextureCatalog.MissingLayer, "Missing wall side not pink");
var wallSurfaces = Compile(SmallMap(1), new(definitions)
{
    [4] = Passage(4, false, true) with { FloorTexture = "textures/wall-floor.png", CeilingTexture = "textures/wall-ceiling.png" },
    [1] = wall with { PassageId = 4 }
});
var wallCell = wallSurfaces.RendererMap.Cells[12];
Check(wallSurfaces.RendererMap.Textures.Layers[wallCell.FloorTextureLayer] == "textures/wall-floor.png" &&
      wallSurfaces.RendererMap.Textures.Layers[wallCell.CeilingTextureLayer] == "textures/wall-ceiling.png", "Wall surfaces not compiled");
var missingWallCell = Compile(SmallMap(1), new(definitions)
{
    [4] = Passage(4) with { FloorTexture = null, CeilingTexture = null },
    [1] = wall with { PassageId = 4 }
}).RendererMap.Cells[12];
Check(missingWallCell.FloorTextureLayer == TextureCatalog.MissingLayer && missingWallCell.CeilingTextureLayer == TextureCatalog.MissingLayer, "Missing wall surfaces should fall back to pink");
var noWallTextures = Compile(objects: new(definitions) { [1] = wall with { Textures = null } }).RendererMap.Materials[1];
Check(new[] { noWallTextures.North, noWallTextures.East, noWallTextures.South, noWallTextures.West }.All(layer => layer == TextureCatalog.MissingLayer), "Omitted wall textures did not fall back");
Check(group.RendererMap.Textures.Layers[walls.North] == "textures/north.png" && group.RendererMap.Textures.Layers[walls.South] == "textures/south.png", "Wall side mapping incorrect");
var missing = new Dictionary<int, MapObjectDefinition>(definitions)
{ [0] = Passage(0) with { FloorTexture = null, CeilingTexture = null }, [2] = spriteDef with { Texture = null } };
var missingMap = Compile(SmallMap(2), missing);
Check(missingMap.RendererMap.Cells[12].FloorTextureLayer == TextureCatalog.MissingLayer && missingMap.RendererMap.Textures.Layers[8] == null, "Missing surfaces/sprite not represented");

var nav = group.NavigationMap;
var snapshot = group.RendererMap;
group.SetCell(2, 2, 3);
Check(group.Revision == 1 && ReferenceEquals(snapshot, group.RendererMap), "No-op edit changed revision");
group.SetCell(2, 2, 2);
Check(group.Revision == 2 && ReferenceEquals(nav, group.NavigationMap) && !nav.IsWalkable(2, 2), "Edit did not update stable navigation");
snapshot = group.RendererMap;
Reject(() => group.SetCell(2, 2, 999), "Unknown ID accepted");
Reject(() => group.SetCell(0, 0, 0), "Open boundary accepted");
Check(group.Revision == 2 && ReferenceEquals(snapshot, group.RendererMap) && !nav.IsWalkable(2, 2), "Invalid edit partially published");
var leaked = group.WorldMap; leaked[2][2] = 999;
Check(group.WorldMap[2][2] == 2, "Source grid is externally mutable");
group.ReplaceMap(SmallMap().WorldMap);
Check(group.Revision == 3 && nav.IsWalkable(2, 2) && group.RendererMap.Sprites.Count == 0, "Replacement failed");

Reject(() => Compile(SmallMap(999)), "Unknown cell ID accepted");
Reject(() => Compile(SmallMap() with { WorldMap = [[1], [1, 0]] }), "Ragged map accepted");
Reject(() => Compile(SmallMap() with { WorldMap = [] }), "Empty map accepted");
Reject(() => Compile(SmallMap() with { PlayerStart = SmallMap().PlayerStart with { X = 0, Y = 0 } }), "Blocked spawn accepted");
Reject(() => Compile(SmallMap() with { PlayerStart = SmallMap().PlayerStart with { DirectionX = 0, DirectionY = 0 } }), "Zero direction accepted");
foreach (var invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
{
    Reject(() => Compile(SmallMap() with { PlayerStart = SmallMap().PlayerStart with { MoveSpeed = invalid } }), "Invalid movement speed accepted");
    Reject(() => Compile(SmallMap() with { PlayerStart = SmallMap().PlayerStart with { RotSpeed = invalid } }), "Invalid rotation speed accepted");
}
Reject(() => Compile(objects: new(definitions) { [2] = spriteDef with { PassageId = 1 } }), "Non-passage reference accepted");
Reject(() => Compile(objects: new(definitions) { [1] = wall with { PassageId = 2 } }), "Wall non-passage reference accepted");
Reject(() => Compile(objects: new(definitions) { [1] = wall with { PassageId = 999 } }), "Wall missing passage reference accepted");
Reject(() => Compile(objects: new(definitions) { [3] = groupDef with { Sprites = [new() { SpriteId = 3, Offset = new() { X = 0, Y = 0 } }] } }), "Nested group accepted");
Reject(() => Compile(objects: new(definitions) { [3] = groupDef with { Sprites = [new() { SpriteId = 999, Offset = new() { X = 0, Y = 0 } }] } }), "Missing child accepted");
Reject(() => Compile(SmallMap(3), new(definitions) { [3] = groupDef with { Sprites = [new() { SpriteId = 2, Offset = new() { X = 100, Y = 0 } }] } }), "Outside sprite accepted");
Reject(() => Compile(objects: new(definitions) { [4] = spriteDef with { Id = 4, Texture = "textures/other.png" } }), "Texture slot conflict accepted");
Reject(() => Compile(objects: new(definitions) { [2] = spriteDef with { TextureLayer = 256 } }), "Unsupported texture layer accepted");
var emptyGroup = Compile(SmallMap(3), new(definitions) { [3] = groupDef with { Sprites = [] } });
Check(emptyGroup.RendererMap.Sprites.Count == 0, "Empty group invalid");

const string passagePath = "objects/passages/passage-mossy-floor-wood-ceiling.json";
using var wrongId = new HttpClient(new FileHandler(webRoot, path => path == passagePath
    ? "{\"type\":\"passage\",\"id\":999,\"raycastTransparent\":true,\"collides\":false}" : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<InvalidDataException>(wrongId, "Index/object ID mismatch accepted");
using var unknownType = new HttpClient(new FileHandler(webRoot, path => path == passagePath ? "{\"type\":\"unknown\",\"id\":0}" : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<JsonException>(unknownType, "Unknown type accepted");
using var missingFlags = new HttpClient(new FileHandler(webRoot, path => path == passagePath ? "{\"type\":\"passage\",\"id\":0}" : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<JsonException>(missingFlags, "Required flags were not validated");
using var malformed = new HttpClient(new FileHandler(webRoot, path => path == "maps/level1.json" ? "{}" : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<JsonException>(malformed, "Malformed level accepted");
string ModifiedMap(Action<System.Text.Json.Nodes.JsonObject> modify)
{
    var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(webRoot, "maps/level1.json")))!.AsObject();
    modify(json);
    return json.ToJsonString();
}
var wallDependencyJson = ModifiedMap(json => json["objects"]!["999"] = "../objects/passages/wall-only-passage.json");
var wallDependencyHandler = new FileHandler(webRoot, path => path switch
{
    "maps/level1.json" => wallDependencyJson,
    "objects/walls/wall-eagle.json" => JsonSerializer.Serialize<MapObjectDefinition>(wall with { PassageId = 999 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    "objects/passages/wall-only-passage.json" => JsonSerializer.Serialize<MapObjectDefinition>(Passage(999) with { FloorTexture = "textures/wall-only-floor.png" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    _ => null
});
using var wallDependencyHttp = new HttpClient(wallDependencyHandler) { BaseAddress = http.BaseAddress };
var wallDependencyMap = await GameMap.LoadAsync(wallDependencyHttp, "maps/level1.json", 64, 64);
Check(wallDependencyMap.Objects.ContainsKey(999) && wallDependencyHandler.Requests.Count(path => path.EndsWith("wall-only-passage.json")) == 1,
    "Wall-only passage dependency was not loaded exactly once");
Check(wallDependencyMap.RendererMap.Textures.Layers.Values.Contains("textures/wall-only-floor.png"), "Wall-only passage surfaces were not compiled");
var missingWallPassage = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(webRoot, "objects/walls/wall-eagle.json")))!.AsObject();
missingWallPassage.Remove("passageId");
using var missingWallPassageHttp = new HttpClient(new FileHandler(webRoot, path => path == "objects/walls/wall-eagle.json" ? missingWallPassage.ToJsonString() : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<JsonException>(missingWallPassageHttp, "Wall without passageId was accepted");
var unusedJson = ModifiedMap(json => json["objects"]!["999"] = "../objects/does-not-exist.json");
var unusedHandler = new FileHandler(webRoot, path => path == "maps/level1.json" ? unusedJson : null);
using var unusedHttp = new HttpClient(unusedHandler) { BaseAddress = http.BaseAddress };
var unusedMap = await GameMap.LoadAsync(unusedHttp, "maps/level1.json", 64, 64);
Check(!unusedMap.Objects.ContainsKey(999) && unusedHandler.Requests.All(path => !path.Contains("does-not-exist")), "Unused indexed object was loaded");
var missingJson = ModifiedMap(json => json["objects"]!.AsObject().Remove("0"));
using var missingIndex = new HttpClient(new FileHandler(webRoot, path => path == "maps/level1.json" ? missingJson : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<InvalidDataException>(missingIndex, "Missing index entry accepted");
var notFoundJson = ModifiedMap(json => json["objects"]!["0"] = "../objects/not-found.json");
using var notFound = new HttpClient(new FileHandler(webRoot, path => path == "maps/level1.json" ? notFoundJson : null)) { BaseAddress = http.BaseAddress };
await RejectLoad<HttpRequestException>(notFound, "Missing definition accepted");
var unusedTexture = Compile(objects: new(definitions) { [99] = wall with { Id = 99, Textures = new() { North = "textures/unused.png" } } });
Check(!unusedTexture.RendererMap.Textures.Layers.Values.Contains("textures/unused.png"), "Unused texture included in catalog");
var childSurfaces = Compile(SmallMap(3), new(definitions)
{
    [5] = Passage(5) with { FloorTexture = "textures/child-only-floor.png", CeilingTexture = "textures/child-only-ceiling.png" },
    [2] = spriteDef with { PassageId = 5 }
});
Check(childSurfaces.RendererMap.Textures.Layers.Values.All(path => path == null || !path.Contains("child-only")), "Unused child surfaces included in catalog");

using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
await RejectLoad<OperationCanceledException>(http, "Cancellation ignored", cancellation.Token);
foreach (bool wallsPresent in new[] { false, true })
foreach (float configuredSpeed in new[] { 0f, 2f, 5f })
foreach (float milliseconds in new[] { 16f, 33f, 100f })
for (int heading = 0; heading < 12; heading++)
for (int keys = 0; keys < 16; keys++)
{
    var level = SmallMap() with { PlayerStart = SmallMap().PlayerStart with { MoveSpeed = configuredSpeed } };
    if (wallsPresent) level.WorldMap[3][2] = 1;
    var navigation = Compile(level).NavigationMap;
    var start = wallsPresent ? new Vector2(2.99f, 2.5f) : new Vector2(2.5f, 2.5f);
    var direction = new Vector2(MathF.Cos(heading * MathF.PI / 6), MathF.Sin(heading * MathF.PI / 6)) * 1.001f;
    var player = new WolfNET.Game.Core.ObjectBase { Transform = new Transform { Position = start, Rotation = direction } };
    var tracker = new WolfNET.Game.InputEvents.KeyTracker { KeyState = new()
    {
        Forward = (keys & 1) != 0, Backward = (keys & 2) != 0,
        StrafeLeft = (keys & 4) != 0, StrafeRight = (keys & 8) != 0
    } };
    var camera = new WolfNET.Game.Player.Components.CameraComponent { Plane = new Vector2(direction.Y, -direction.X) * 0.66f };
    var controller = new WolfNET.Game.Player.Components.PlayerController(tracker, new(), navigation, camera, level.PlayerStart, player);
    controller.Update(milliseconds);
    var speed = Vector2.Distance(start, player.Transform.Position) / (milliseconds / 1000);
    Check(speed <= configuredSpeed + 0.0001f, "Movement exceeded the configured speed cap");
    if (!wallsPresent)
    {
        bool active = tracker.KeyState.Forward != tracker.KeyState.Backward || tracker.KeyState.StrafeLeft != tracker.KeyState.StrafeRight;
        Check(MathF.Abs(speed - (active ? configuredSpeed : 0)) < 0.0001f, "Combined movement speed changed");
    }
    Check(navigation.IsWalkable((int)player.Transform.Position.X, (int)player.Transform.Position.Y), "Controller entered a blocked cell");
}
foreach (float configuredSpeed in new[] { 0f, 0.5f, 2f, 4f })
foreach (float milliseconds in new[] { 16f, 33f, 100f })
foreach (float pixels in new[] { -10000f, -1f, 0f, 1f, 10000f })
foreach (int keyboard in new[] { -1, 0, 1 })
{
    var level = Compile(SmallMap() with { PlayerStart = SmallMap().PlayerStart with { RotSpeed = configuredSpeed } });
    var player = new WolfNET.Game.Core.ObjectBase
    {
        Transform = new Transform { Position = new Vector2(2.5f), Rotation = Vector2.UnitX }
    };
    var tracker = new WolfNET.Game.InputEvents.KeyTracker
    {
        KeyState = new() { Left = keyboard > 0, Right = keyboard < 0 }
    };
    var mouse = new WolfNET.Game.InputEvents.MouseTracker { HorizontalMovement = pixels };
    var camera = new WolfNET.Game.Player.Components.CameraComponent { Plane = new Vector2(0, -0.66f) };
    var controller = new WolfNET.Game.Player.Components.PlayerController(tracker, mouse, level.NavigationMap, camera, level.PlayerStart, player);
    controller.Update(milliseconds);
    var limit = configuredSpeed * milliseconds / 1000f;
    var expected = Math.Clamp(-pixels * 0.0025f + keyboard * limit, -limit, limit);
    var angle = MathF.Atan2(player.Transform.Rotation.Y, player.Transform.Rotation.X);
    Check(MathF.Abs(angle - expected) < 0.00001f, "Mouse/keyboard turning did not respect the configured rate");
    Check(MathF.Abs(camera.Plane.Length() - 0.66f) < 0.00001f &&
          MathF.Abs(Vector2.Dot(camera.Plane, player.Transform.Rotation)) < 0.00001f, "Turning changed camera field of view");
    tracker.KeyState = new();
    controller.Update(milliseconds);
    Check(MathF.Abs(MathF.Atan2(player.Transform.Rotation.Y, player.Transform.Rotation.X) - angle) < 0.00001f,
        "Mouse movement was applied more than once");
}
var editorSource = new WolfNET.Editor.MapWorkspace(webRoot);
var editorDocument = editorSource.Load("showcase") with { Name = "editor-roundtrip" };
Check(editorSource.Maps().Contains("showcase"), "Editor map catalog missing showcase");
Reject(() => editorSource.Load("../showcase"), "Editor accepted a path outside maps");
Reject(() => editorSource.Validate(editorDocument with { Name = "CON" }), "Editor accepted a reserved filename");
Reject(() => editorSource.Validate(editorDocument with
{
    Objects = new(editorDocument.Objects.Where(pair => pair.Key != 11))
}), "Editor accepted a missing passage dependency");
var editorRoot = Path.Combine(Path.GetTempPath(), "wolfnet-editor-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(editorRoot, "maps"));
try
{
    var store = new WolfNET.Editor.MapWorkspace(editorRoot);
    await store.SaveAsync(editorDocument, false, CancellationToken.None);
    var persisted = store.Load("editor-roundtrip");
    Check(persisted.Map.WorldMap.SelectMany(column => column).SequenceEqual(editorDocument.Map.WorldMap.SelectMany(column => column)), "Editor save changed grid orientation");
    Check(persisted.Map.PlayerStart == editorDocument.Map.PlayerStart, "Editor save changed spawn settings");
    Check(persisted.Objects.Count == editorDocument.Objects.Count, "Editor lost object dependencies");
    Check(persisted.Map.Objects.Values.All(path => path.StartsWith("../objects/editor/editor-roundtrip/")), "Editor did not isolate object snapshots");
    using var savedHttp = new HttpClient(new FileHandler(editorRoot)) { BaseAddress = http.BaseAddress };
    var engineRoundTrip = await GameMap.LoadAsync(savedHttp, "maps/editor-roundtrip.json", 64, 64);
    Check(engineRoundTrip.RendererMap.Sprites.Count == showcase.RendererMap.Sprites.Count, "Editor save cannot be read by the engine");
    var beforeOverwrite = File.ReadAllText(Path.Combine(editorRoot, "maps/editor-roundtrip.json"));
    try { await store.SaveAsync(editorDocument, false, CancellationToken.None); throw new Exception("Editor silently overwrote a map"); }
    catch (InvalidDataException) { assertions++; }
    Check(File.ReadAllText(Path.Combine(editorRoot, "maps/editor-roundtrip.json")) == beforeOverwrite, "Rejected overwrite changed the map");
    var modifiedObjects = new Dictionary<int, MapObjectDefinition>(editorDocument.Objects)
    {
        [11] = (PassageDefinition)editorDocument.Objects[11] with { FloorTexture = "textures/mossy.png" }
    };
    await store.SaveAsync(editorDocument with { Objects = modifiedObjects }, true, CancellationToken.None);
    var edited = store.Load("editor-roundtrip");
    Check(((PassageDefinition)edited.Objects[11]).FloorTexture == "textures/mossy.png", "Editor overwrite did not persist definition edits");
    Check(((PassageDefinition)editorSource.Load("showcase").Objects[11]).FloorTexture == "textures/colorstone.png", "Editor edited shared engine objects");
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(store.Export(editorDocument)));
    Check(zip.GetEntry("maps/editor-roundtrip.json") != null, "Editor export missing map");
    Check(zip.Entries.Count(entry => entry.FullName.EndsWith(".json")) == editorDocument.Objects.Count + 1, "Editor export lost dependency files");
    foreach (var path in persisted.Map.Objects.Values)
        Check(zip.GetEntry(path[3..]) != null, "Editor export index points outside bundle");
    var unsafeEditorMap = SmallMap() with { Objects = new() { [1] = "../../outside.json" } };
    File.WriteAllText(Path.Combine(editorRoot, "maps/unsafe.json"), JsonSerializer.Serialize(unsafeEditorMap, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Reject(() => store.Load("unsafe"), "Editor followed a path outside engine assets");
}
finally { Directory.Delete(editorRoot, true); }
Console.WriteLine($"PASS: {assertions} assertions (migration, flags, groups, surfaces, navigation, revisions, loading failures, movement, rotation, and editor round trips).");

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
