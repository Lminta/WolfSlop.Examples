using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Game.Physics;
using WolfNET.Game.Player;

internal static class OpponentDifficultyChecks
{
    public static async Task<int> RunAsync(HttpClient http)
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new Exception(message);
        }
        void Reject(GameplaySettings invalid)
        {
            try { invalid.Validate(); }
            catch (InvalidDataException) { count++; return; }
            throw new Exception("Invalid opponent settings were accepted.");
        }

        var gameplay = (await http.GetFromJsonAsync<GameplaySettings>("gameplay-settings.json"))!;
        gameplay.Validate();
        Check(gameplay.CameraShake == new CameraShakeSettings(), "JSON camera shake settings differ from the code defaults.");
        Check(gameplay.Opponents == new OpponentSettings(), "JSON opponent profiles differ from the code defaults.");
        Check(gameplay.Opponents.DefaultDifficulty == RaceDifficulty.Normal, "Normal is not the default difficulty.");
        var roundTrip = JsonSerializer.Deserialize<GameplaySettings>(JsonSerializer.Serialize(gameplay))!;
        Check(roundTrip.CameraShake == gameplay.CameraShake, "Camera shake settings did not survive JSON serialization.");
        Check(roundTrip.Opponents == gameplay.Opponents, "Opponent structs did not survive JSON serialization.");
        Reject(gameplay with { Opponents = gameplay.Opponents with { DefaultDifficulty = (RaceDifficulty)999 } });
        Reject(gameplay with { Opponents = gameplay.Opponents with { Easy = null! } });
        Reject(gameplay with { Opponents = gameplay.Opponents with { Wife = null! } });
        foreach (var bad in new OpponentProfileSettings[]
        {
            gameplay.Opponents.Easy.Green with { MaxSpeedOffset = -100 },
            gameplay.Opponents.Easy.Blue with { AccelerationMultiplier = float.NaN },
            gameplay.Opponents.Easy.Blue with { CornerGripFraction = 1.1f },
            gameplay.Opponents.Easy.Blue with { SteeringCurvatureGain = 0 },
            gameplay.Opponents.Easy.Blue with { Mistakes = new() { IntervalSeconds = 0 } },
            gameplay.Opponents.Easy.Blue with { Mistakes = new() { DurationSeconds = -1 } },
            gameplay.Opponents.Easy.Blue with { Mistakes = new() { SpeedMultiplier = 1 } }
        }) Reject(gameplay with { Opponents = gameplay.Opponents with { Easy = gameplay.Opponents.Easy with { Blue = bad } } });

        var loaded = await new GameSceneLoader(http, new(), gameplay).LoadAsync("maps/pine-loop.json");
        using (var configured = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(),
                   gameplay with { CameraShake = gameplay.CameraShake with { ImpactAmplitude = .1f } }))
        {
            var player = (CarObject)configured.Player;
            player.Look.TriggerImpact(20);
            Check(MathF.Abs(configured.CaptureFrame().Pitch - .1f) < .0001f,
                "Gameplay camera shake settings did not reach the player camera and rendered frame.");
        }
        var route = new RaceRoute(loaded.Map.RendererMap.RaceMinimap!);
        var easyDefault = gameplay with { Opponents = gameplay.Opponents with { DefaultDifficulty = RaceDifficulty.Easy } };
        using (var easy = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(), easyDefault))
        using (var normal = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(), easyDefault, RaceDifficulty.Normal))
        using (var wife = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(),
                   gameplay with { Opponents = gameplay.Opponents with { DefaultDifficulty = RaceDifficulty.Wife } }))
        {
            Check(easy.Difficulty == RaceDifficulty.Easy && normal.Difficulty == RaceDifficulty.Normal,
                "Explicit difficulty did not override the JSON default.");
            Check(wife.Difficulty == RaceDifficulty.Wife &&
                  wife.Opponent!.Settings.Asphalt.MaxForwardSpeed == gameplay.Car.Asphalt.MaxForwardSpeed + gameplay.Opponents.Wife.Blue.MaxSpeedOffset &&
                  wife.SecondOpponent!.Settings.Asphalt.MaxForwardSpeed == gameplay.Car.Asphalt.MaxForwardSpeed + gameplay.Opponents.Wife.Green.MaxSpeedOffset &&
                  ((CarObject)wife.Player).Settings == gameplay.Car,
                "Wife mode did not select its configured opponents or changed the player's settings.");
            Check(easy.SecondOpponent!.Settings.Asphalt.MaxForwardSpeed == gameplay.Car.Asphalt.MaxForwardSpeed - 5 &&
                  normal.SecondOpponent!.Settings.Asphalt.MaxForwardSpeed == gameplay.Car.Asphalt.MaxForwardSpeed - 3,
                "Green's difficulty speed offsets were not applied by the scene.");
            Check(easy.Opponent!.Settings.Asphalt.MaxForwardSpeed == normal.Opponent!.Settings.Asphalt.MaxForwardSpeed &&
                  ((CarObject)easy.Player).Settings == gameplay.Car,
                "Easy difficulty changed blue's top speed or the player's car.");
        }
        var tuned = gameplay with { Opponents = gameplay.Opponents with
        {
            Normal = gameplay.Opponents.Normal with
            {
                Blue = gameplay.Opponents.Normal.Blue with { MaxSpeedOffset = 1, AccelerationMultiplier = .5f }
            }
        } };
        using (var custom = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(), tuned))
            Check(custom.Opponent!.Settings.Asphalt.MaxForwardSpeed == gameplay.Car.Asphalt.MaxForwardSpeed + 1 &&
                  custom.Opponent.Settings.Asphalt.Acceleration == gameplay.Car.Asphalt.Acceleration * .5f,
                "The scene ignored a configured opponent profile.");

        bool OffRoad(CarObject car)
        {
            var body = car.Transform;
            var box = new OrientedBoxCollider(loaded.Map.NavigationMap, car.Settings.Length, car.Settings.Width);
            for (var x = (int)MathF.Floor(body.Position.X - car.Settings.Length); x <= (int)MathF.Ceiling(body.Position.X + car.Settings.Length); x++)
            for (var y = (int)MathF.Floor(body.Position.Y - car.Settings.Length); y <= (int)MathF.Ceiling(body.Position.Y + car.Settings.Length); y++)
                if (box.OverlapsCell(body.Position, body.Rotation, x, y) && loaded.Map.NavigationMap.GetSurface(x, y) != "asphalt") return true;
            return false;
        }
        int Solo(RaceDifficulty difficulty, bool green)
        {
            var profile = gameplay.Opponents.Get(difficulty).Get(green);
            var settings = OpponentDriver.CreateCarSettings(gameplay.Car, profile);
            var start = loaded.Map.PlayerStart;
            var position = new Vector2(start.X, start.Y);
            var driver = new OpponentDriver(route, loaded.Map.NavigationMap, settings, gameplay.Race, position,
                gridSide: 0, profile: profile);
            using var car = new CarObject(driver, driver, loaded.Map.NavigationMap, settings)
                { Transform = new() { Position = position, Rotation = Vector2.Normalize(new(start.DirectionX, start.DirectionY)) } };
            var race = new RaceSession(loaded.Map.RendererMap.Race!, gameplay.Race with
                { LapCount = difficulty == RaceDifficulty.Wife ? 3 : 1 });
            race.BeginCountdown();
            race.AdvanceStart(5000);
            double integral = 0;
            var samples = 0;
            var mud = 0;
            for (var frame = 0; frame < 60 * 900 && race.Status.Phase != RacePhase.Finished; frame++)
            {
                var before = car.Transform.Position;
                var time = race.Status.TotalMilliseconds;
                driver.Update(car, Array.Empty<CarObject>(), 1f / 60);
                car.Update(1000f / 60);
                race.Update(before, car.Transform.Position, 1000f / 60);
                integral += car.Controller.Speed * (race.Status.TotalMilliseconds - time);
                if (frame % 6 == 0) { samples++; if (OffRoad(car)) mud++; }
            }
            Check(race.Status.Phase == RacePhase.Finished, "A difficulty profile failed to complete its lap.");
            Check(mud < samples * .01f, "A difficulty profile repeatedly left the asphalt.");
            var mean = integral / race.Status.TotalMilliseconds;
            if (difficulty == RaceDifficulty.Easy)
                Check(mean >= 15.8 && mean <= 17.2, $"Easy {(green ? "green" : "blue")} missed its 16-17 blocks/s loop target: {mean:F2}.");
            if (difficulty == RaceDifficulty.Wife)
            {
                Check(race.Status.LapTimes.Count == 3 && race.Status.LapTimes.All(time => time >= 170000 && time <= 180000),
                    $"Wife mode {(green ? "green" : "blue")} missed its 2:50-3:00 solo lap target.");
                Console.WriteLine($"Wife {(green ? "green" : "blue")} solo laps: {string.Join(", ", race.Status.LapTimes.Select(time => $"{time / 1000:F2}s"))}.");
            }
            Console.WriteLine($"{difficulty} {(green ? "green" : "blue")} lap: mean {mean:F2} blocks/s, {driver.MistakesMade} mistakes.");
            return driver.MistakesMade;
        }
        var normalMistakes = Solo(RaceDifficulty.Normal, false);
        var easyMistakes = Solo(RaceDifficulty.Easy, false);
        Check(easyMistakes > normalMistakes * 2, "Easy blue did not make substantially more mistakes than normal blue.");
        Check(Solo(RaceDifficulty.Easy, true) == 0, "Easy green made deliberate mistakes.");
        Solo(RaceDifficulty.Wife, false);
        Check(Solo(RaceDifficulty.Wife, true) == 0, "Wife mode green made deliberate mistakes.");

        foreach (var difficulty in new[] { RaceDifficulty.Easy, RaceDifficulty.Wife })
        using (var traffic = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new(), gameplay, difficulty))
        {
            var cars = new[] { traffic.Opponent!, traffic.SecondOpponent! };
            var integral = new double[2];
            var mud = new int[2];
            var samples = new int[2];
            for (var frame = 0; frame < 60 * 900 && traffic.RaceState!.Value.Opponents.Any(r => !r.Finished); frame++)
            {
                var before = traffic.RaceState!.Value.Opponents;
                var speeds = (cars[0].Controller.Speed, cars[1].Controller.Speed);
                traffic.Update(1000f / 60);
                var after = traffic.RaceState!.Value.Opponents;
                for (var i = 0; i < 2; i++)
                {
                    var elapsed = after[i].TotalMilliseconds - before[i].TotalMilliseconds;
                    integral[i] += (after[i].Finished ? i == 0 ? speeds.Item1 : speeds.Item2 : cars[i].Controller.Speed) * elapsed;
                    if (elapsed <= 0 || frame % 6 != 0) continue;
                    samples[i]++;
                    if (OffRoad(cars[i])) mud[i]++;
                }
            }
            for (var i = 0; i < 2; i++)
            {
                var result = traffic.RaceState!.Value.Opponents[i];
                Check(result.Finished && result.LapTimes.Count == 3, $"A {difficulty} opponent did not finish three validated laps in traffic.");
                var mean = integral[i] / result.TotalMilliseconds;
                if (difficulty == RaceDifficulty.Easy)
                    Check(mean >= 15.8 && mean <= 17.2, $"Easy traffic average missed its 16-17 blocks/s target: {mean:F2}.");
                else
                {
                    Check(result.LapTimes.All(time => time >= 170000 && time <= 180000),
                        $"Wife mode opponent {i} missed its 2:50-3:00 traffic lap target.");
                    Console.WriteLine($"Wife traffic {i} laps: {string.Join(", ", result.LapTimes.Select(time => $"{time / 1000:F2}s"))}.");
                }
                Check(mud[i] < samples[i] * .01f, $"A {difficulty} opponent repeatedly left the asphalt in traffic.");
                Console.WriteLine($"{difficulty} traffic {i}: mean {mean:F2} blocks/s.");
            }
            Check(traffic.GetOpponentMistakes(1) == 0, $"{difficulty} green made deliberate mistakes in traffic.");
        }
        Console.WriteLine($"PASS: {count} opponent difficulty assertions.");
        return count;
    }
}
