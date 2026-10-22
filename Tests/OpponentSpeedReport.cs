using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Game.Player;
using WolfNET.Game.Physics;

internal static class OpponentSpeedReport
{
    private const float Step = 1000f / 120;
    private sealed record Sample(double Speed, double PlannedSpeed, double Milliseconds, bool OffRoad, OpponentPace Pace);

    private sealed class Recorder(string scenario, string driver, CarObject car, RaceRoute route,
        NavigationMap map, RaceSettings settings, bool skilled)
    {
        private readonly List<Sample> _samples = [];
        private readonly OpponentRacingLine _line = new(route, map, car.Settings);
        private float? _progress;
        private readonly OrientedBoxCollider _footprint = new(map, car.Settings.Length, car.Settings.Width);

        public void Add(double milliseconds, float speed, OpponentPace pace)
        {
            if (milliseconds <= 0) return;
            _progress = route.Project(car.Transform.Position, _progress);
            var planned = MathF.Min(car.Settings.Asphalt.MaxForwardSpeed,
                _line.Speed(_progress.Value, skilled, settings.OpponentCornerSpeed));
            var position = car.Transform.Position;
            var heading = car.Transform.Rotation;
            var extent = new Vector2(car.Settings.Length * MathF.Abs(heading.X) + car.Settings.Width * MathF.Abs(heading.Y),
                car.Settings.Length * MathF.Abs(heading.Y) + car.Settings.Width * MathF.Abs(heading.X)) * .5f;
            var offRoad = false;
            for (var x = (int)MathF.Floor(position.X - extent.X); x <= (int)MathF.Floor(position.X + extent.X); x++)
            for (var y = (int)MathF.Floor(position.Y - extent.Y); y <= (int)MathF.Floor(position.Y + extent.Y); y++)
                if (map.GetSurface(x, y) != "asphalt" && _footprint.OverlapsCell(position, heading, x, y)) offRoad = true;
            _samples.Add(new(speed, planned, milliseconds, offRoad, pace));
        }

        public object Summarize(double totalMilliseconds, IReadOnlyList<double> lapTimes)
        {
            var duration = _samples.Sum(s => s.Milliseconds);
            double Percent(Func<Sample, bool> condition) => _samples.Where(condition).Sum(s => s.Milliseconds) / duration * 100;
            double Quantile(Func<Sample, double> selector, double fraction)
            {
                double elapsed = 0;
                foreach (var sample in _samples.OrderBy(selector))
                {
                    elapsed += sample.Milliseconds;
                    if (elapsed >= duration * fraction) return selector(sample);
                }
                throw new InvalidOperationException("Race has no speed samples.");
            }
            var median = Quantile(s => s.Speed, .5);
            var maximum = _samples.Max(s => s.Speed);
            var p95 = Quantile(s => s.Speed, .95);
            var cap = car.Settings.Asphalt.MaxForwardSpeed;
            Console.WriteLine(FormattableString.Invariant($"Limits: corner {Percent(s => s.Pace.Corner <= s.Pace.Recovery && s.Pace.Corner <= s.Pace.Obstacle && s.Pace.Corner <= s.Pace.Traffic):F1}%, obstacle {Percent(s => s.Pace.Obstacle < s.Pace.Corner && s.Pace.Obstacle <= s.Pace.Recovery && s.Pace.Obstacle <= s.Pace.Traffic):F1}%, recovery {Percent(s => s.Pace.Recovery < s.Pace.Corner && s.Pace.Recovery < s.Pace.Obstacle && s.Pace.Recovery <= s.Pace.Traffic):F1}%, traffic {Percent(s => s.Pace.Traffic < s.Pace.Corner && s.Pace.Traffic < s.Pace.Obstacle && s.Pace.Traffic < s.Pace.Recovery):F1}%, target median {Quantile(s => s.Pace.Target,.5):F2}"));
            Console.WriteLine(FormattableString.Invariant(
                $"{scenario,-12} {driver,-5}: median {median:F3}, max {maximum:F3}, p95 {p95:F3}, cap {cap:F1} blocks/s; race {totalMilliseconds / 1000:F2}s; below 10 {Percent(s => s.Speed < 10):F1}%; mud {Percent(s => s.OffRoad):F1}%; corner planner below 90% cap {Percent(s => s.PlannedSpeed < cap * .9):F1}%"));
            return new
            {
                scenario, driver, samples = _samples.Count, measuredSeconds = duration / 1000,
                configuredMaximum = cap, median, maximum, p95,
                mean = _samples.Sum(s => s.Speed * s.Milliseconds) / duration,
                offRoadPercent = Percent(s => s.OffRoad),
                below1Percent = Percent(s => s.Speed < 1), below7Percent = Percent(s => s.Speed < 7),
                below10Percent = Percent(s => s.Speed < 10),
                atLeast90PercentOfCap = Percent(s => s.Speed >= cap * .9),
                targetMedian = Quantile(s => s.Pace.Target, .5),
                obstacleLimitedPercent = Percent(s => s.Pace.Obstacle < s.Pace.Corner && s.Pace.Obstacle <= s.Pace.Recovery && s.Pace.Obstacle <= s.Pace.Traffic),
                recoveryLimitedPercent = Percent(s => s.Pace.Recovery < s.Pace.Corner && s.Pace.Recovery < s.Pace.Obstacle && s.Pace.Recovery <= s.Pace.Traffic),
                cornerPlannerMedian = Quantile(s => s.PlannedSpeed, .5),
                cornerPlannerBelow90PercentOfCap = Percent(s => s.PlannedSpeed < cap * .9),
                totalSeconds = totalMilliseconds / 1000,
                lapSeconds = lapTimes.Select(t => t / 1000).ToArray()
            };
        }
    }

