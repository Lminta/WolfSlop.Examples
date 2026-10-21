using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player;
using WolfNET.Graphics;

namespace WolfNET.Game;

public readonly record struct PlayerPose(Vector2 Position, Vector2 Direction);
public readonly record struct DrivingViewState(bool ThirdPerson, bool Paused, float SteeringAngle, float Speed, float CameraYaw);

/// <summary>Owns simulation objects for one map. Browser and GPU lifetimes belong to the host.</summary>
public sealed class GameScene : IDisposable
{
    private readonly ObjectCollection _objects = new();
    private readonly IKeyboardInput _input;
    private readonly IMouseInput _mouse;
    private string? _requestedMap;
    private bool _disposed;
    private readonly RaceSession? _race;
    private RenderMap? _greenSource;
    private RenderMap? _greenMap;
    public IMap Map { get; }
    public IControlledActor Player { get; }
    public ICharacterAppearance Character { get; }
    public bool Paused { get; private set; }
    public RaceStatus? RaceState => _race?.Status;
    public DrivingViewState? DrivingState => Player is CarObject car
        ? new(car.Camera.ThirdPerson, Paused, car.Controller.SteeringAngle, car.Controller.Speed, car.Look.Yaw) : null;
    public PlayerPose PlayerPose => new(Player.Transform.Position, Player.Transform.Rotation);

    public GameScene(IMap map, ICharacterAppearance character, IKeyboardInput input, IMouseInput mouse,
        RendererSettings settings, GameplaySettings? gameplay = null)
    {
        gameplay ??= new();
        Map = map;
        Character = character;
        _input = input;
        _mouse = mouse;
        var start = map.PlayerStart;
        var direction = Vector2.Normalize(new(start.DirectionX, start.DirectionY));
        Player = start.Controller == "car"
            ? new CarObject(input, mouse, map.NavigationMap, gameplay.Car)
            : new PlayerObject(input, mouse, map.NavigationMap, start);
        Player.Transform = new Transform { Position = new(start.X, start.Y), Rotation = direction };
        Player.Camera.Plane = new Vector2(direction.Y, -direction.X) * settings.CameraPlaneScale;
        _objects.Add((ObjectBase)Player);
        if (Player is CarObject && map.RendererMap.Race is { } race) _race = new RaceSession(race, gameplay.Race);
    }

    public void SetPaused(bool paused)
    {
        if (Player is not CarObject car || Paused == paused) return;
        Paused = paused;
        car.Controller.ResetTiming();
    }

    public void Update(float deltaMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Paused || _race?.Status.Phase == RacePhase.Finished) return;
        if (_race != null)
        {
            _race.BeginCountdown();
            deltaMilliseconds = _race.AdvanceStart(Math.Clamp(deltaMilliseconds, 0, 250));
            if (_race.Status.Phase != RacePhase.Racing)
            {
                _input.ConsumeJump();
                _input.ConsumeViewToggle();
                _input.ConsumeAction();
                _mouse.ConsumeMovement();
                return;
            }
        }
        var previousPosition = Player.Transform.Position;
        _objects.Update(deltaMilliseconds);
        var transform = Player.Transform;
        _race?.Update(previousPosition, transform.Position, Math.Clamp(deltaMilliseconds, 0, 250));
        if (_input.ConsumeAction() && !Map.Interact(transform.Position, transform.Rotation, Player.InteractionRadius))
            _requestedMap ??= MapInteraction.FindDestination(Map, transform.Position, transform.Rotation);
        Map.UpdateDoors(deltaMilliseconds, transform.Position, Player.InteractionRadius);
    }

    public string? ConsumeMapRequest()
    {
        var requested = _requestedMap;
        _requestedMap = null;
        return requested;
    }

    public RenderFrame CaptureFrame()
    {
        var camera = Player.Camera;
        var map = Map.RendererMap;
        if (_race?.Status.Phase is RacePhase.Racing or RacePhase.Finished)
        {
            if (!ReferenceEquals(_greenSource, map))
            {
                _greenSource = map;
                _greenMap = map with { Sprites = Array.AsReadOnly(map.Sprites.Select(sprite => sprite.RaceStartLight
                    ? sprite with { TextureLayer = sprite.TextureLayer + 1, Atlas = null } : sprite).ToArray()) };
            }
            map = _greenMap!;
        }
        if (Player is CarObject car)
        {
            var carView = car.Look.View(car.Transform, camera.ThirdPerson, map, car.Settings);
            Sprite? sprite = camera.ThirdPerson && carView.Distance > .35f ? Character.CreateSprite(car, carView.Position) : null;
            return new(carView.Position, car.Look.Direction(car.Transform), car.Look.Plane(car.Transform, camera.Plane.Length()),
                map, car.Look.Pitch, carView.Elevation, sprite);
        }
        var view = camera.View(map);
        Sprite? character = camera.ThirdPerson && view.Distance > .35f
            ? Character.CreateSprite(Player, view.Position)
            : null;
        return new(view.Position, camera.Transform.Rotation, camera.Plane, map, camera.Pitch, view.Elevation, character);
    }

    public void Dispose()
    {
        _disposed = true;
        _objects.Dispose();
    }
}
