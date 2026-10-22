using WolfNET.Game;
using WolfNET.Game.Physics;
using WolfNET.Game.Player;
using System.Numerics;

internal static class OpponentChecks
{
    public static async Task<int> RunAsync(HttpClient http)
    {
        var loaded = await new GameSceneLoader(http, new WolfNET.Graphics.RendererSettings()).LoadAsync("maps/pine-loop.json");
        using var scene = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new());
        var opponent = scene.Opponent ?? throw new Exception("Race has no opponent.");
        var green = scene.SecondOpponent ?? throw new Exception("Race has no green opponent.");
        var greenStart = green.Transform;
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        // Both driver profiles defend either passing lane against any faster racer.
        var route = new RaceRoute(loaded.Map.RendererMap.RaceMinimap!);
        foreach (var skilled in new[] { false, true })
        {
            float Defend(float lateral, float behind, float closing, bool facingForward = true, bool move = false)
            {
                var driver = new OpponentDriver(route, loaded.Map.NavigationMap, opponent.Settings, new(),
                    new(25, 400), skilled, gridSide: 0);
                using var defender = new CarObject(driver, driver, loaded.Map.NavigationMap, opponent.Settings)
                    { Transform = new() { Position = new(25, 400), Rotation = new(0, -1) } };
                using var challenger = new CarObject(new FakeKeyboard(), new FakeMouse(), loaded.Map.NavigationMap, opponent.Settings)
                    { Transform = new() { Position = new(25 + lateral, 400 + behind), Rotation = new(0, facingForward ? -1 : 1) } };
                defender.Controller.ResolveCarContact(defender.Transform.Position, new(0, -10));
                challenger.Controller.ResolveCarContact(challenger.Transform.Position, new(0, -(10 + closing)));
                var traffic = new[] { defender, challenger };
                for (var frame = 0; frame < 60; frame++)
                {
                    driver.Update(defender, traffic, 1f / 60);
                    if (!move) continue;
                    defender.Update(1000f / 60);
                    challenger.Update(1000f / 60);
                    Check(new OrientedBoxCollider(loaded.Map.NavigationMap, defender.Settings.Length, defender.Settings.Width)
                        .CanOccupy(defender.Transform.Position, defender.Transform.Rotation), "Defending car crossed a solid boundary.");
                }
                return move ? defender.Transform.Position.X - 25 : driver.Steering;
            }
            Check(Defend(1.1f, 5, 4) > .05f, "Opponent did not cover a faster racer's right passing lane.");
            Check(Defend(-1.1f, 5, 4) < -.05f, "Opponent did not cover a faster racer's left passing lane.");
            Check(MathF.Abs(Defend(1.1f, 30, 4)) < .01f, "Opponent defended against distant traffic.");
            Check(MathF.Abs(Defend(1.1f, 5, -2)) < .01f, "Opponent blocked a slower following car.");
            Check(MathF.Abs(Defend(1.1f, .5f, 4)) < .01f, "Opponent swerved into a car already alongside.");
            Check(MathF.Abs(Defend(1.1f, 5, 4, facingForward: false)) < .01f, "Opponent defended against oncoming traffic.");
            Check(Defend(1.1f, 5, 4, move: true) > .15f, "Defensive steering did not physically cover the passing lane.");
        }
        // A real pursuer holds throttle: the defender must cover its lane before it gets alongside.
        foreach (var skilled in new[] { false, true })
        foreach (var passingLane in new[] { 26.4f, 24.1f })
        {
            var driver = new OpponentDriver(route, loaded.Map.NavigationMap, opponent.Settings, new(),
                new(25, 435), skilled, gridSide: 0);
            using var defender = new CarObject(driver, driver, loaded.Map.NavigationMap, opponent.Settings)
                { Transform = new() { Position = new(25, 435), Rotation = new(0, -1) } };
            var keys = new FakeKeyboard { KeyState = new() { Forward = true } };
            using var pursuer = new CarObject(keys, new FakeMouse(), loaded.Map.NavigationMap, opponent.Settings)
                { Transform = new() { Position = new(passingLane, 441), Rotation = new(0, -1) } };
            defender.Controller.ResolveCarContact(defender.Transform.Position, new(0, -16));
            pursuer.Controller.ResolveCarContact(pursuer.Transform.Position, new(0, -18.5f));
            var traffic = new[] { defender, pursuer };
            var covered = false;
            for (var frame = 0; frame < 360; frame++)
            {
                driver.Update(defender, traffic, 1f / 120);
                defender.Update(1000f / 120);
                pursuer.Update(1000f / 120);
                var behind = pursuer.Transform.Position.Y - defender.Transform.Position.Y;
                if (behind < 3)
                {
                    covered = behind > defender.Settings.Length &&
                        MathF.Abs(pursuer.Transform.Position.X - defender.Transform.Position.X) < defender.Settings.Width;
                    break;
                }
            }
            Check(covered, $"{(skilled ? "Green" : "Blue")} defender let a moving pursuer reach the passing lane unopposed.");
            // Equal speed means the block worked; the defender should keep occupying that lane.
            pursuer.Controller.ResolveCarContact(pursuer.Transform.Position, defender.Controller.Velocity);
            for (var frame = 0; frame < 90; frame++)
            {
                driver.Update(defender, traffic, 1f / 120);
                defender.Update(1000f / 120);
                pursuer.Update(1000f / 120);
            }
            Check(MathF.Abs(pursuer.Transform.Position.X - defender.Transform.Position.X) < defender.Settings.Width,
                "Defender abandoned the passing lane as soon as the pursuer matched its speed.");
        }
        var start = opponent.Transform;
        var parked = scene.PlayerPose;
        Check(opponent.Settings.Length == ((CarObject)scene.Player).Settings.Length &&
            opponent.Settings.Width == ((CarObject)scene.Player).Settings.Width, "Opponent has a different collision footprint.");
        var opponentSprite = scene.CaptureFrame().Opponent!.Value;
        Check(opponentSprite.SwapRedBlue && (opponentSprite.ViewKind(Vector2.Zero) & 32) != 0,
            "Opponent did not share the car artwork with red/blue channel swapping.");
        var greenSprite = scene.CaptureFrame().SecondOpponent!.Value;
        Check(greenSprite.SwapRedGreen && (greenSprite.ViewKind(Vector2.Zero) & 64) != 0,
            "Green opponent did not carry its palette through the sprite flags.");
        Check(Vector2.Distance(start.Position, greenStart.Position) > opponent.Settings.Length &&
            Vector2.Distance(parked.Position, greenStart.Position) > opponent.Settings.Length, "Grid cars overlap.");
        for (var i = 0; i < 299; i++) scene.Update(1000f / 60);
        Check(opponent.Transform.Position == start.Position && green.Transform.Position == greenStart.Position, "Opponent moved before GO.");
        scene.SetPaused(true);
        var pausedRace = scene.RaceState;
        scene.Update(1000);
        Check(scene.RaceState == pausedRace && opponent.Transform.Position == start.Position && green.Transform.Position == greenStart.Position, "Pausing did not freeze the opponent and timing.");
        scene.SetPaused(false);
        var box = new OrientedBoxCollider(loaded.Map.NavigationMap, opponent.Settings.Length, opponent.Settings.Width);
        var lastLap = 0;
        for (var i = 0; i < 60 * 1200 && scene.RaceState?.Opponents.Any(r => !r.Finished) == true; i++)
        {
            scene.Update(1000f / 60);
            if (i == 180) Check(scene.PlayerPose == parked, "Opponent hit the parked player while leaving the grid.");
            if (i % 600 == 0) Check(box.CanOccupy(opponent.Transform.Position, opponent.Transform.Rotation) &&
                box.CanOccupy(green.Transform.Position, green.Transform.Rotation), "Opponent crossed a solid boundary.");
            if (scene.RaceState?.Opponent?.CompletedLaps > lastLap)
            {
                lastLap = scene.RaceState.Value.Opponent.Value.CompletedLaps;
                Console.WriteLine($"Opponent completed lap {lastLap} at {scene.RaceState.Value.Opponent.Value.TotalMilliseconds / 1000:F1}s.");
            }
        }
        Check(scene.RaceState?.Opponent is { Finished: true, CompletedLaps: 3, LapTimes.Count: 3 },
            $"Opponent failed to complete three validated laps: {scene.RaceState?.Opponent}, position {opponent.Transform.Position}.");
        var greenResult = scene.RaceState!.Value.Opponents[1];
        Console.WriteLine($"Green car: {greenResult.TotalMilliseconds / 1000:F1}s, laps {greenResult.CompletedLaps}, position {green.Transform.Position}.");
        Check(greenResult is { Finished: true, CompletedLaps: 3, LapTimes.Count: 3 }, "Green opponent failed to complete three validated laps.");
        var finishers = scene.RaceState.Value.Opponents.OrderBy(r => r.TotalMilliseconds).ToArray();
        Check(scene.RaceState.Value.Standings.Take(2).Select(r => r.Status.TotalMilliseconds)
            .SequenceEqual(finishers.Select(r => r.TotalMilliseconds)) && scene.RaceState.Value.Standings[2].IsPlayer,
            "Standings did not rank both finishers by their actual times ahead of the unfinished player.");
        Check(Math.Abs(scene.RaceState.Value.GapMilliseconds!.Value -
            (scene.RaceState.Value.TotalMilliseconds - finishers[0].TotalMilliseconds)) < .01,
            "Gap did not track the actual race leader.");

