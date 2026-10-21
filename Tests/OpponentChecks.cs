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
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        var start = opponent.Transform;
        var parked = scene.PlayerPose;
        Check(opponent.Settings.Length == ((CarObject)scene.Player).Settings.Length &&
            opponent.Settings.Width == ((CarObject)scene.Player).Settings.Width, "Opponent has a different collision footprint.");
        var opponentSprite = scene.CaptureFrame().Opponent!.Value;
        Check(opponentSprite.SwapRedBlue && (opponentSprite.ViewKind(Vector2.Zero) & 32) != 0,
            "Opponent did not share the car artwork with red/blue channel swapping.");
        for (var i = 0; i < 299; i++) scene.Update(1000f / 60);
        Check(opponent.Transform.Position == start.Position, "Opponent moved before GO.");
        scene.SetPaused(true);
        var pausedRace = scene.RaceState;
        scene.Update(1000);
        Check(scene.RaceState == pausedRace && opponent.Transform.Position == start.Position, "Pausing did not freeze the opponent and timing.");
        scene.SetPaused(false);
        var box = new OrientedBoxCollider(loaded.Map.NavigationMap, opponent.Settings.Length, opponent.Settings.Width);
        var lastLap = 0;
        for (var i = 0; i < 60 * 1200 && scene.RaceState?.Opponent?.Finished != true; i++)
        {
            scene.Update(1000f / 60);
            if (i == 180) Check(scene.PlayerPose == parked, "Opponent hit the parked player while leaving the grid.");
            if (i % 600 == 0) Check(box.CanOccupy(opponent.Transform.Position, opponent.Transform.Rotation), "Opponent crossed a solid boundary.");
            if (scene.RaceState?.Opponent?.CompletedLaps > lastLap)
            {
                lastLap = scene.RaceState.Value.Opponent.Value.CompletedLaps;
                Console.WriteLine($"Opponent completed lap {lastLap} at {scene.RaceState.Value.Opponent.Value.TotalMilliseconds / 1000:F1}s.");
            }
        }
        Check(scene.RaceState?.Opponent is { Finished: true, CompletedLaps: 3, LapTimes.Count: 3 },
            $"Opponent failed to complete three validated laps: {scene.RaceState?.Opponent}, position {opponent.Transform.Position}.");
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
        Check(playerFirst.RaceState?.GapMilliseconds > 0, "Player-first finish did not show a growing positive lead.");
        playerFirst.SetPaused(true); // Pointer capture releases when results open.
        var stoppedPose = playerFirst.PlayerPose;
        var stoppedTime = playerFirst.RaceState!.Value.TotalMilliseconds;
        for (var i = 0; i < 5000 && playerFirst.RaceState?.Opponent?.Finished == false; i++) playerFirst.Update(250);
        var finalResults = playerFirst.RaceState ?? throw new Exception("Race results disappeared.");
        Check(finalResults.Opponent is { Finished: true, LapTimes.Count: 3 }, "Opponent did not finish while player results were open.");
        Check(playerFirst.PlayerPose == stoppedPose && finalResults.TotalMilliseconds == stoppedTime,
            "Player moved or kept timing while waiting for opponent results.");
        Check(Math.Abs(finalResults.GapMilliseconds!.Value -
            (finalResults.Opponent!.Value.TotalMilliseconds - stoppedTime)) < .01,
            "Final gap did not equal the racers' actual finish-time difference.");
        Console.WriteLine($"PASS: {count} opponent assertions.");
        return count;
    }
}
