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

        var route = new RaceRoute(new() { Route = [[0,0], [100,0], [100,100], [0,100], [0,0]], StartFinish = [10,0] });
        var playerProgress = new RaceProgress(route, new(10, 0));
        var opponentProgress = new RaceProgress(route, new(10, 0));
        playerProgress.Update(new(30, 0), 1000); opponentProgress.Update(new(20, 0), 1000);
        Check(Math.Abs(RaceProgress.Gap(playerProgress, opponentProgress, 1000) - 500) < .01,
            "Leading gap did not compare arrival time at the same track position.");
        Check(Math.Abs(RaceProgress.Gap(playerProgress, opponentProgress, 2000) - 1500) < .01, "Lead did not grow while the opponent waited.");
        playerProgress.Update(new(15, 0), 2000);
        Check(RaceProgress.Gap(playerProgress, opponentProgress, 2000) < 0, "Reversing past the opponent did not change the gap sign.");
        var lapProgress = new RaceProgress(route, new(10,0));
        foreach (var point in new Vector2[] { new(100,0), new(100,100), new(0,100), new(0,0), new(10,0) }) lapProgress.Update(point, 4000);
        Check(lapProgress.Distance > 399, "Race progress reset when wrapping across the lap line.");
        var stationary = new RaceProgress(route, new(10,0));
        Check(RaceProgress.Gap(lapProgress, stationary, 5000) > 0, "A full-lap lead compared against the wrong lap.");

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