        // Blocking can change race order; compare the cornering profiles on an unobstructed lap.
        double SoloLap(bool skilled)
        {
            var startPosition = new Vector2(25.5f, 445.5f);
            var driver = new OpponentDriver(route, loaded.Map.NavigationMap, opponent.Settings, new(), startPosition, skilled, gridSide: 0);
            using var car = new CarObject(driver, driver, loaded.Map.NavigationMap, opponent.Settings)
                { Transform = new() { Position = startPosition, Rotation = new(0, -1) } };
            var race = new RaceSession(loaded.Map.RendererMap.Race!, new() { LapCount = 1 });
            race.BeginCountdown();
            race.AdvanceStart(5000);
            for (var frame = 0; frame < 60 * 300 && race.Status.Phase != RacePhase.Finished; frame++)
            {
                var before = car.Transform.Position;
                driver.Update(car, Array.Empty<CarObject>(), 1f / 60);
                car.Update(1000f / 60);
                race.Update(before, car.Transform.Position, 1000f / 60);
            }
            Check(race.Status.Phase == RacePhase.Finished, "Unobstructed opponent failed to finish a lap.");
            return race.Status.TotalMilliseconds;
        }
        var blueSolo = SoloLap(false);
        var greenSolo = SoloLap(true);
        Check(greenSolo < blueSolo, "Green driver's cornering did not beat the blue driver on an unobstructed lap.");
        Check(((CarObject)scene.Player).Controller.Speed == 0, "Parked player moved during the opponent race.");
        var opponentFinish = scene.RaceState!.Value.Opponent;
        var finishPose = opponent.Transform;
        scene.Update(250);
        Check(scene.RaceState.Value.Opponent == opponentFinish && opponent.Transform.Position == finishPose.Position,
            "Opponent continued moving or timing after its finish.");
        Check(scene.RaceState.Value.Phase == RacePhase.Racing, "Opponent finishing prematurely ended the player's race.");