    public static async Task RunAsync(HttpClient http, string root)
    {
        var gameplay = await http.GetFromJsonAsync<GameplaySettings>("gameplay-settings.json")
            ?? throw new InvalidDataException("Missing gameplay settings.");
        gameplay.Validate();
        var loaded = await new GameSceneLoader(http, new(), gameplay).LoadAsync("maps/pine-loop.json");
        var route = new RaceRoute(loaded.Map.RendererMap.RaceMinimap!);
        var results = new List<object>();
        using (var scene = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(), gameplay))
        {
            var cars = new[] { scene.Opponent!, scene.SecondOpponent! };
            var recorders = cars.Select((car, i) => new Recorder("Traffic", i == 0 ? "Blue" : "Green",
                car, route, loaded.Map.NavigationMap, gameplay.Race, i == 1)).ToArray();
            for (var frame = 0; frame < 120 * 1800 && scene.RaceState!.Value.Opponents.Any(s => !s.Finished); frame++)
            {
                var before = scene.RaceState!.Value.Opponents;
                var speeds = cars.Select(c => c.Controller.Speed).ToArray();
                scene.Update(Step);
                var after = scene.RaceState!.Value.Opponents;
                for (var i = 0; i < cars.Length; i++)
                    recorders[i].Add(after[i].TotalMilliseconds - before[i].TotalMilliseconds,
                        after[i].Finished ? speeds[i] : cars[i].Controller.Speed, scene.GetOpponentPace(i));
            }
            var final = scene.RaceState!.Value.Opponents;
            for (var i = 0; i < cars.Length; i++)
            {
                if (!final[i].Finished) throw new Exception("Traffic race did not finish.");
                results.Add(recorders[i].Summarize(final[i].TotalMilliseconds, final[i].LapTimes));
            }
        }
        for (var i = 0; i < 2; i++)
        {
            var skilled = i == 1;
            var settings = OpponentDriver.CreateCarSettings(gameplay.Car, skilled);
            var start = loaded.Map.PlayerStart;
            var position = new Vector2(start.X, start.Y);
            var driver = new OpponentDriver(route, loaded.Map.NavigationMap, settings, gameplay.Race, position, skilled, gridSide: 0);
            using var car = new CarObject(driver, driver, loaded.Map.NavigationMap, settings)
            {
                Transform = new() { Position = position, Rotation = Vector2.Normalize(new(start.DirectionX, start.DirectionY)) }
            };
            var race = new RaceSession(loaded.Map.RendererMap.Race!, gameplay.Race);
            race.BeginCountdown();
            race.AdvanceStart(5000);
            var recorder = new Recorder("Solo", skilled ? "Green" : "Blue", car, route, loaded.Map.NavigationMap, gameplay.Race, skilled);
            for (var frame = 0; frame < 120 * 1800 && race.Status.Phase != RacePhase.Finished; frame++)
            {
                var before = car.Transform.Position;
                var beforeTime = race.Status.TotalMilliseconds;
                driver.Update(car, Array.Empty<CarObject>(), Step / 1000);
                car.Update(Step);
                race.Update(before, car.Transform.Position, Step);
                recorder.Add(race.Status.TotalMilliseconds - beforeTime, car.Controller.Speed, driver.Pace);
            }
            if (race.Status.Phase != RacePhase.Finished) throw new Exception("Solo race did not finish.");
            results.Add(recorder.Summarize(race.Status.TotalMilliseconds, race.Status.LapTimes));
        }
        var output = Path.Combine(root, "artifacts", "opponent-speeds.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            units = "blocks/s", map = "pine-loop", laps = gameplay.Race.LapCount, simulationHz = 120,
            method = "Time-weighted samples from GO to each car's finish; countdown and post-finish time excluded. Traffic uses the normal three-car scene with a parked player. Solo removes all other cars.",
            gameplay, results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Saved {output}");
    }
}
