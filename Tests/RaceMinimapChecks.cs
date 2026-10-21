using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Graphics;

internal static class RaceMinimapChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        void Near(Vector2 actual, Vector2 expected, string message) => Check(Vector2.Distance(actual, expected) < .0002f, message);
        var player = new Vector2(25.5f, 445.5f);
        foreach (var direction in new[] { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY,
                     new Vector2(1, 1), new Vector2(-1, 1), new Vector2(2, -3) })
        {
            var matrix = RaceMinimapProjection.Create(player, direction);
            var forward = Vector2.Normalize(direction);
            var right = new Vector2(forward.Y, -forward.X);
            Near(Vector2.Transform(player, matrix), new(50, 70), "Race minimap player is not anchored below center");
            Near(Vector2.Transform(player + forward * 84, matrix), new(50, 0), "Race minimap does not show 84 blocks ahead");
            Near(Vector2.Transform(player - forward * 36, matrix), new(50, 100), "Race minimap does not show 36 blocks behind");
            Near(Vector2.Transform(player + right * 60, matrix), new(100, 70), "Race minimap right edge is mirrored or zoom changed");
            Near(Vector2.Transform(player - right * 60, matrix), new(0, 70), "Race minimap left edge is mirrored or zoom changed");
            var moved = player + forward * 5;
            Near(Vector2.Transform(player, RaceMinimapProjection.Create(moved, direction)), new(50, 74.16667f),
                "Race minimap does not scroll beneath the anchored player");
        }
        foreach (var invalid in new[] { Vector2.Zero, new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 1) })
        {
            try { RaceMinimapProjection.Create(player, invalid); }
            catch (ArgumentException) { assertions++; continue; }
            throw new Exception("Invalid minimap direction accepted");
        }

        var map = await GameMap.LoadAsync(http, "maps/pine-loop.json", 512, 512);
        var definition = map.RendererMap.RaceMinimap!;
        Check(definition != null, "Pine Loop has no dedicated race minimap");
        definition!.Validate(map.RendererMap.Width, map.RendererMap.Height);
        Check(definition.Route[0].SequenceEqual(definition.Route[^1]) && definition.Route.Length > 100,
            "Race minimap lost the complete authored circuit");
        Check(definition.StartFinish.SequenceEqual(new[] { 25.5f, 442.5f }) && definition.Shortcuts.Length == 1 &&
              definition.Shortcuts[0][0].SequenceEqual(new[] { 101f, 321f }) &&
              definition.Shortcuts[0][^1].SequenceEqual(new[] { 141f, 321f }),
            "Race minimap start/finish or shortcut is misplaced");
        var workspace = new WolfNET.Editor.MapWorkspace(webRoot);
        var document = workspace.Load("pine-loop");
        using var bundle = new System.IO.Compression.ZipArchive(new MemoryStream(workspace.Export(document)));
        using var stream = bundle.GetEntry("maps/pine-loop.json")!.Open();
        var exported = JsonSerializer.Deserialize<MapDefinition>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Check(JsonSerializer.Serialize(exported.RaceMinimap) == JsonSerializer.Serialize(definition),
            "Editor export loses race minimap metadata");
        foreach (var name in new[] { "glassworks", "level1", "showcase" })
            Check((await GameMap.LoadAsync(http, $"maps/{name}.json", 512, 512)).RendererMap.RaceMinimap == null,
                "An exploration map unexpectedly switched to the race minimap");

        foreach (var invalid in new[] {
            definition with { Route = null! },
            definition with { Route = [[1, 1], [2, 2]] },
            definition with { Route = [[1, 1], [2, 1], [2, 2], [1, 2]] },
            definition with { Route = [[1, 1], null!, [2, 2], [1, 1]] },
            definition with { Route = [[1, 1], [2], [2, 2], [1, 1]] },
            definition with { Route = [[1, 1], [float.NaN, 1], [2, 2], [1, 1]] },
            definition with { StartFinish = [-1, 0] },
            definition with { StartFinish = [256, 0] },
            definition with { StartFinish = [1, 512] },
            definition with { Shortcuts = null! },
            definition with { Shortcuts = [null!] },
            definition with { Shortcuts = [[[1, 1]]] },
            definition with { Shortcuts = [[[1, 1], [float.PositiveInfinity, 1]]] }
        })
        {
            try { invalid.Validate(256, 512); }
            catch (InvalidDataException) { assertions++; continue; }
            throw new Exception("Invalid race minimap metadata accepted");
        }
        var raw = JsonSerializer.Deserialize<MapDefinition>(File.ReadAllText(Path.Combine(webRoot, "maps/pine-loop.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var rejected = false;
        try { _ = new GameMap(raw with { RaceMinimap = definition with { Route = [] } }, map.Objects, 512, 512); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Compiler skipped race minimap validation");
        Console.WriteLine($"PASS: {assertions} race minimap assertions (projection, headings, local range, metadata, validation, and editor export).");
        return assertions;
    }
}
