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
        var texture = "textures/wolf/" + originalWallTextures[originalGrid[x][y] - 1] + ".png";
        Check(new[] { faces.North, faces.East, faces.South, faces.West }.All(layer => map.RendererMap.Textures.Layers[layer] == texture), "Migrated wall texture changed");
    }
    else
        Check(map.RendererMap.Textures.Layers[cell.FloorTextureLayer] == "textures/wolf/mossy.png" &&
              map.RendererMap.Textures.Layers[cell.CeilingTextureLayer] == "textures/wolf/wood.png", "Migrated passage surfaces changed");
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
Check(map.RendererMap.Textures.Layers[material.North] == "textures/wolf/colorstone.png", "Wall material changed");
Check(material.North == material.East && material.East == material.South && material.South == material.West, "Wall faces changed");

Check(fileHandler.Requests.All(path => !path.Contains("showcase") && !path.EndsWith("index.json")), "Loaded another map or global index");
Check(fileHandler.Requests.Count == map.Objects.Count + 1, "An object was loaded more than once");
Check(map.RendererMap.Textures.Layers.Values.All(path => path == null || !path.Contains("showcase-missing")), "Unrelated map textures leaked");
var showcase = await GameMap.LoadAsync(http, "maps/showcase.json", 64, 64);
Check(showcase.RendererMap.Width == 34 && showcase.RendererMap.Height == 25 && showcase.Revision == 1, "Showcase dimensions/revision invalid");
Check(showcase.PlayerStartPosition == new Vector2(18.5f, 22.5f) && showcase.PlayerStartDirection == new Vector2(0, -1), "Showcase spawn invalid");
Check(showcase.RendererMap.Sprites.Count == 76, "Showcase sprite demonstrations missing");
RenderCell ShowcaseCell(int x, int y) => showcase.RendererMap.Cells[y * showcase.RendererMap.Width + x];
Check(showcase.RendererMap.Sky?.Texture == "textures/skyboxes/skybox.jpg", "Showcase sky panorama missing");
Check(ShowcaseCell(30, 18).Flags.HasFlag(RenderCellFlags.SkyCeiling), "Open sky showroom missing");
Check(!ShowcaseCell(26, 18).Flags.HasFlag(RenderCellFlags.SkyCeiling) &&
    showcase.RendererMap.Textures.Layers[ShowcaseCell(26,18).CeilingTextureLayer] == "textures/wolf/mesh.png", "Mesh showroom ceiling missing");
Check(showcase.Objects.Values.Where(value => value is SpriteDefinition or SpriteGroupDefinition).All(value => value.RaycastTransparent), "Showcase contains ray-blocking sprites");
Check(!showcase.Objects.ContainsKey(113) && !showcase.Objects.ContainsKey(122), "Removed sprite examples still loaded");
foreach (var x in new[] { 15, 17, 19, 21 })
{
    var cell = ShowcaseCell(x, 6);
    Check(showcase.RendererMap.Textures.Layers[cell.FloorTextureLayer] == "textures/wolf/colorstone.png" &&
          showcase.RendererMap.Textures.Layers[cell.CeilingTextureLayer] == "textures/wolf/wood.png", "Lane wall surfaces missing");
}

foreach (var (x, blocksRays, walkable) in new[] { (15, true, false), (17, true, true), (19, false, false), (21, false, true) })
{
    Check(ShowcaseCell(x, 6).Flags.HasFlag(RenderCellFlags.StopsRay) == blocksRays, "Showcase ray-blocking lane incorrect");
    Check(showcase.NavigationMap.IsWalkable(x, 6) == walkable, "Showcase collision lane incorrect");
}
Check(!showcase.NavigationMap.IsWalkable(3, 21) && showcase.NavigationMap.IsWalkable(4, 21), "Showcase group walkability incorrect");
foreach (var sprite in showcase.RendererMap.Sprites)
{
    var definition = showcase.Objects.Values.OfType<SpriteDefinition>().First(value => value.TextureLayer == sprite.TextureLayer);
    if (definition.Texture is "textures/wolf/barrel.png" or "textures/wolf/greenlight.png" or "textures/wolf/pillar.png")
        Check(showcase.NavigationMap.IsWalkable((int)sprite.Position.X, (int)sprite.Position.Y) ==
            (definition.Texture != "textures/wolf/pillar.png"),
            "Showcase sprite walkability disagrees with its appearance");
}
Check(!showcase.NavigationMap.IsWalkable(9,20) && showcase.NavigationMap.IsWalkable(9,20,inAir:true), "Showcase jumpable columns collision is incorrect");
Check(ShowcaseCell(19, 6).Flags.HasFlag(RenderCellFlags.Wall) && ShowcaseCell(21, 6).Flags.HasFlag(RenderCellFlags.Wall), "Showcase transparent wall demonstrations missing");
Check(showcase.NavigationMap.IsWalkable(13, 8) && !ShowcaseCell(13, 8).Flags.HasFlag(RenderCellFlags.StopsRay) &&
      !ShowcaseCell(13, 8).Flags.HasFlag(RenderCellFlags.Wall), "Open floor outside the test lanes is blocked");
