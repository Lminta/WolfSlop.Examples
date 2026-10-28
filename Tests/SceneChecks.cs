using System.Numerics;
using WolfNET.Engine.World;
using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Graphics;

internal static class SceneChecks
{
    public static async Task<int> RunAsync(HttpClient http)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { assertions++; return; }
            throw new Exception($"Expected {typeof(T).Name}.");
        }

        var keyboard = new KeyboardState();
        var settings = new RendererSettings { FovDegrees = 90 };
        var loaded = await new GameSceneLoader(http, settings).LoadAsync("maps/level1.json");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await new GameSceneLoader(http, settings).LoadAsync("maps/level1.json", canceled.Token);
            throw new Exception("Canceled scene loading completed.");
        }
        catch (OperationCanceledException) { assertions++; }

        var map = MakeMap(9, 9);
        using var scene = new GameScene(map, loaded.Character, keyboard, new FakeMouse(), settings);
        Check(scene.Player.Transform.Position == new Vector2(4.5f) && scene.Player.Transform.Rotation == Vector2.UnitX,
            "Scene did not initialize the player's transform.");
        Check(Math.Abs(scene.Player.Camera.Plane.Length() - 1) < .00001f, "Scene did not apply configured FOV.");
        Check(scene.CaptureFrame().Character == null, "First-person scene drew the avatar.");
        Check(scene.CaptureFrame().FirstPerson is { Kind: FirstPersonViewKind.Hands, InAir: false, Crouching: false, HandMotion: var idleMotion } && idleMotion == Vector4.Zero &&
              scene.DrivingState == null, "Walking scene did not initialize its first-person hands state.");
        keyboard.Press("KeyV");
        keyboard.Press("KeyW");
        scene.Update(160);
        keyboard.Release("KeyW");
        Check(scene.Player.Camera.ThirdPerson && scene.Player.Transform.Position.X > 4.8f,
            "Scene did not update the real player controller.");
        Check(scene.CaptureFrame().Character != null && scene.Player.AnimationFrame != 14,
            "Scene did not connect movement, animation, and third-person rendering.");
        Check(scene.CaptureFrame().FirstPerson.Kind == FirstPersonViewKind.None &&
              MathF.Abs(((PlayerObject)scene.Player).Animation.HandSwing) > .1f,
            "Third-person frame retained first-person sprites or lost player animation.");
        scene.Player.Camera.ThirdPerson = false;
        keyboard.Press("KeyF");
        scene.Update(1000);
        Check(map.Doors.Single().Openness == 1 && map.NavigationMap.IsWalkable(5, 4),
            "Scene did not route interaction and door updates.");
        Check(scene.CaptureFrame().FirstPerson is { HandMotion: var restingMotion } && MathF.Abs(restingMotion.X) < .001f && restingMotion.Y < .001f,
            "Hands continued walking after the player stopped.");
        scene.Update(90);
        Check(scene.CaptureFrame().FirstPerson.HandMotion.W > .5f, "Successful interaction did not animate the hand.");
        scene.Update(400);
        keyboard.Press("KeyF", repeat: true);
        scene.Update(90);
        Check(scene.CaptureFrame().FirstPerson.HandMotion.W == 0, "Held F repeated the action animation or the hand did not return to rest.");
        keyboard.Release("KeyF");
        scene.Player.Transform = new() { Position = scene.Player.Transform.Position, Rotation = -Vector2.UnitX };
        keyboard.Press("KeyF");
        scene.Update(16);
        scene.Update(90);
        Check(scene.CaptureFrame().FirstPerson.HandMotion.W > .5f && scene.ConsumeMapRequest() == null && map.Doors.Single().Openness == 1,
            "Pressing F without an interaction target did not animate the hand independently.");
        keyboard.Release("KeyF");
        scene.Update(400);
        Check(scene.CaptureFrame().FirstPerson.HandMotion.W == 0, "Empty interaction gesture did not finish.");
        scene.Dispose();
        Throws<ObjectDisposedException>(() => scene.Update(16));

        Console.WriteLine($"PASS: {assertions} application scene assertions (loading, cancellation, walking, animation, and interaction).");
        return assertions;
    }

    private static TileMap MakeMap(int width, int height, int doorX = 5)
    {
        var cells = Enumerable.Range(0, width).Select(x => Enumerable.Range(0, height)
            .Select(y => x == 0 || y == 0 || x == width - 1 || y == height - 1 ? 1 : 0).ToArray()).ToArray();
        cells[doorX][4] = 2;
        var definition = new MapDefinition
        {
            WorldMap = cells,
            Spawn = new ActorStartDefinition { X = 4.5f, Y = 4.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 2, RotSpeed = 2 }
        };
        return new TileMap(definition, new Dictionary<int, MapObjectDefinition>
        {
            [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false },
            [1] = new WallDefinition { Id = 1, PassageId = 0, RaycastTransparent = false, Collides = true },
            [2] = new DoorDefinition { Id = 2, PassageId = 0, Axis = "x", RaycastTransparent = false, Collides = true }
        }, 64, 64);
    }

}
