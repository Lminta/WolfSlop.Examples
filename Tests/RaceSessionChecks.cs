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
        void Ready(RaceSession race)
        {
            race.BeginCountdown();
            race.AdvanceStart(5000);
        }
        session.BeginCountdown();
        Check(session.Status is { Phase: RacePhase.Countdown, CountdownNumber: 5 }, "Countdown did not begin at five.");
        session.Update(new(5, 6), new(5, 4), 1000);
        Check(session.Status.TotalMilliseconds == 0, "Driving or timing was allowed before GO.");
        for (var number = 4; number >= 1; number--)
        {
            Near(session.AdvanceStart(1000), 0, "Countdown time leaked into racing.");
            Check(session.Status.CountdownNumber == number, "Countdown skipped a number.");
        }
        Near(session.AdvanceStart(1100), 100, "GO did not preserve the racing fraction of its frame.");
        Check(session.Status is { Phase: RacePhase.Racing, ShowGo: true, CountdownNumber: null }, "Countdown did not switch to GO.");
        session.Update(new(5, 6), new(5, 4), 200);
        Check(session.Status.Started && session.Status.CompletedLaps == 0, "The initial finish crossing counted as a completed lap.");
        Near(session.Status.TotalMilliseconds, 200, "Race time did not begin at GO.");
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
        Check(!session.Status.ShowGo, "GO did not disappear after one second of racing.");
        for (var lapNumber = 2; lapNumber <= 3; lapNumber++)
        {
            session.Update(new(5, 4), new(5, 2), 200);
            session.Update(new(6, 3), new(8, 3), 200);
            var before = session.Status;
            session.Update(new(5, 6), new(5, 4), 200);
            Check(session.Status.CompletedLaps == lapNumber, "A valid subsequent lap was not counted.");
            Near(session.Status.TotalMilliseconds, before.TotalMilliseconds + (lapNumber == 3 ? 100 : 200),
                "Race finish time was not frozen at the third crossing fraction.");
        }
        var finished = session.Status;
        Check(finished.Phase == RacePhase.Finished && finished.LapTimes.Count == 3, "Three laps did not produce final results.");
        Near(finished.LapTimes.Sum(), finished.TotalMilliseconds, "Lap results do not add up to total race time.");
        session.BeginCountdown(); session.AdvanceStart(5000);
        session.Update(new(5, 6), new(5, 2), 10000);
        Check(session.Status == finished, "Input or elapsed time changed a finished race.");

        foreach (var lapCount in new[] { 1, 4 })
        {
            var configured = new RaceSession(definition, new RaceSettings { LapCount = lapCount });
            Check(configured.Status.TotalLaps == lapCount, "Race status ignored the configured total lap count.");
            Ready(configured);
            configured.Update(new(5, 6), new(5, 4), 200);
            for (var lapNumber = 1; lapNumber <= lapCount; lapNumber++)
            {
                configured.Update(new(5, 4), new(5, 2), 200);
                configured.Update(new(6, 3), new(8, 3), 200);
                configured.Update(new(5, 6), new(5, 4), 200);
                Check(configured.Status.Phase == (lapNumber == lapCount ? RacePhase.Finished : RacePhase.Racing),
                    "Race finished at a different lap than configured.");
            }
            Check(configured.Status.LapTimes.Count == lapCount, "Results omitted configured laps.");
        }
        Reject(() => new GameplaySettings { Race = new() { LapCount = 0 } }.Validate());
        Reject(() => new RaceSession(definition, new RaceSettings { LapCount = -1 }));

        var inline = new RaceSession(new() { StartFinish = Gate(1, 5, 1, 0),
            Checkpoints = [Gate(2, 5, 1, 0), Gate(3, 5, 1, 0)] });
        Ready(inline);
        inline.Update(new(0, 5), new(4, 5), 1000);
        Near(inline.Status.TotalMilliseconds, 1000, "Fast movement lost time after GO.");
        inline.Update(new(0, 5), new(2, 5), 200);
        Check(inline.Status.CompletedLaps == 1, "Multiple gate crossings in one movement were processed out of order.");
        Near(inline.Status.LastLapMilliseconds!.Value, 1100, "Fast gate crossings lost elapsed race time.");

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
            Ready(ride);
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
        var startingPose = scene.PlayerPose;
        scene.SetPaused(true); scene.Update(1000);
        Check(scene.RaceState?.Phase == RacePhase.AwaitingStart, "Race countdown began before mouse capture.");
        scene.SetPaused(false);
        scene.Update(250);
        var countdown = scene.RaceState;
        var redLight = scene.CaptureFrame().Map.Sprites.Single(sprite => sprite.RaceStartLight);
        Check(redLight.ViewLayer(Vector2.Zero) == redLight.TextureLayer, "Starting light was not red during countdown.");
        scene.SetPaused(true); scene.Update(1000);
        Check(scene.RaceState == countdown, "Countdown continued while paused.");
        scene.SetPaused(false);
        for (var i = 0; i < 18; i++) scene.Update(250);
        Check(scene.PlayerPose == startingPose && scene.RaceState?.CountdownNumber == 1, "Countdown allowed the car to move.");
        scene.Update(250);
        var greenLight = scene.CaptureFrame().Map.Sprites.Single(sprite => sprite.RaceStartLight);
        Check(scene.RaceState is { Phase: RacePhase.Racing, ShowGo: true } && greenLight.TextureLayer == redLight.TextureLayer + 1,
            "GO did not release controls and switch the world light to green.");
        for (var i = 0; i < 90; i++) scene.Update(1000f / 60);
        Check(scene.RaceState is { Started: true, TotalMilliseconds: > 0, CompletedLaps: 0 }, "Real car movement did not start race timing at the finish line.");
        var saved = scene.RaceState;
        scene.SetPaused(true); scene.Update(1000);
        Check(scene.RaceState == saved, "Pausing the car advanced the race timers.");
        scene.SetPaused(false); scene.Update(1000f / 60);
        Check(scene.RaceState!.Value.TotalMilliseconds > saved!.Value.TotalMilliseconds, "Race timer did not resume with driving.");
        using var fresh = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new RendererSettings(),
            new GameplaySettings { Race = new() { LapCount = 7 } });
        Check(fresh.RaceState?.TotalLaps == 7, "Scene did not pass gameplay lap settings to the session.");
        Check(fresh.RaceState is { Phase: RacePhase.AwaitingStart, Started: false, CompletedLaps: 0, TotalMilliseconds: 0 }, "A new race scene retained the previous session's times or laps.");
        Check(fresh.CaptureFrame().Map.Sprites.Single(sprite => sprite.RaceStartLight).TextureLayer == redLight.TextureLayer,
            "Restarting did not reset the starting light to red.");
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