Check(!showcase.Objects.ContainsKey(26), "Stray invisible passage barrier is still loaded");
var fourFaces = showcase.RendererMap.Materials[(int)ShowcaseCell(18, 17).MaterialIndex];
Check(new[] { fourFaces.North, fourFaces.East, fourFaces.South, fourFaces.West }.Distinct().Count() == 4, "Showcase wall needs four different faces");
Check(ShowcaseCell(15, 16).FloorTextureLayer != ShowcaseCell(15, 15).FloorTextureLayer, "Showcase floor variation missing");
Check(ShowcaseCell(3, 7).FloorTextureLayer == TextureCatalog.MissingLayer && ShowcaseCell(3, 7).CeilingTextureLayer == TextureCatalog.MissingLayer, "Showcase missing surface fields not demonstrated");
Check(showcase.RendererMap.Materials[(int)ShowcaseCell(6, 4).MaterialIndex].West == TextureCatalog.MissingLayer, "Showcase missing wall face not demonstrated");
Check(showcase.RendererMap.Sprites.Any(sprite => sprite.TextureLayer == 11) && showcase.RendererMap.Sprites.Any(sprite => sprite.TextureLayer == 12), "Showcase missing sprites not demonstrated");
foreach (var (x, y, jump) in new[] { (26, 8, true), (26, 4, false), (30, 4, true), (30, 8, false) })
{
    Check(!showcase.NavigationMap.IsWalkable(x,y), "Corridor obstacle allows standing movement");
    Check(showcase.NavigationMap.IsWalkable(x,y,inAir:true) == jump &&
          showcase.NavigationMap.IsWalkable(x,y,crouching:true) == !jump, "Corridor obstacle requires the wrong movement");
    Check(showcase.NavigationMap.IsWalkable(x,y-1) && showcase.NavigationMap.IsWalkable(x,y+1), "Corridor obstacle lacks approach/landing space");
    Check(!showcase.NavigationMap.IsWalkable(x-1,y) && !showcase.NavigationMap.IsWalkable(x+1,y), "Corridor obstacle can be bypassed sideways");
}
Check(showcase.NavigationMap.IsWalkable(24,11), "Obstacle corridor entrance is blocked");
var car = showcase.RendererMap.Sprites.Single(sprite => sprite.Atlas != null);
Check(car.Position == new Vector2(30.5f,18.5f) && !showcase.NavigationMap.IsWalkable(30,18), "Directional car placement/collision incorrect");
Check(car.Scale == Vector2.One && Math.Abs(car.Elevation + .08f) < .00001f && ShowcaseCell(30,18).Flags.HasFlag(RenderCellFlags.SkyCeiling), "Car scale/elevation or open-sky passage incorrect");
Check(car.Atlas!.Frames.Length == 8 && car.Atlas.Directions == 8 && car.RotationDegrees == -90, "Car atlas/facing lost");
for (int direction = 0; direction < 8; direction++)
{
    float angle = (direction * 45 - 90) * MathF.PI / 180;
    var observer = car.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 2;
    Check(car.ViewFrame(observer) == direction, "Directional car selected the wrong view");
    Check(car.ViewLayer(observer) == 32 + direction, "Directional view did not use its reserved layer");
    var frame = car.Atlas.Frames[direction];
    Check(showcase.RendererMap.Textures.Frames[(uint)(32+direction)] == frame &&
        showcase.RendererMap.Textures.Layers[(uint)(32+direction)] == "textures/race/car-game-atlas.png", "Atlas frame was pruned from scene textures");
    Check(Math.Abs(car.ViewScale(observer).X - 1f * frame.Width / frame.Height) < .00001f, "Atlas aspect ratio lost");
}
Check(car.ViewFrame(car.Position) == 0, "Coincident observer produced an invalid view");
Check((car with { RotationDegrees = 270 }).ViewFrame(car.Position + new Vector2(0,-1)) == 0, "Direction angle wrapping failed");
Check((car with { Atlas = null }).ViewLayer(car.Position + Vector2.One) == car.TextureLayer, "Ordinary sprite selection changed");
var reachable = new HashSet<(int X, int Y)> { (18, 22) };
var frontier = new Queue<(int X, int Y)>(); frontier.Enqueue((18, 22));
while (frontier.TryDequeue(out var cell))
foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
{
    var next = (X: cell.X + dx, Y: cell.Y + dy);
    if ((showcase.NavigationMap.IsWalkable(next.X, next.Y, inAir:true) ||
         showcase.NavigationMap.IsWalkable(next.X, next.Y, crouching:true)) && reachable.Add(next)) frontier.Enqueue(next);
}
for (int x = 0; x < showcase.RendererMap.Width; x++)
for (int y = 0; y < showcase.RendererMap.Height; y++)
    Check(!showcase.NavigationMap.IsWalkable(x, y) || reachable.Contains((x, y)), $"Unreachable showcase cell at {x},{y}");