        RaceStatus Laps(params double[] times) => new(times.Length, times.Sum(), 0, times.LastOrDefault(), true)
            { LapTimes = times };
        void Gap(RaceStatus player, RaceStatus rival, double now, double? expected, string message)
        {
            var gap = RaceLapGap.Calculate(player, rival, now);
            Check(expected == null ? gap == null : gap != null && Math.Abs(gap.Value - expected.Value) < .01, message);
        }
        Gap(Laps(), Laps(), 59000, null, "Gap appeared before either racer completed a lap.");
        Gap(Laps(60000), Laps(), 60000, 0, "First lap finish did not start the gap at zero.");
        Gap(Laps(60000), Laps(), 63000, 3000, "Player-first lap gap did not grow positively.");
        Gap(Laps(60000), Laps(), 65000, 5000, "Unmatched lap gap stopped growing.");
        Gap(Laps(60000), Laps(68000), 68000, 8000, "Second lap finisher did not freeze the measured separation.");
        Gap(Laps(60000), Laps(68000), 100000, 8000, "Completed lap gap changed before the next lap finish.");
        Gap(Laps(60000), Laps(68000,40000), 108000, 0, "Opponent-first next lap did not reset the gap.");
        Gap(Laps(60000), Laps(68000,40000), 111000, -3000, "Opponent-first lap gap did not grow negatively.");
        Gap(Laps(60000,55000), Laps(68000,40000), 115000, -7000, "Matching second lap finishes did not freeze the new gap.");
        Gap(Laps(60000,55000), Laps(68000,40000), 140000, -7000, "Negative completed gap did not stay frozen.");
        Gap(Laps(60000,55000,35000), Laps(68000,40000), 153000, 3000, "Final lap finish did not reset and reverse the lead.");
        Gap(Laps(60000,55000,35000), Laps(68000,40000,50000), 200000, 8000, "Final gap did not stay frozen after both finishes.");
        Gap(Laps(60000), Laps(68000,40000), 112000, -4000, "A lapped racer froze the gap for the wrong lap.");

