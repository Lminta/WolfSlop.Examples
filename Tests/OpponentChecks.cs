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
        var playerSettings = ((CarObject)scene.Player).Settings;
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        foreach (var rival in new[] { opponent, green })
        {
            Check(rival.Settings.Length == playerSettings.Length && rival.Settings.Width == playerSettings.Width &&
                rival.Settings.Height == playerSettings.Height && rival.Settings.Wheelbase == playerSettings.Wheelbase,
                "Rival dimensions differ from the player's dimensions.");
            // Compare at the same pose and distance so perspective cannot hide a size difference.
            using var alignedRival = new CarObject(new FakeKeyboard(), new FakeMouse(), loaded.Map.NavigationMap, rival.Settings)
                { Transform = scene.Player.Transform };
            for (var direction = 0; direction < 8; direction++)
            {
                var angle = direction * MathF.Tau / 8;
                var observer = scene.Player.Transform.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 5;
                var playerSprite = loaded.Character.CreateSprite(scene.Player, observer);
                var rivalSprite = loaded.Character.CreateSprite(alignedRival, observer);
                Check(playerSprite.ViewScale(observer) == rivalSprite.ViewScale(observer) &&
                    playerSprite.Elevation == rivalSprite.Elevation,
                    $"Rival sprite differs from player size in direction {direction}.");
            }
        }
        Check(MathF.Abs(opponent.Settings.Asphalt.MaxForwardSpeed - playerSettings.Asphalt.MaxForwardSpeed - 3) < .001f,
            "Blue top speed is not 3 blocks/s above the player's.");
        Check(MathF.Abs(green.Settings.Asphalt.MaxForwardSpeed - playerSettings.Asphalt.MaxForwardSpeed + 3) < .001f,
            "Green top speed is not 3 blocks/s below the player's.");
        var tunedPlayer = playerSettings with { Asphalt = playerSettings.Asphalt with { MaxForwardSpeed = 32 } };
        Check(OpponentDriver.CreateCarSettings(tunedPlayer, false).Asphalt.MaxForwardSpeed == 35 &&
            OpponentDriver.CreateCarSettings(tunedPlayer, true).Asphalt.MaxForwardSpeed == 29,
            "Rival speed differences did not follow a tuned player speed.");
        // Check actual throttle/braking too, so an old shared AI cap cannot hide the speed differences.
        var straightMap = new NavigationMap();
        var straightCells = Enumerable.Range(0, 50).Select(_ => Enumerable.Repeat(true, 1000).ToArray()).ToArray();
        straightMap.Replace(straightCells, straightCells.Select(c => new bool[c.Length]).ToArray(),
            straightCells.Select(c => new bool[c.Length]).ToArray(),
            straightCells.Select(c => c.Select(_ => "asphalt").ToArray()).ToArray());
        var straightRoute = new RaceRoute(new RaceMinimapDefinition
        {
            StartFinish = [40, 500], Route = [[10, 10], [40, 10], [40, 990], [10, 990], [10, 10]]
        });
        foreach (var skilled in new[] { false, true })
        {
            var position = new Vector2(40, 500);
            var rivalSettings = OpponentDriver.CreateCarSettings(playerSettings, skilled);
            var driver = new OpponentDriver(straightRoute, straightMap, rivalSettings, new(), position, skilled, gridSide: 0);
            using var rival = new CarObject(driver, driver, straightMap, rivalSettings)
                { Transform = new() { Position = position, Rotation = Vector2.UnitY } };
            rival.Controller.ResolveCarContact(position, Vector2.UnitY * playerSettings.Asphalt.MaxForwardSpeed);
            for (var frame = 0; frame < 120; frame++)
            {
                driver.Update(rival, Array.Empty<CarObject>(), 1f / 120);
                rival.Update(1000f / 120);
            }
            Check(skilled ? rival.Controller.Speed <= rivalSettings.Asphalt.MaxForwardSpeed + .5f :
                    rival.Controller.Speed > playerSettings.Asphalt.MaxForwardSpeed + .05f,
                "Rival AI throttle/braking did not use its individual top speed on a clear straight.");
        }
        // A faster rival approaches on one side, while the player passes on the other.
        foreach (var skilled in new[] { false, true })
        {
            var rivalSettings = OpponentDriver.CreateCarSettings(playerSettings, skilled);
            var driver = new OpponentDriver(straightRoute, straightMap, rivalSettings, new(), new(40, 500), skilled, gridSide: 0);
            using var defender = new CarObject(driver, driver, straightMap, rivalSettings)
                { Transform = new() { Position = new(40, 500), Rotation = Vector2.UnitY } };
            using var passingPlayer = new CarObject(new FakeKeyboard(), new FakeMouse(), straightMap, playerSettings)
                { Transform = new() { Position = new(41.2f, 494), Rotation = Vector2.UnitY } };
            using var rival = new CarObject(new FakeKeyboard(), new FakeMouse(), straightMap, opponent.Settings)
                { Transform = new() { Position = new(38.8f, 495), Rotation = Vector2.UnitY } };
            defender.Controller.ResolveCarContact(defender.Transform.Position, Vector2.UnitY * 16);
            passingPlayer.Controller.ResolveCarContact(passingPlayer.Transform.Position, Vector2.UnitY * 20);
            rival.Controller.ResolveCarContact(rival.Transform.Position, Vector2.UnitY * 23);
            var traffic = new[] { defender, rival, passingPlayer };
            for (var frame = 0; frame < 120; frame++) driver.Update(defender, traffic, 1f / 120, passingPlayer);
            Check(driver.Steering < -.025f, "Opponent covered the faster rival instead of the passing player.");
        }
        // Once the player is ahead, a rival must not keep the blue car trapped in a blocking duel.
        var chaseBlueDriver = new OpponentDriver(straightRoute, straightMap, opponent.Settings, new(), new(40, 200), gridSide: 0);
        var chaseGreenDriver = new OpponentDriver(straightRoute, straightMap, green.Settings, new(), new(41.1f, 195), skilled: true, gridSide: 0);
        using (var chasingBlue = new CarObject(chaseBlueDriver, chaseBlueDriver, straightMap, opponent.Settings)
            { Transform = new() { Position = new(40, 200), Rotation = Vector2.UnitY } })
        using (var chasingGreen = new CarObject(chaseGreenDriver, chaseGreenDriver, straightMap, green.Settings)
            { Transform = new() { Position = new(41.1f, 195), Rotation = Vector2.UnitY } })
        using (var leadingPlayer = new CarObject(new FakeKeyboard { KeyState = new() { Forward = true } }, new FakeMouse(), straightMap, playerSettings)
            { Transform = new() { Position = new(40, 230), Rotation = Vector2.UnitY } })
        {
            chasingBlue.Controller.ResolveCarContact(chasingBlue.Transform.Position, Vector2.UnitY * 20);
            chasingGreen.Controller.ResolveCarContact(chasingGreen.Transform.Position, Vector2.UnitY * 22);
            leadingPlayer.Controller.ResolveCarContact(leadingPlayer.Transform.Position, Vector2.UnitY * playerSettings.Asphalt.MaxForwardSpeed);
            var traffic = new[] { leadingPlayer, chasingBlue, chasingGreen };
            for (var frame = 0; frame < 120 * 20; frame++)
            {
                leadingPlayer.Update(1000f / 120);
                chaseBlueDriver.Update(chasingBlue, traffic, 1f / 120, leadingPlayer);
                chaseGreenDriver.Update(chasingGreen, traffic, 1f / 120, leadingPlayer);
                chasingBlue.Update(1000f / 120);
                chasingGreen.Update(1000f / 120);
                for (var a = 0; a < traffic.Length; a++)
                    for (var b = a + 1; b < traffic.Length; b++) CarContact.Resolve(traffic[a], traffic[b], straightMap, 1f / 120, false, false);
            }
            var gap = leadingPlayer.Transform.Position.Y - chasingBlue.Transform.Position.Y;
            Console.WriteLine($"Player chase: blue gap {gap:F1} blocks, blue {chasingBlue.Controller.Speed:F1}, green {chasingGreen.Controller.Speed:F1} blocks/s.");
            Check(gap < 27 && chasingBlue.Controller.Speed > playerSettings.Asphalt.MaxForwardSpeed,
                "Blue did not break off its rival fight and close the player's lead at its own configured speed.");
            Check(chasingGreen.Transform.Position.Y > 545 && chasingGreen.Controller.Speed > 18,
                "Green stayed trapped in a rival battle instead of resuming racing.");
        }
        // A normal player launch must not leave both rivals crawling in their grid lanes.
        using (var launch = new GameScene(loaded.Map, loaded.Character,
            new FakeKeyboard { KeyState = new() { Forward = true } }, new FakeMouse(), new()))
        {
            for (var frame = 0; frame < 20; frame++) launch.Update(250);
            for (var frame = 0; frame < 60 * 4; frame++) launch.Update(1000f / 60);
            Console.WriteLine($"Launch speeds: blue {launch.Opponent!.Controller.Speed:F1}, green {launch.SecondOpponent!.Controller.Speed:F1}.");
            Check(launch.Opponent.Controller.Speed > 12 && launch.SecondOpponent.Controller.Speed > 12,
                "Opponents crawled in their launch or blocking lanes while the player accelerated away.");
            for (var frame = 0; frame < 60 * 2; frame++) launch.Update(1000f / 60);
            var player = (CarObject)launch.Player;
            Console.WriteLine($"Launch: player {player.Transform.Position}, blue {launch.Opponent!.Transform.Position}, green {launch.SecondOpponent!.Transform.Position}.");

            Check(launch.Opponent.Transform.Position.Y - player.Transform.Position.Y < 12 &&
                launch.SecondOpponent.Transform.Position.Y - player.Transform.Position.Y < 12,
                "Opponents lost the player on the opening straight through unnecessary rival fighting.");
        }
        // Both driver profiles defend either passing lane against any faster racer.
        var route = new RaceRoute(loaded.Map.RendererMap.RaceMinimap!);
        foreach (var skilled in new[] { false, true })
        {
            float Defend(float lateral, float behind, float closing, bool facingForward = true, bool move = false)
            {
                var driver = new OpponentDriver(route, loaded.Map.NavigationMap, opponent.Settings, new(),
                    new(25.5f, 400), skilled, gridSide: 0);
                using var defender = new CarObject(driver, driver, loaded.Map.NavigationMap, opponent.Settings)
                    { Transform = new() { Position = new(25.5f, 400), Rotation = new(0, -1) } };
                using var challenger = new CarObject(new FakeKeyboard(), new FakeMouse(), loaded.Map.NavigationMap, opponent.Settings)
                    { Transform = new() { Position = new(25.5f + lateral, 400 + behind), Rotation = new(0, facingForward ? -1 : 1) } };
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
                return move ? defender.Transform.Position.X - 25.5f : driver.Steering;
            }
            var cruising = Defend(0, 30, -2);
            Check(Defend(1.1f, 5, 4) > cruising + .025f, "Opponent did not cover a faster racer's right passing lane.");
            Check(Defend(-1.1f, 5, 4) < cruising - .025f, "Opponent did not cover a faster racer's left passing lane.");
            Check(MathF.Abs(Defend(1.1f, 30, 4) - cruising) < .01f, "Opponent defended against distant traffic.");
            Check(MathF.Abs(Defend(1.1f, 5, -2) - cruising) < .01f, "Opponent blocked a slower following car.");
            Check(MathF.Abs(Defend(1.1f, .5f, 4) - cruising) < .01f, "Opponent swerved into a car already alongside.");
            Check(MathF.Abs(Defend(1.1f, 5, 4, facingForward: false) - cruising) < .01f, "Opponent defended against oncoming traffic.");
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
        bool OffRoad(CarObject racer)
        {
            var footprint = new OrientedBoxCollider(loaded.Map.NavigationMap, racer.Settings.Length, racer.Settings.Width);
            var position = racer.Transform.Position;
            for (var x = (int)MathF.Floor(position.X - racer.Settings.Length); x <= (int)MathF.Ceiling(position.X + racer.Settings.Length); x++)
            for (var y = (int)MathF.Floor(position.Y - racer.Settings.Length); y <= (int)MathF.Ceiling(position.Y + racer.Settings.Length); y++)
                if (footprint.OverlapsCell(position, racer.Transform.Rotation, x, y) && loaded.Map.NavigationMap.GetSurface(x, y) != "asphalt") return true;
            return false;
        }
        foreach (var skilled in new[] { false, true })
        {
            var position = new Vector2(27.5f, 395);
            var heading = Vector2.Normalize(new Vector2(.35f, -1));
            var driver = new OpponentDriver(route, loaded.Map.NavigationMap, opponent.Settings, new(), position, skilled, gridSide: 0);
            using var recovering = new CarObject(driver, driver, loaded.Map.NavigationMap, opponent.Settings)
                { Transform = new() { Position = position, Rotation = heading } };
            recovering.Controller.ResolveCarContact(position, heading * 10);
            for (var frame = 0; frame < 60 * 12; frame++)
            {
                driver.Update(recovering, Array.Empty<CarObject>(), 1f / 60);
                recovering.Update(1000f / 60);
            }
            Console.WriteLine($"Recovery {skilled}: {recovering.Transform.Position}, speed {recovering.Controller.Speed:F1}, mud {OffRoad(recovering)}");
            Check(!OffRoad(recovering) && recovering.Controller.Speed > 3,
                "Opponent did not recover onto the road after being knocked onto the verge.");
        }
        // Start pointed at the outside wall of a narrow ring, as after a collision.
        var ringMap = new NavigationMap();
        var ringCells = Enumerable.Range(0, 50).Select(x => Enumerable.Range(0, 50)
            .Select(y => MathF.Abs(Vector2.Distance(new(x + .5f, y + .5f), new(24.5f)) - 15) < 3).ToArray()).ToArray();
        ringMap.Replace(ringCells, ringCells.Select(c => new bool[c.Length]).ToArray(),
            ringCells.Select(c => new bool[c.Length]).ToArray(),
            ringCells.Select(c => c.Select(open => open ? "asphalt" : "mud").ToArray()).ToArray());
        var ringRoute = new RaceRoute(new RaceMinimapDefinition
        {
            StartFinish = [39.5f, 24.5f],
            Route = Enumerable.Range(0, 129).Select(i => new[] { 24.5f + 15 * MathF.Cos(i * MathF.Tau / 128),
                24.5f + 15 * MathF.Sin(i * MathF.Tau / 128) }).ToArray()
        });
        foreach (var skilled in new[] { false, true })
        {
            var position = new Vector2(39.5f, 24.5f);
            var driver = new OpponentDriver(ringRoute, ringMap, opponent.Settings, new(), position, skilled, gridSide: 0);
            using var car = new CarObject(driver, driver, ringMap, opponent.Settings)
                { Transform = new() { Position = position, Rotation = Vector2.UnitX } };
            car.Controller.ResolveCarContact(position, Vector2.UnitX * 6);
            for (var frame = 0; frame < 60 * 18; frame++)
            {
                driver.Update(car, Array.Empty<CarObject>(), 1f / 60);
                car.Update(1000f / 60);
            }
            Console.WriteLine($"Wall recovery {skilled}: {car.Transform.Position}, speed {car.Controller.Speed:F1}");
            Check(car.Controller.Speed > 3 && Vector2.Distance(car.Transform.Position, position) > 5,
                "Opponent remained stuck pointing into a barrier after a collision.");
        }
        var start = opponent.Transform;
        var parked = scene.PlayerPose;
        Check(opponent.Settings.Length == ((CarObject)scene.Player).Settings.Length &&
            opponent.Settings.Width == ((CarObject)scene.Player).Settings.Width, "Opponent has a different collision footprint.");
        var opponentSprite = scene.CaptureFrame().Opponent!.Value;
        var frontObserver = start.Position + start.Rotation * 5;
        var frontView = loaded.Character.CreateSprite(opponent, frontObserver).ViewScale(frontObserver);
        Check(MathF.Abs(frontView.X - opponent.Settings.Width) < .0001f &&
            MathF.Abs(frontView.Y - opponent.Settings.Height) < .0001f,
            "Car sprite did not match the sports-car width and height.");
        Check(MathF.Abs(opponentSprite.Elevation - opponent.Settings.Height * .5f + .505f) < .0001f,
            "Resizing the car sprite lifted its tires off the ground.");
        Check(opponentSprite.SwapRedBlue && (opponentSprite.ViewKind(Vector2.Zero) & 32) != 0,
            "Opponent did not share the car artwork with red/blue channel swapping.");
        var greenSprite = scene.CaptureFrame().SecondOpponent!.Value;
        Check(greenSprite.SwapRedGreen && (greenSprite.ViewKind(Vector2.Zero) & 64) != 0,
            "Green opponent did not carry its palette through the sprite flags.");
        bool GridClear(Vector2 first, Vector2 second)
        {
            var delta = first - second;
            var side = new Vector2(-start.Rotation.Y, start.Rotation.X);
            return MathF.Abs(Vector2.Dot(delta, start.Rotation)) > opponent.Settings.Length ||
                MathF.Abs(Vector2.Dot(delta, side)) > opponent.Settings.Width;
        }
        Check(GridClear(start.Position, greenStart.Position) && GridClear(parked.Position, greenStart.Position), "Grid cars overlap.");
        for (var i = 0; i < 299; i++) scene.Update(1000f / 60);
        Check(opponent.Transform.Position == start.Position && green.Transform.Position == greenStart.Position, "Opponent moved before GO.");
        scene.SetPaused(true);
        var pausedRace = scene.RaceState;
        scene.Update(1000);
        Check(scene.RaceState == pausedRace && opponent.Transform.Position == start.Position && green.Transform.Position == greenStart.Position, "Pausing did not freeze the opponent and timing.");
        scene.SetPaused(false);
        var box = new OrientedBoxCollider(loaded.Map.NavigationMap, opponent.Settings.Length, opponent.Settings.Width);
        var lastLap = 0;
        var raceSamples = new int[2];
        var raceMud = new int[2];
        var raceSlow = new int[2];
        var raceSpeeds = new[] { new List<float>(), new List<float>() };
        var trafficWorst = new[] { new Dictionary<(int, int), int>(), new Dictionary<(int, int), int>() };
        for (var i = 0; i < 60 * 1200 && scene.RaceState?.Opponents.Any(r => !r.Finished) == true; i++)
        {
            scene.Update(1000f / 60);
            if (i % 6 == 0 && i > 120)
            {
                var racers = new[] { opponent, green };
                for (var racer = 0; racer < racers.Length; racer++)
                {
                    if (scene.RaceState!.Value.Opponents[racer].Finished) continue;
                    raceSamples[racer]++;
                    raceSpeeds[racer].Add(racers[racer].Controller.Speed);
                    if (OffRoad(racers[racer]))
                    {
                        raceMud[racer]++;
                        var position = racers[racer].Transform.Position;
                        var cell = ((int)(position.X / 8) * 8, (int)(position.Y / 8) * 8);
                        trafficWorst[racer][cell] = trafficWorst[racer].GetValueOrDefault(cell) + 1;
                    }
                    if (racers[racer].Controller.Speed < 1) raceSlow[racer]++;
                }
            }
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
        for (var racer = 0; racer < 2; racer++)
        {
            Console.WriteLine($"Traffic {racer}: mud {100f * raceMud[racer] / raceSamples[racer]:F1}%, slow {raceSlow[racer]}, worst {string.Join("; ", trafficWorst[racer].OrderByDescending(p => p.Value).Take(6))}");
            Check(raceMud[racer] < raceSamples[racer] * .01f, "Opponent spent too much of a traffic race off the asphalt.");
            raceSpeeds[racer].Sort();
            var median = raceSpeeds[racer][raceSpeeds[racer].Count / 2];
            Check(median >= 15 && median <= 16.5f,
                $"Opponent median race pace regressed outside the 15-16 blocks/s target: {median:F2} blocks/s.");
        }
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
        void SoloLap(bool skilled)
        {
            var startPosition = new Vector2(25.5f, 445.5f);
            var rivalSettings = OpponentDriver.CreateCarSettings(playerSettings, skilled);
            var driver = new OpponentDriver(route, loaded.Map.NavigationMap, rivalSettings, new(), startPosition, skilled, gridSide: 0);
            using var car = new CarObject(driver, driver, loaded.Map.NavigationMap, rivalSettings)
                { Transform = new() { Position = startPosition, Rotation = new(0, -1) } };
            var race = new RaceSession(loaded.Map.RendererMap.Race!, new() { LapCount = 1 });
            race.BeginCountdown();
            race.AdvanceStart(5000);
            var samples = 0;
            var muddy = 0;
            var slow = 0;
            var worst = new Dictionary<(int X, int Y), int>();
            for (var frame = 0; frame < 60 * 300 && race.Status.Phase != RacePhase.Finished; frame++)
            {
                var before = car.Transform.Position;
                driver.Update(car, Array.Empty<CarObject>(), 1f / 60);
                car.Update(1000f / 60);
                race.Update(before, car.Transform.Position, 1000f / 60);
                if (frame % 6 == 0 && frame > 60)
                {
                    samples++;
                    var position = car.Transform.Position;
                    var offRoad = OffRoad(car);
                    if (car.Controller.Speed < 1) slow++;
                    if (offRoad)
                    {
                        muddy++;
                        var cell = ((int)(position.X / 8) * 8, (int)(position.Y / 8) * 8);
                        worst[cell] = worst.GetValueOrDefault(cell) + 1;
                    }
                }
            }
            Console.WriteLine($"SOLO {(skilled ? "Green" : "Blue")}: {race.Status.TotalMilliseconds / 1000:F1}s, mud {100f * muddy / samples:F1}%, slow {slow}, worst {string.Join("; ", worst.OrderByDescending(p => p.Value).Take(8))}");
            Check(skilled ? driver.MistakesMade == 0 : driver.MistakesMade > 0, "Rival did not follow its intended mistake profile.");
            Check(muddy < samples * .01f, "Opponent repeatedly cut corners or ran wide onto mud on an unobstructed lap.");
            Check(slow == 0, "Unobstructed opponent stalled against track barriers.");
            Check(race.Status.Phase == RacePhase.Finished, "Unobstructed opponent failed to finish a lap.");
        }
        SoloLap(false);
        SoloLap(true);
        Check(((CarObject)scene.Player).Controller.Speed == 0, "Parked player moved during the opponent race.");
        Check(opponent.Controller.Speed == 0 && green.Controller.Speed == 0,
            "Finished cars retained moving velocities that confused the remaining racers.");
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
        CarContact.Resolve(playerCar, opponent, loaded.Map.NavigationMap, 1f / 120, false, false);
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