PassageDefinition Passage(int id, bool transparent = true, bool collides = false) => new()
{ Id = id, RaycastTransparent = transparent, Collides = collides, FloorTexture = "textures/wolf/mossy.png", CeilingTexture = "textures/wolf/wood.png" };
var wall = new WallDefinition { Id = 1, RaycastTransparent = false, Collides = true, PassageId = 0, Textures = new WallTextures
{ North = "textures/north.png", East = "textures/east.png", South = "textures/south.png" } };
var spriteDef = new SpriteDefinition { Id = 2, RaycastTransparent = false, Collides = true, PassageId = 0, Texture = "textures/wolf/barrel.png", TextureLayer = 8 };
var groupDef = new SpriteGroupDefinition { Id = 3, RaycastTransparent = true, Collides = false, PassageId = 0,
    Sprites = [new() { SpriteId = 2, Offset = new() { X = -0.25f, Y = 0 } }, new() { SpriteId = 2, Offset = new() { X = 0.75f, Y = 0.1f } }] };
var definitions = new Dictionary<int, MapObjectDefinition> { [0] = Passage(0), [1] = wall, [2] = spriteDef, [3] = groupDef };
MapDefinition SmallMap(int center = 0) => new()
{
    WorldMap = [[1, 1, 1, 1, 1], [1, 0, 0, 0, 1], [1, 0, center, 0, 1], [1, 0, 0, 0, 1], [1, 1, 1, 1, 1]],
    PlayerStart = new() { X = 1.5f, Y = 1.5f, DirectionX = -1, DirectionY = 0, MoveSpeed = 5, RotSpeed = 2, CollisionRadius = 0.2f }
};
GameMap Compile(MapDefinition? definition = null, Dictionary<int, MapObjectDefinition>? objects = null) => new(definition ?? SmallMap(), objects ?? definitions, 64, 64);
var openPassages = new Dictionary<int, MapObjectDefinition>(definitions)
{
    [0] = Passage(0) with { CeilingMode = "sky", CeilingTexture = "textures/unused-roof.png" }
};
Reject(() => Compile(objects: openPassages), "Open ceiling accepted without map sky");
var atlasDefinition = spriteDef with { TextureLayer = 32, Atlas = car.Atlas, RotationDegrees = -90 };
var atlasObjects = new Dictionary<int, MapObjectDefinition>(definitions) { [2] = atlasDefinition };
var atlasGroup = Compile(SmallMap() with { WorldMap = [[1,1,1,1,1],[1,0,0,0,1],[1,0,3,0,1],[1,0,0,0,1],[1,1,1,1,1]] }, atlasObjects);
Check(atlasGroup.RendererMap.Sprites.All(sprite => sprite.Atlas == car.Atlas && sprite.RotationDegrees == -90), "Group member lost atlas/facing");
Check(Compile(objects: atlasObjects).RendererMap.Textures.Frames.Count == 0, "Unused atlas layers loaded");
foreach (var invalidAtlas in new SpriteAtlas[] {
    new() { Frames = [] }, new() { Frames = car.Atlas.Frames, Directions = 9 },
    new() { Frames = [new() { X=-1,Y=0,Width=1,Height=1 }] },
    new() { Frames = [new() { X=0,Y=0,Width=0,Height=1 }] } })
    Reject(() => Compile(objects: new(definitions) { [2] = atlasDefinition with { Atlas = invalidAtlas } }), "Invalid atlas accepted");
Reject(() => Compile(objects: new(definitions) { [2] = atlasDefinition with { TextureLayer = 250 } }), "Atlas texture overflow accepted");
Reject(() => Compile(objects: new(atlasObjects) { [4] = spriteDef with { Id = 4, TextureLayer = 33 } }), "Overlapping atlas slot accepted");
Reject(() => Compile(objects: new(atlasObjects) { [4] = atlasDefinition with { Id = 4, Atlas = car.Atlas with { Frames = car.Atlas.Frames.Reverse().ToArray() } } }), "Conflicting atlas rectangles accepted");
Reject(() => Compile(objects: new(definitions) { [2] = spriteDef with { RotationDegrees = float.NaN } }), "Invalid sprite facing accepted");
var skyMap = SmallMap() with { Sky = new() { Texture = "textures/skyboxes/skybox.jpg", RotationDegrees = 45 } };
var skyLevel = Compile(skyMap, openPassages);
Check(skyLevel.RendererMap.Sky == skyMap.Sky && skyLevel.RendererMap.Cells[12].Flags.HasFlag(RenderCellFlags.SkyCeiling),
    "Map sky and open-ceiling flags were not compiled");
