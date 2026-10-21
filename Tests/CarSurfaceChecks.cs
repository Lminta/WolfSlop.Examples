using System.IO.Compression;
using System.Text.Json;
using WolfNET.Game;

internal static class CarSurfaceChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var assertions = 0;
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

        var definitions = new Dictionary<int, MapObjectDefinition>
        {
            [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false, FloorTexture = "textures/raceway/asphalt.png" },
            [1] = new WallDefinition { Id = 1, RaycastTransparent = false, Collides = true, PassageId = 0 },
            [2] = new PassageDefinition { Id = 2, RaycastTransparent = true, Collides = false, Surface = "asphalt" },
            [3] = new PassageDefinition { Id = 3, RaycastTransparent = true, Collides = false, Surface = "grass", FloorTexture = "textures/raceway/asphalt.png" },
            [4] = new SpriteDefinition { Id = 4, RaycastTransparent = true, Collides = false, PassageId = 2, TextureLayer = 8 },
            [5] = new SpriteGroupDefinition { Id = 5, RaycastTransparent = true, Collides = false, PassageId = 3,
                Sprites = [new() { SpriteId = 4, Offset = new() { X = 0, Y = 0 } }] },
            [6] = new WallDefinition { Id = 6, RaycastTransparent = false, Collides = true, PassageId = 2 },
            [7] = new DoorDefinition { Id = 7, RaycastTransparent = false, Collides = true, PassageId = 2 }
        };
        var cells = Enumerable.Range(0, 8).Select(x => Enumerable.Range(0, 8)
            .Select(y => x == 0 || y == 0 || x == 7 || y == 7 ? 1 : 0).ToArray()).ToArray();
        cells[2][2] = 2; cells[2][3] = 3; cells[3][2] = 4; cells[3][3] = 5;
        cells[4][2] = 6; cells[4][3] = 7;
        var definition = new MapDefinition { WorldMap = cells, PlayerStart = new() {
            X = 1.5f, Y = 1.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 3, RotSpeed = 3 } };
        var map = new GameMap(definition, definitions, 8, 8);
        Check(map.PlayerStart.Controller == "player", "Old maps no longer default to the walking character");
        Check(map.NavigationMap.GetSurface(1, 1) == "mud", "Missing semantic surface must default to mud, independent of asphalt artwork");
        Check(map.NavigationMap.GetSurface(2, 2) == "asphalt", "Asphalt surface needs no texture to work");
        Check(map.NavigationMap.GetSurface(2, 3) == "grass", "Semantic grass was inferred from asphalt artwork");
        Check(map.NavigationMap.GetSurface(3, 2) == "asphalt" && map.NavigationMap.GetSurface(3, 3) == "grass",
            "Sprites and groups do not inherit their own passage surface");
        Check(map.NavigationMap.GetSurface(4, 2) == "asphalt" && map.NavigationMap.GetSurface(4, 3) == "asphalt",
            "Walls or doors lost passage surface metadata");
        foreach (var point in new[] { (-1, 0), (0, -1), (8, 0), (0, 8) })
            Check(map.NavigationMap.GetSurface(point.Item1, point.Item2) == "mud", "Outside-map surface must default to mud");
        var navigation = map.NavigationMap;
        var revision = map.Revision;
        map.SetCell(2, 2, 3);
        Check(ReferenceEquals(navigation, map.NavigationMap) && navigation.GetSurface(2, 2) == "grass" && map.Revision > revision,
            "Map edits did not replace surface data on the stable navigation map");
        revision = map.Revision;
        Reject(() => map.SetCell(2, 2, 999), "Unknown-object map revision unexpectedly succeeded");
        Check(map.Revision == revision && navigation.GetSurface(2, 2) == "grass" && map.WorldMap[2][2] == 3,
            "Failed map compilation published partial surface data");
        foreach (var controller in new[] { "", "truck", "Car", null! })
            Reject(() => new GameMap(definition with { PlayerStart = definition.PlayerStart with { Controller = controller } }, definitions, 8, 8),
                "Invalid controller accepted");
        Check(new GameMap(definition with { PlayerStart = definition.PlayerStart with { Controller = "car" } }, definitions, 8, 8).PlayerStart.Controller == "car",
            "Car controller metadata was rejected");
        foreach (var surface in new[] { "", "sand", "Asphalt", null! })
        {
            var invalid = new Dictionary<int, MapObjectDefinition>(definitions) { [2] = (PassageDefinition)definitions[2] with { Surface = surface } };
            Reject(() => new MapCompiler(invalid, 8, 8), "Invalid driving surface accepted");
        }

        var race = await GameMap.LoadAsync(http, "maps/pine-loop.json", 512, 512);
        Check(race.PlayerStart.Controller == "car", "Pine Loop did not select the car controller");
        var asphalt = new HashSet<int> { 500, 503, 504, 505, 506, 508, 615 };
        Check(race.Objects.Values.OfType<PassageDefinition>().All(p => p.Surface == (asphalt.Contains(p.Id) ? "asphalt" : p.Id == 501 ? "grass" : "mud")),
            "Pine Loop passage profiles do not cover asphalt, grid, tunnel, pit, yard, mud and grass correctly");
        Check(race.NavigationMap.GetSurface(25, 442) == "asphalt" && race.NavigationMap.GetSurface(25, 444) == "asphalt" &&
              race.NavigationMap.GetSurface(90, 44) == "asphalt" && race.NavigationMap.GetSurface(35, 412) == "asphalt" &&
              race.NavigationMap.GetSurface(215, 384) == "asphalt" && race.NavigationMap.GetSurface(120, 321) == "mud",
            "Start, grid, tunnel, pit, service aisle or shortcut use the wrong driving surface");
        foreach (var name in new[] { "glassworks", "level1", "showcase" })
            Check((await GameMap.LoadAsync(http, $"maps/{name}.json", 512, 512)).PlayerStart.Controller == "player",
                "Exploration map unexpectedly uses the car controller");

        var workspace = new WolfNET.Editor.MapWorkspace(webRoot);
        using var archive = new ZipArchive(new MemoryStream(workspace.Export(workspace.Load("pine-loop"))));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var mapStream = archive.GetEntry("maps/pine-loop.json")!.Open();
        var exported = JsonSerializer.Deserialize<MapDefinition>(mapStream, json)!;
        Check(exported.PlayerStart.Controller == "car", "Editor export lost the car controller selection");
        foreach (var passage in race.Objects.Values.OfType<PassageDefinition>())
        {
            var path = exported.Objects[passage.Id].Replace("../", "");
            using var objectStream = archive.GetEntry(path)!.Open();
            var exportedPassage = (PassageDefinition)JsonSerializer.Deserialize<MapObjectDefinition>(objectStream, json)!;
            Check(exportedPassage.Surface == passage.Surface, $"Editor export lost surface metadata for passage {passage.Id}");
        }
        Console.WriteLine($"PASS: {assertions} car surface assertions (semantic profiles, inheritance, atomic revisions, controller selection, and editor export).");
        return assertions;
    }
}