        // Inelastic body contact separates equal car boxes and removes closing velocity.
        var playerCar = (CarObject)scene.Player;
        playerCar.Transform = new() { Position = new(25.5f,445.5f), Rotation = new(0,-1) };
        opponent.Transform = new() { Position = new(25.9f,445.5f), Rotation = new(0,-1) };
        playerCar.Controller.ResolveCarContact(playerCar.Transform.Position, new(2,0));
        opponent.Controller.ResolveCarContact(opponent.Transform.Position, new(-2,0));
        CarContact.Resolve(playerCar, opponent, loaded.Map.NavigationMap, false, false);
        Check(Vector2.Distance(playerCar.Transform.Position, opponent.Transform.Position) >= playerCar.Settings.Width,
            "Cars remained interpenetrating after contact.");
        Check(playerCar.Controller.Speed < .001f && opponent.Controller.Speed < .001f, "Car contact retained closing velocity.");
        Check(box.CanOccupy(playerCar.Transform.Position, playerCar.Transform.Rotation) &&
            box.CanOccupy(opponent.Transform.Position, opponent.Transform.Rotation), "Car contact pushed a car into the scenery.");

        using var playerFirst = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new());
        for (var i = 0; i < 20; i++) playerFirst.Update(250);
        var fastPlayer = (CarObject)playerFirst.Player;
        void Cross(RaceGateDefinition gate)
        {
            var direction = Vector2.Normalize(new Vector2(gate.Direction[0], gate.Direction[1]));
            var position = new Vector2(gate.Center[0], gate.Center[1]) - direction * .02f;
            fastPlayer.Transform = new() { Position = position, Rotation = direction };
            fastPlayer.Controller.ResolveCarContact(position, direction * 2);
            playerFirst.Update(1000f / 60);
        }
        var gates = loaded.Map.RendererMap.Race!;
        Cross(gates.StartFinish);
        for (var lap = 0; lap < 3; lap++)
        {
            foreach (var gate in gates.Checkpoints) Cross(gate);
            Cross(gates.StartFinish);
        }
        Check(playerFirst.RaceState is { Phase: RacePhase.Finished, Opponent.Finished: false }, "Player-first finish did not open results while the opponent was racing.");
        playerFirst.Update(250);
        Check(playerFirst.RaceState?.GapMilliseconds == 0, "Leading player did not show zero gap to the leader.");
        playerFirst.SetPaused(true); // Pointer capture releases when results open.
        var stoppedPose = playerFirst.PlayerPose;
        var stoppedTime = playerFirst.RaceState!.Value.TotalMilliseconds;
        for (var i = 0; i < 5000 && playerFirst.RaceState?.Opponents.Any(r => !r.Finished) == true; i++) playerFirst.Update(250);
        var finalResults = playerFirst.RaceState ?? throw new Exception("Race results disappeared.");
        Check(finalResults.Opponent is { Finished: true, LapTimes.Count: 3 }, "Opponent did not finish while player results were open.");
        Check(playerFirst.PlayerPose == stoppedPose && finalResults.TotalMilliseconds == stoppedTime,
            "Player moved or kept timing while waiting for opponent results.");
        Check(finalResults.Opponents.All(r => r.Finished), "Results stopped before both rivals finished.");
        Check(finalResults.GapMilliseconds == 0 && finalResults.Standings[0].IsPlayer,
            "Winning player did not retain first place and zero gap to the leader.");
        Console.WriteLine($"PASS: {count} opponent assertions.");
        return count;
    }
}