Check(!skyLevel.RendererMap.Textures.Layers.Values.Contains("textures/unused-roof.png"), "Open ceiling loaded an unused ceiling texture");
var skyInherited = Compile(skyMap, new(openPassages) { [1] = wall with { PassageId = 0 } });
Check(skyInherited.RendererMap.Cells[0].Flags.HasFlag(RenderCellFlags.SkyCeiling), "Walls did not inherit passage ceiling mode");
Reject(() => Compile(skyMap with { Sky = skyMap.Sky! with { RotationDegrees = float.NaN } }), "Nonfinite sky rotation accepted");
Reject(() => Compile(skyMap with { Sky = skyMap.Sky! with { Texture = "" } }), "Empty sky texture accepted");
Reject(() => Compile(objects: new(definitions) { [0] = Passage(0) with { CeilingMode = "invalid" } }), "Unknown ceiling mode accepted");
foreach (bool transparent in new[] { false, true })
foreach (bool collides in new[] { false, true })
foreach (bool jumpable in new[] { false, true })
foreach (bool crouchable in new[] { false, true })
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
    var objects = new Dictionary<int, MapObjectDefinition>(definitions) { [testId] = value with { Jumpable = jumpable, Crouchable = crouchable } };
    var compiled = Compile(SmallMap(testId), objects);
    Check(compiled.NavigationMap.IsWalkable(2, 2) == !collides, "Collision flag ignored");
    Check(compiled.NavigationMap.IsWalkable(2, 2, inAir: true) == (!collides || jumpable), "Jumpable flag ignored or inherited incorrectly");
    Check(compiled.NavigationMap.IsWalkable(2, 2, crouching: true) == (!collides || crouchable), "Crouchable flag ignored or inherited incorrectly");
    Check(compiled.NavigationMap.IsWalkable(2, 2, inAir: true, crouching: true) == (!collides || jumpable), "Airborne crouching bypassed a non-jumpable obstacle");
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
Check(group.RendererMap.Textures.Layers[8] == "textures/wolf/barrel.png", "Explicit sprite layer not reserved");
var groupPassage = Compile(SmallMap(3), new(definitions)
{
    [4] = Passage(4, false, true) with { FloorTexture = "textures/wolf/bluestone.png", CeilingTexture = "textures/wolf/greystone.png" },
    [3] = groupDef with { PassageId = 4 }
});
Check(groupPassage.NavigationMap.IsWalkable(2, 2) && !groupPassage.RendererMap.Cells[12].Flags.HasFlag(RenderCellFlags.StopsRay), "Passage flags overrode group flags");
Check(groupPassage.RendererMap.Textures.Layers[groupPassage.RendererMap.Cells[12].FloorTextureLayer] == "textures/wolf/bluestone.png" &&
      groupPassage.RendererMap.Textures.Layers[groupPassage.RendererMap.Cells[12].CeilingTextureLayer] == "textures/wolf/greystone.png", "Group did not use its own passage reference");
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
    var start = wallsPresent ? new Vector2(2.79f, 2.5f) : new Vector2(2.5f, 2.5f);
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
Vector2 MovePlayer(GameMap level, Vector2 position, Vector2 direction, float milliseconds, float speed = 5)
{
    var player = new WolfNET.Game.Core.ObjectBase
    {
        Transform = new Transform { Position = position, Rotation = Vector2.Normalize(direction) }
    };
    var keys = new WolfNET.Game.InputEvents.KeyTracker { KeyState = new() { Forward = true } };
    var controller = new WolfNET.Game.Player.Components.PlayerController(keys, new(), level.NavigationMap,
        new(), level.PlayerStart with { MoveSpeed = speed }, player);
    controller.Update(milliseconds);
    return player.Transform.Position;
}

var collisionMap = SmallMap();
collisionMap.WorldMap[3][2] = 1;
var collisionLevel = Compile(collisionMap);
Check(map.PlayerStart.CollisionRadius == 0.15f && showcase.PlayerStart.CollisionRadius == 0.15f,
    "Supplied maps did not load the configured collision radius");
Check(new PlayerStartDefinition { X = 1.5f, Y = 1.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 5, RotSpeed = 3 }.CollisionRadius == 0.15f,
    "Legacy maps did not retain the default collision radius");
foreach (float radius in new[] { 0.1f, 0.15f, 0.3f })
{
    var configuredLevel = Compile(collisionMap with { PlayerStart = collisionMap.PlayerStart with { CollisionRadius = radius } });
    var result = MovePlayer(configuredLevel, new(2.5f, 2.5f), Vector2.UnitX, 1000);
    Check(MathF.Abs(result.X - (3 - radius)) < 0.0001f, "Controller ignored the map's collision radius");
}
foreach (float invalid in new[] { 0f, -1f, 0.5f, float.NaN, float.PositiveInfinity })
    Reject(() => Compile(SmallMap() with { PlayerStart = SmallMap().PlayerStart with { CollisionRadius = invalid } }),
        "Invalid collision radius accepted");
var overlapSlide = MovePlayer(collisionLevel, new(2.800001f, 2.2f), Vector2.UnitY, 100);
Check(overlapSlide.X < 2.8f && overlapSlide.Y > 2.69f,
    "A tiny initial wall overlap prevented sliding");
