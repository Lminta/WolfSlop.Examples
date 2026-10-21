using System.Net.Http.Json;
using System.Numerics;
using WolfNET.Game;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player;
using WolfNET.Graphics;

internal static class CarSceneChecks
{
    public static async Task<int> RunAsync(HttpClient http)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        void Reject(GameplaySettings invalid)
        {
            try { invalid.Validate(); } catch (InvalidDataException) { count++; return; }
            throw new Exception("Invalid car gameplay settings were accepted.");
        }
        var gameplay = await http.GetFromJsonAsync<GameplaySettings>("gameplay-settings.json") ?? throw new Exception("Missing gameplay settings");
        gameplay.Validate();
        Check(gameplay.Car.Length == .9f && gameplay.Car.Width == .7f && gameplay.Car.Asphalt.MaxForwardSpeed == 18 &&
            gameplay.Car.OffRoad.MaxForwardSpeed == 8, "Approved gameplay defaults did not load from JSON.");
        Reject(gameplay with { Car = gameplay.Car with { Width = float.NaN } });
        Reject(gameplay with { Car = gameplay.Car with { Wheelbase = 2 } });
        Reject(gameplay with { Car = gameplay.Car with { PartialMudPenalty = 1.1f } });
        Reject(gameplay with { Car = gameplay.Car with { MudPullStrength = -1 } });
        Reject(gameplay with { Car = gameplay.Car with { OffRoad = gameplay.Car.OffRoad with { SurfacePenaltyCoefficient = -1 } } });
        Reject(gameplay with { Car = gameplay.Car with { Asphalt = gameplay.Car.Asphalt with { MaxReverseSpeed = 19 } } });

        var renderer = new RendererSettings();
        var loaded = await new GameSceneLoader(http, renderer, gameplay).LoadAsync("maps/pine-loop.json");
        Check(loaded.Character is CarAppearance, "Race loader still selected the walking character.");
        var appearance = (CarAppearance)loaded.Character;
        var textures = appearance.AddTextures(loaded.Map.RendererMap.Textures);
        Check(Enumerable.Range(0, 8).All(i => textures.Layers[appearance.FirstLayer + (uint)i] == "textures/race/car-game-atlas.png"),
            "Car's eight directional frames were not added to scene textures.");
        var keyboard = new KeyboardState();
        var mouse = new Mouse();
        using var scene = new GameScene(loaded.Map, appearance, keyboard, mouse, renderer, gameplay);
        Check(scene.Player is CarObject && scene.Player.InteractionRadius > .57f && !scene.Player.InAir,
            "Race scene needs a grounded car and full-footprint door protection.");
        var car = (CarObject)scene.Player;
        Check(scene.CaptureFrame().Character == null && !car.Camera.ThirdPerson, "Race must start in first person.");
        keyboard.Press("KeyW");
        for (var i = 0; i < 60; i++) scene.Update(1000f / 60);
        keyboard.Release("KeyW");
        Check(car.Controller.Speed > 6 && car.Transform.Position.Y < loaded.Map.PlayerStartPosition.Y - 2,
            "Car did not accelerate from the starting grid.");
        var moving = car.Transform;
        scene.Update(100);
        Check(Vector2.Distance(car.Transform.Position, moving.Position) > .5f, "Releasing W stopped momentum.");
        scene.SetPaused(true);
        var pose = car.Transform;
        var velocity = car.Controller.Velocity;
        scene.Update(10000);
        Check(car.Transform.Position == pose.Position && car.Transform.Rotation == pose.Rotation && car.Controller.Velocity == velocity && scene.DrivingState!.Value.Paused,
            "Paused car moved or lost its saved momentum.");
        scene.SetPaused(false);
        scene.Update(1000f / 60);
        Check(car.Transform.Position != pose.Position && car.Controller.Speed > 5, "Resume failed to preserve car momentum.");
        var direction = car.Transform.Rotation;
        mouse.Movement = new(100, -20);
        scene.Update(1000f / 60);
        Check(car.Transform.Rotation == direction && scene.CaptureFrame().Direction == direction && scene.CaptureFrame().Pitch == 0,
            "Cockpit mouse input changed the viewing direction or pitch.");
        car.Look.Reset();
        keyboard.Press("KeyV"); scene.Update(1000f / 60); keyboard.Release("KeyV");
        var frame = scene.CaptureFrame();
        Check(car.Camera.ThirdPerson && frame.Character is { } sprite && sprite.ViewLayer(frame.Position) == appearance.FirstLayer + 4 &&
            sprite.ViewScale(frame.Position).X > .69f && sprite.ViewScale(frame.Position).X < .71f &&
            !sprite.FacingCamera && (sprite.ViewKind(frame.Position) & (uint)SpriteKind.DirectionalPlane) != 0,
            "Chase view did not render the correctly scaled rear car frame.");
        var rearSprite = frame.Character!.Value;
        var nearbyObserver = car.Transform.Position - Vector2.Normalize(car.Transform.Rotation + new Vector2(car.Transform.Rotation.Y, -car.Transform.Rotation.X) * .1f) * 1.5f;
        var nearbySprite = appearance.CreateSprite(car, nearbyObserver);
        Check(nearbySprite.ViewScale(nearbyObserver) == rearSprite.ViewScale(frame.Position) &&
            nearbySprite.ViewKind(nearbyObserver) == rearSprite.ViewKind(frame.Position),
            "Car sprite stretched or rotated its plane within the same directional sector.");
        mouse.Movement = new(-MathF.PI / gameplay.Car.MouseSensitivity, 20);
        scene.Update(1000f / 60);
        Check(Vector2.Dot(scene.CaptureFrame().Direction, car.Transform.Rotation) < -.99f,
            "Chase orbit could not look backward over the front of the car.");
        Check(scene.DrivingState is { ThirdPerson: true, Paused: false } && scene.PlayerPose.Direction == car.Transform.Rotation,
            "Driving HUD/minimap state did not follow the actual car.");
        var previousSpeed = car.Controller.Speed;
        keyboard.Press("KeyV"); scene.Update(1000f / 60); keyboard.Release("KeyV");
        Check(scene.CaptureFrame().Character == null && car.Controller.Speed > previousSpeed - .1f,
            "Camera toggle reset driving or retained the car in first person.");
        Check(car.Look.Yaw == 0 && car.Look.Pitch == 0 && scene.CaptureFrame().Direction == car.Transform.Rotation,
            "Switching to cockpit retained third-person orbit offsets.");
        var human = await new GameSceneLoader(http, renderer, gameplay).LoadAsync("maps/glassworks.json");
        using var walking = new GameScene(human.Map, human.Character, keyboard, mouse, renderer, gameplay);
        Check(walking.Player is PlayerObject && walking.Character is CharacterAppearance && walking.DrivingState == null,
            "Exploration maps lost their walking controller/appearance.");
        walking.SetPaused(true);
        Check(!walking.Paused, "Car pause policy unexpectedly changed the walking controller.");
        Console.WriteLine($"PASS: {count} car scene assertions (configuration, loading, appearance, momentum, pause, cameras, and walking compatibility).");
        return count;
    }

    private sealed class Mouse : IMouseInput
    {
        public Vector2 Movement;
        public Vector2 ConsumeMovement() { var value = Movement; Movement = default; return value; }
    }
}
