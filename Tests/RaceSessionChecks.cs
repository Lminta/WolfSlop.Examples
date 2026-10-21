using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Graphics;

internal static class RaceSessionChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        void Near(double value, double expected, string message) => Check(Math.Abs(value - expected) < .01, message);
        void Reject(Action action)
        {
            try { action(); } catch (InvalidDataException) { count++; return; }
            throw new Exception("Malformed race gate metadata was accepted.");
        }
        RaceGateDefinition Gate(float x, float y, float dx, float dy) => new()
            { Center = [x, y], Direction = [dx, dy], HalfWidth = 1 };
        var definition = new RaceDefinition { StartFinish = Gate(5, 5, 0, -1),
            Checkpoints = [Gate(5, 3, 0, -1), Gate(7, 3, 1, 0)] };
        definition.Validate(10, 10);
        var session = new RaceSession(definition);
        session.Update(new(5, 6), new(5, 6), 1000);
        Check(!session.Status.Started && session.Status.TotalMilliseconds == 0, "Timer started before the first forward line crossing.");
        session.Update(new(5, 4), new(5, 6), 200);
        session.Update(new(8, 6), new(8, 4), 200);
        Check(!session.Status.Started, "Reverse or out-of-width crossing started a race.");
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.Started && session.Status.CompletedLaps == 0, "The initial finish crossing counted as a completed lap.");
        Near(session.Status.TotalMilliseconds, 100, "Race start time ignored the crossing fraction within a frame.");
        session.Update(new(5, 4), new(5, 6), 200);
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.CompletedLaps == 0, "Oscillating across the finish line counted a lap.");
        session.Update(new(6, 3), new(8, 3), 200); // Checkpoint 2 first: ignored.
        session.Update(new(5, 4), new(5, 2), 200);
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.CompletedLaps == 0, "Out-of-order checkpoints were credited.");
        session.Update(new(6, 3), new(8, 3), 200);
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.CompletedLaps == 0, "An incomplete finish preserved checkpoints from the previous attempt.");
        session.Update(new(5, 4), new(5, 2), 200);
        session.Update(new(6, 3), new(8, 3), 200);
        var beforeFinish = session.Status;
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.CompletedLaps == 1, "A complete ordered lap was not counted.");
        Near(session.Status.LastLapMilliseconds!.Value, beforeFinish.LapMilliseconds + 100, "Completed lap time lost the finish-crossing fraction.");
        Near(session.Status.LapMilliseconds, 100, "Current lap timer did not reset at the finish line.");
        Near(session.Status.TotalMilliseconds, beforeFinish.TotalMilliseconds + 200, "Lap completion reset the total timer.");
        session.Update(new(5, 4), new(5, 6), 200);
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.CompletedLaps == 1, "A completed lap left its checkpoint credits reusable.");

        var inline = new RaceSession(new() { StartFinish = Gate(1, 5, 1, 0),
            Checkpoints = [Gate(2, 5, 1, 0), Gate(3, 5, 1, 0)] });
        inline.Update(new(0, 5), new(4, 5), 1000);
        Near(inline.Status.TotalMilliseconds, 750, "Fast movement missed the initial timing gate.");
        inline.Update(new(0, 5), new(2, 5), 200);
        Check(inline.Status.CompletedLaps == 1, "Multiple gate crossings in one movement were processed out of order.");
        Near(inline.Status.LastLapMilliseconds!.Value, 850, "Fast gate crossings lost elapsed race time.");

        Reject(() => (definition with { Checkpoints = [] }).Validate(10, 10));
        Reject(() => (definition with { StartFinish = Gate(10, 5, 0, 1) }).Validate(10, 10));
        Reject(() => (definition with { StartFinish = Gate(5, 5, 0, 0) }).Validate(10, 10));
        Reject(() => (definition with { StartFinish = definition.StartFinish with { HalfWidth = 0 } }).Validate(10, 10));
        Reject(() => (definition with { Checkpoints = [definition.Checkpoints[0], definition.Checkpoints[0]] }).Validate(10, 10));

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var authored = JsonSerializer.Deserialize<MapDefinition>(File.ReadAllText(Path.Combine(webRoot, "maps/pine-loop.json")), json)!;
        Check(authored.Race != null, "Generated race map has no lap/checkpoint metadata.");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(webRoot, "../../Tests/Fixtures/pine-loop-lap.json")));
        var lap = fixture.RootElement.GetProperty("lap").EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray()[..^1];
        var finishGate = authored.Race!.StartFinish;
        var center = new Vector2(finishGate.Center[0], finishGate.Center[1]);
        var forward = Vector2.Normalize(new(finishGate.Direction[0], finishGate.Direction[1]));
        var initial = Enumerable.Range(0, lap.Length).First(i =>
            Vector2.Dot(lap[i] - center, forward) <= 0 && Vector2.Dot(lap[(i + 1) % lap.Length] - center, forward) > 0 &&
            Vector2.Distance(lap[i], center) < finishGate.HalfWidth);
        var ordered = Enumerable.Range(0, lap.Length + 2).Select(i => lap[(initial + i) % lap.Length]).ToList();
        RaceStatus Ride(IReadOnlyList<Vector2> points)
        {
            var ride = new RaceSession(authored.Race);
            for (var i = 1; i < points.Count; i++) ride.Update(points[i - 1], points[i], 100);
            return ride.Status;
        }
        Check(Ride(ordered).CompletedLaps == 1, "A complete authored circuit fails the checkpoint sequence.");
        var shortcut = fixture.RootElement.GetProperty("shortcut");
        Vector2 Point(string name) => new(shortcut.GetProperty(name)[0].GetSingle(), shortcut.GetProperty(name)[1].GetSingle());
        var enter = ordered.FindIndex(point => point == Point("from"));
        var exit = ordered.FindIndex(enter + 1, point => point == Point("to"));
        Check(enter >= 0 && exit > enter, "Shortcut does not follow the main lap direction.");
        var shorter = ordered.Take(enter + 1).Concat(ordered.Skip(exit)).ToArray();
        Check(Ride(shorter).CompletedLaps == 1, "Invisible checkpoints invalidate the deliberate mud shortcut.");

        var loaded = await new GameSceneLoader(http, new RendererSettings()).LoadAsync("maps/pine-loop.json");
        var keys = new FakeKeyboard { KeyState = new() { Forward = true } };
        using var scene = new GameScene(loaded.Map, loaded.Character, keys, new FakeMouse(), new RendererSettings());
        for (var i = 0; i < 90; i++) scene.Update(1000f / 60);
        Check(scene.RaceState is { Started: true, TotalMilliseconds: > 0, CompletedLaps: 0 }, "Real car movement did not start race timing at the finish line.");
        var saved = scene.RaceState;
        scene.SetPaused(true); scene.Update(1000);
        Check(scene.RaceState == saved, "Pausing the car advanced the race timers.");
        scene.SetPaused(false); scene.Update(1000f / 60);
        Check(scene.RaceState!.Value.TotalMilliseconds > saved!.Value.TotalMilliseconds, "Race timer did not resume with driving.");
        using var fresh = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new RendererSettings());
        Check(fresh.RaceState is { Started: false, CompletedLaps: 0, TotalMilliseconds: 0 }, "A new race scene retained the previous session's times or laps.");
        var withoutRace = new GameMap(authored with { Race = null }, loaded.Map.Objects, 512, 512);
        Check(withoutRace.RendererMap.Sprites.Count == loaded.Map.RendererMap.Sprites.Count &&
            withoutRace.RendererMap.Cells.SequenceEqual(loaded.Map.RendererMap.Cells), "Timing checkpoints changed map objects or collisions.");
        var workspace = new WolfNET.Editor.MapWorkspace(webRoot);
        var exported = workspace.Export(workspace.Load("pine-loop"));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(exported));
        var mapEntry = zip.Entries.First(entry => entry.FullName.EndsWith("pine-loop.json"));
        using var reader = new StreamReader(mapEntry.Open());
        var restored = JsonSerializer.Deserialize<MapDefinition>(reader.ReadToEnd(), json)!;
        Check(restored.Race?.Checkpoints.Length == authored.Race.Checkpoints.Length, "Editor export lost invisible race checkpoints.");
        Console.WriteLine($"PASS: {count} race session assertions (ordered gates, lap timing, shortcut, pause, scene resets, and editor export).");
        return count;
    }
}