var aroundCorner = MovePlayer(collisionLevel, new(2.88f, 1.84f), Vector2.UnitX, 100);
Check(aroundCorner.X > 3 && aroundCorner.Y < 1.81f,
    "Moving directly into a rounded corner pinned the player instead of sliding");
var contact = MovePlayer(collisionLevel, new(2.5f, 2.5f), Vector2.UnitX, 1000);
Check(MathF.Abs(contact.X - 2.8f) < 0.0001f && contact.Y == 2.5f,
    "Circle failed to stop at its radius from a wall during fast movement");
var retreat = MovePlayer(collisionLevel, contact, -Vector2.UnitX, 100);
var leaveSideways = MovePlayer(collisionLevel, contact, Vector2.UnitY, 100);
Check(retreat.X < 2.31f && leaveSideways.Y > 2.99f,
    "Player could not retreat or strafe after a direct obstacle collision");
var slide = MovePlayer(collisionLevel, new(2.8f, 2.2f), Vector2.One, 100);
Check(MathF.Abs(slide.X - 2.8f) < 0.0001f && slide.Y > 2.5f,
    "Circle did not slide along a wall");
var corner = MovePlayer(collisionLevel, new(2.5f, 1.85f), Vector2.UnitX, 100);
Check(corner.X > 2.8f && corner.X < 3 && Vector2.Distance(corner, new(3, 2)) >= 0.1999f,
    "Circle corner clearance was incorrect or behaved like a square");
collisionMap.WorldMap[2][3] = 1;
var diagonalContact = MovePlayer(Compile(collisionMap), new(2.5f), Vector2.One, 1000);
Check(diagonalContact.X <= 2.8001f && diagonalContact.Y <= 2.8001f,
    "Player squeezed through a diagonal gap between solid cells");
var corridorMap = SmallMap();
for (int y = 1; y <= 3; y++) corridorMap.WorldMap[1][y] = corridorMap.WorldMap[3][y] = 1;
corridorMap = corridorMap with { PlayerStart = corridorMap.PlayerStart with { X = 2.5f, Y = 1.5f } };
var corridorEnd = MovePlayer(Compile(corridorMap), new(2.5f, 1.5f), Vector2.UnitY, 1000);
Check(corridorEnd.X == 2.5f && MathF.Abs(corridorEnd.Y - 3.8f) < 0.0001f,
    "Circle could not traverse a one-cell-wide corridor or crossed its boundary");
var leftContact = MovePlayer(Compile(), new(1.5f, 2.5f), -Vector2.UnitX, 1000);
Check(MathF.Abs(leftContact.X - 1.2f) < 0.0001f,
    "Circle failed to keep clearance from the opposite map boundary");
foreach (bool transparent in new[] { false, true })
foreach (bool collides in new[] { false, true })
{
    var level = Compile(SmallMap(2), new(definitions)
    {
        [2] = spriteDef with { RaycastTransparent = transparent, Collides = collides }
    });
    var result = MovePlayer(level, new(1.5f, 2.5f), Vector2.UnitX, 1000, speed: 1);
    Check(MathF.Abs(result.X - (collides ? 1.8f : 2.5f)) < 0.0001f,
        "Circular collision did not respect sprite collision flags independently of ray transparency");
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
foreach (float configuredSpeed in new[] { 0f, 0.5f, 3f })
foreach (float milliseconds in new[] { 0f, 16f, 100f })
foreach (float pixels in new[] { -10000f, -1f, 0f, 1f, 10000f })
foreach (float initialPitch in new[] { -MathF.PI / 6, 0f, MathF.PI / 6 })
{
    var player = new WolfNET.Game.Core.ObjectBase { Transform = new Transform { Position = new(2.5f), Rotation = Vector2.UnitX } };
    var mouse = new WolfNET.Game.InputEvents.MouseTracker { VerticalMovement = pixels };
    var camera = new WolfNET.Game.Player.Components.CameraComponent { Plane = new(0, -0.66f), Pitch = initialPitch };
    var level = Compile();
    var controller = new WolfNET.Game.Player.Components.PlayerController(new(), mouse, level.NavigationMap, camera,
        level.PlayerStart with { RotSpeed = configuredSpeed }, player);
    controller.Update(milliseconds);
    var limit = configuredSpeed * milliseconds / 1000;
    var expected = Math.Clamp(initialPitch + Math.Clamp(-pixels * 0.0025f, -limit, limit), -MathF.PI / 6, MathF.PI / 6);
    Check(MathF.Abs(camera.Pitch - expected) < 0.00001f, "Vertical mouse look ignored speed, direction, or pitch limits");
    Check(player.Transform.Position == new Vector2(2.5f) && player.Transform.Rotation == Vector2.UnitX && camera.Plane == new Vector2(0, -0.66f),
        "Vertical looking changed horizontal movement or field of view");
    controller.Update(100);
    Check(camera.Pitch == expected, "Vertical movement continued after the mouse stopped");
}

MapDefinition JumpMap(float length = 4, float multiplier = 2, float rise = 0.25f) => new()
{
    WorldMap = Enumerable.Range(0, 9).Select(x => Enumerable.Range(0, 7)
        .Select(y => x == 0 || x == 8 || y == 0 || y == 6 ? 1 : x == 4 && y == 3 ? 10 : 0).ToArray()).ToArray(),
    PlayerStart = SmallMap().PlayerStart with { X = 2.5f, Y = 3.5f, MoveSpeed = 4, JumpLength = length, JumpRise = rise, JumpSpeedMultiplier = multiplier }
};
GameMap JumpLevel(MapDefinition? definition = null, bool jumpable = true) => Compile(definition ?? JumpMap(),
    new(definitions) { [10] = Passage(10, collides: true) with { Jumpable = jumpable } });
(WolfNET.Game.Core.ObjectBase Player, WolfNET.Game.InputEvents.KeyTracker Keys,
    WolfNET.Game.Player.Components.CameraComponent Camera, WolfNET.Game.Player.Components.PlayerController Controller,
    WolfNET.Game.InputEvents.MouseTracker Mouse) JumpPlayer(GameMap level)
{
    var player = new WolfNET.Game.Core.ObjectBase { Transform = new Transform { Position = level.PlayerStartPosition, Rotation = Vector2.UnitX } };
    var keys = new WolfNET.Game.InputEvents.KeyTracker { KeyState = new() { Jump = true } };
    var camera = new WolfNET.Game.Player.Components.CameraComponent { Plane = new(0,-0.66f) };
    var mouse = new WolfNET.Game.InputEvents.MouseTracker();
    return (player, keys, camera, new(keys, mouse, level.NavigationMap, camera, level.PlayerStart, player), mouse);
}
{
    var jump = JumpPlayer(JumpLevel());
    jump.Controller.Update(0);
    Check(jump.Controller.InAir && jump.Camera.Elevation == 0 && jump.Controller.JumpDestination == new Vector2(6.5f,3.5f), "Jump destination was not planned at takeoff");
    jump.Controller.Update(250);
    Check(MathF.Abs(jump.Camera.Elevation - .25f) < .0001f && jump.Player.Transform.Position == new Vector2(4.5f,3.5f), "Jump rise, travel speed, or apex timing incorrect");
    jump.Keys.KeyState = new() { Backward = true, StrafeRight = true, Jump = true };
    jump.Mouse.HorizontalMovement = 100;
    jump.Controller.Update(250);
    Check(!jump.Controller.InAir && jump.Camera.Elevation == 0 && jump.Player.Transform.Position == new Vector2(6.5f,3.5f), "Airborne input steered the jump or changed its distance");
    Check(jump.Player.Transform.Rotation != Vector2.UnitX, "Jumping prevented mouse look");
    jump.Keys.KeyState = new() { Jump = true }; jump.Controller.Update(100);
    Check(!jump.Controller.InAir && jump.Player.Transform.Position == new Vector2(6.5f,3.5f), "Held Space repeated the jump");
    jump.Keys.KeyState = new();
    jump.Keys.KeyState = new() { Jump = true }; jump.Controller.Update(0);
    Check(jump.Controller.InAir, "Space released and pressed between frames did not restart jumping");
}
foreach (float multiplier in new[] { 0.5f, 1f, 2f })
foreach (float rise in new[] { 0f, .1f, .4f })
{
    var jump = JumpPlayer(JumpLevel(JumpMap(multiplier: multiplier, rise: rise)));
    var seconds = 4 / (4 * multiplier);
    jump.Controller.Update(seconds * 500);
    Check(jump.Controller.InAir && MathF.Abs(jump.Camera.Elevation - rise) < .0001f && MathF.Abs(jump.Player.Transform.Position.X - 4.5f) < .0001f,
        "Configured jump rise or walking-speed multiplier was ignored");
    jump.Controller.Update(seconds * 500);
    Check(!jump.Controller.InAir && jump.Player.Transform.Position == new Vector2(6.5f,3.5f), "Derived jump time did not produce the requested distance");
}
foreach (bool jumpable in new[] { false, true })
foreach (float frame in new[] { 16f, 100f, 500f })
{
    var jump = JumpPlayer(JumpLevel(jumpable: jumpable));
    for(float elapsed=0;elapsed<500;elapsed+=frame)jump.Controller.Update(MathF.Min(frame,500-elapsed));
    Check(!jump.Controller.InAir && jump.Camera.Elevation == 0, "Jump timing depended on frame interval");
    Check(MathF.Abs(jump.Player.Transform.Position.X - (jumpable ? 6.5f : 3.8f)) < .0001f, "Jump failed to cross a jumpable obstacle or stop at a solid obstacle");
}
foreach (float length in new[] { 1.35f, 2f })
{
    var jump = JumpPlayer(JumpLevel(JumpMap(length)));
    jump.Controller.Update(0);
    Check(MathF.Abs(jump.Controller.JumpDestination.X - 3.8f) < .0001f, "Blocked landing circle was not identified before takeoff");
    float previous=2.5f;
    for(int i=0;i<40;i++)
    {
        jump.Controller.Update(16);
        var x=jump.Player.Transform.Position.X;
        Check(x>=previous-.00001f && x<=3.80001f, "Jump entered its blocked destination or snapped backward");
        previous=x;
    }
    Check(!jump.Controller.InAir, "Blocked jump never finished");
}
{
    var definition = JumpMap(); definition.WorldMap[5][3]=1;
    var jump = JumpPlayer(JumpLevel(definition)); jump.Controller.Update(0);
    Check(MathF.Abs(jump.Controller.JumpDestination.X - 3.8f)<.0001f, "Solid wall beyond a jumpable cell left a blocked landing inside that cell");
    jump.Controller.Update(500);
    Check(jump.Player.Transform.Position.X<=3.8f, "Jump crossed the obstacle that blocks its landing");
}
{
    var jump=JumpPlayer(JumpLevel(JumpMap(length:0))); jump.Controller.Update(500);
    Check(!jump.Controller.InAir && jump.Player.Transform.Position==new Vector2(2.5f,3.5f), "Zero length did not disable jumping");
    var level=JumpLevel(); level.SetCell(4,3,1);
    Check(!level.NavigationMap.IsWalkable(4,3,inAir:true), "Map revisions retained stale airborne collision flags");
    Check(!level.NavigationMap.IsWalkable(-1,2,inAir:true)&&!level.NavigationMap.IsWalkable(0,2,inAir:true), "Airborne player could pass map boundaries");
}
Reject(() => Compile(objects: new(definitions) { [1] = wall with { Jumpable = true } }), "Jumpable boundary accepted");
foreach(float invalid in new[]{-1f,0.5f,float.NaN,float.PositiveInfinity})
    Reject(()=>Compile(SmallMap() with{PlayerStart=SmallMap().PlayerStart with{JumpRise=invalid}}), "Invalid jump rise accepted");
foreach(float invalid in new[]{-1f,65f,float.NaN,float.PositiveInfinity})
    Reject(()=>Compile(SmallMap() with{PlayerStart=SmallMap().PlayerStart with{JumpLength=invalid}}), "Invalid jump length accepted");
foreach(float invalid in new[]{-1f,0f,float.NaN,float.PositiveInfinity})
    Reject(()=>Compile(SmallMap() with{PlayerStart=SmallMap().PlayerStart with{JumpSpeedMultiplier=invalid}}), "Invalid jump multiplier accepted");

var editorSource = new WolfNET.Editor.MapWorkspace(webRoot);
foreach (var direction in new[] { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY })
{
    var center = new Vector2(9.5f,20.5f);
    var start = center - direction;
    var destination = center + direction;
    var player = new WolfNET.Game.Core.ObjectBase { Transform = new Transform { Position = start, Rotation = direction } };
    var keys = new WolfNET.Game.InputEvents.KeyTracker { KeyState = new() { Jump = true } };
    var camera = new WolfNET.Game.Player.Components.CameraComponent { Plane = new Vector2(direction.Y,-direction.X) * .66f };
    var controller = new WolfNET.Game.Player.Components.PlayerController(keys,new(),showcase.NavigationMap,camera,showcase.PlayerStart,player);
    controller.Update(0);
    Check(Vector2.Distance(controller.JumpDestination,destination)<.0001f,$"Showcase column jump destination is blocked in direction {direction}");
    controller.Update(160);
    Check(controller.InAir && Vector2.Distance(player.Transform.Position,center)<.0001f && MathF.Abs(camera.Elevation-.4f)<.0001f,
        "Showcase jump did not cross the column rows at the configured rise");
    controller.Update(160);
    Check(!controller.InAir && Vector2.Distance(player.Transform.Position,destination)<.0001f,"Showcase column jump did not land beyond both rows");
}
var editorDocument = editorSource.Load("showcase") with { Name = "editor-roundtrip" };
foreach (var direction in new[] { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY })
{
    var center = new Vector2(9.5f,16.5f);
    var player = new WolfNET.Game.Core.ObjectBase { Transform = new Transform { Position = center-direction, Rotation = direction } };
    var keys = new WolfNET.Game.InputEvents.KeyTracker { KeyState = new() { Forward = true } };
    var camera = new WolfNET.Game.Player.Components.CameraComponent { Plane = new Vector2(direction.Y,-direction.X)*.66f };
    var controller = new WolfNET.Game.Player.Components.PlayerController(keys,new(),showcase.NavigationMap,camera,showcase.PlayerStart,player);
    controller.Update(200);
    Check(Vector2.Distance(player.Transform.Position,center)>.64f && !controller.Crouching,"Standing player entered ceiling columns");
    keys.KeyState = new() { Forward = true, Crouch = true }; controller.Update(200);
    Check(controller.Crouching && MathF.Abs(camera.Elevation+.25f)<.0001f && Vector2.Distance(player.Transform.Position,center)<.2f,
        "Crouching did not lower the camera or pass beneath the obstacle at half speed");
    var inside=player.Transform.Position;
    keys.KeyState=new(); controller.Update(200);
    Check(controller.Crouching && player.Transform.Position==inside && camera.Elevation==-.25f,"Releasing Alt under an obstacle stood up or pushed the player out");
    keys.KeyState=new() {Forward=true}; controller.Update(400);
    keys.KeyState=new(); controller.Update(120);
    Check(!controller.Crouching && MathF.Abs(camera.Elevation)<.0001f && Vector2.Dot(player.Transform.Position-center,direction)>.8f,
        "Player could not leave the overhead obstacle and stand up");
}
{
    var crouchObjects=new Dictionary<int,MapObjectDefinition>(definitions) { [10]=Passage(10,collides:true) with {Crouchable=true} };
    var level=Compile(JumpMap(),crouchObjects);
    var jump=JumpPlayer(level);
    jump.Keys.KeyState=new() {Crouch=true,Jump=true}; jump.Controller.Update(120);
    Check(jump.Controller.Crouching&&!jump.Controller.InAir&&jump.Camera.Elevation==-.25f,"Player jumped while crouched");
    jump.Keys.KeyState=new(); jump.Controller.Update(120);
    jump.Keys.KeyState=new() {Jump=true}; jump.Controller.Update(0);
    Check(jump.Controller.JumpDestination.X<3.81f,"Jumping bypassed a crouch-only obstacle");
    jump.Keys.KeyState=new() {Crouch=true}; jump.Controller.Update(100);
    Check(jump.Controller.InAir&&!jump.Controller.Crouching&&jump.Camera.Elevation>0,"Alt in flight enabled crouching");
    Check(level.NavigationMap.IsWalkable(4,3,crouching:true),"Crouch navigation flag missing");
    level.SetCell(4,3,1);
    Check(!level.NavigationMap.IsWalkable(4,3,crouching:true)&&!level.NavigationMap.IsWalkable(-1,3,crouching:true),"Crouching ignored revised walls or map bounds");
}
Reject(()=>Compile(objects:new(definitions){[1]=wall with {Crouchable=true}}),"Crouchable map boundary accepted");
foreach(float invalid in new[]{0f,-1f,.5f,float.NaN,float.PositiveInfinity})
    Reject(()=>Compile(SmallMap() with{PlayerStart=SmallMap().PlayerStart with{CrouchDrop=invalid}}),"Invalid crouch drop accepted");
foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
    Reject(()=>Compile(SmallMap() with{PlayerStart=SmallMap().PlayerStart with{CrouchSpeedMultiplier=invalid}}),"Invalid crouch speed accepted");
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
    Directory.CreateDirectory(Path.Combine(editorRoot, "textures", "skyboxes"));
    File.WriteAllBytes(Path.Combine(editorRoot, "textures", "skyboxes", "skybox.jpg"), [1, 2, 3]);
    Directory.CreateDirectory(Path.Combine(editorRoot, "textures", "wolf"));
    File.WriteAllBytes(Path.Combine(editorRoot, "textures", "wolf", "mesh.png"), [4, 5, 6]);
    await store.SaveAsync(editorDocument, false, CancellationToken.None);
    var persisted = store.Load("editor-roundtrip");
    Check(persisted.Map.WorldMap.SelectMany(column => column).SequenceEqual(editorDocument.Map.WorldMap.SelectMany(column => column)), "Editor save changed grid orientation");
    Check(persisted.Map.PlayerStart == editorDocument.Map.PlayerStart, "Editor save changed spawn settings");
    Check(persisted.Map.Sky == editorDocument.Map.Sky, "Editor save changed sky settings");
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
        [11] = (PassageDefinition)editorDocument.Objects[11] with { FloorTexture = "textures/wolf/mossy.png" }
    };
    await store.SaveAsync(editorDocument with { Objects = modifiedObjects }, true, CancellationToken.None);
    var edited = store.Load("editor-roundtrip");
    Check(((PassageDefinition)edited.Objects[11]).FloorTexture == "textures/wolf/mossy.png", "Editor overwrite did not persist definition edits");
    Check(((PassageDefinition)editorSource.Load("showcase").Objects[11]).FloorTexture == "textures/wolf/colorstone.png", "Editor edited shared engine objects");
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(store.Export(editorDocument)));
    Check(zip.GetEntry("maps/editor-roundtrip.json") != null, "Editor export missing map");
    Check(zip.GetEntry("textures/skyboxes/skybox.jpg")?.Length == 3 && zip.GetEntry("textures/wolf/mesh.png")?.Length == 3,
        "Editor export omitted the nested sky JPG or alpha ceiling texture");
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
