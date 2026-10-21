using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player;
using WolfNET.Game.Physics;
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
    private readonly RaceSession? _opponentRace;
    private readonly OpponentDriver? _opponentDriver;
    private readonly RaceProgress? _playerProgress;
    private readonly RaceProgress? _opponentProgress;
    private double _raceClock;
    public CarObject? Opponent { get; }
    public IMap Map { get; }
    public IControlledActor Player { get; }
    public ICharacterAppearance Character { get; }
    public bool Paused { get; private set; }
    public RaceStatus? RaceState => _race == null ? null : _race.Status with
    {
        Opponent = _opponentRace is { } rival ? new(rival.Status.CompletedLaps, rival.Status.TotalMilliseconds,
            rival.Status.LapTimes, rival.Status.Phase == RacePhase.Finished) : null,
        GapMilliseconds = RaceGap()
    };
    public DrivingViewState? DrivingState => Player is CarObject car
        ? new(car.Camera.ThirdPerson, Paused, car.Controller.SteeringAngle, car.Controller.Speed, car.Look.Yaw) : null;
    public PlayerPose PlayerPose => new(Player.Transform.Position, Player.Transform.Rotation);

    private double? RaceGap()
    {
        if (_race == null || _opponentRace == null || !_race.Status.Started) return null;
        var playerFinished = _race.Status.Phase == RacePhase.Finished;
        var opponentFinished = _opponentRace.Status.Phase == RacePhase.Finished;
        if (playerFinished && opponentFinished) return _opponentRace.Status.TotalMilliseconds - _race.Status.TotalMilliseconds;
        if (playerFinished) return Math.Max(0, _raceClock - _race.Status.TotalMilliseconds);
        if (opponentFinished) return -Math.Max(0, _raceClock - _opponentRace.Status.TotalMilliseconds);
        return RaceProgress.Gap(_playerProgress!, _opponentProgress!, _raceClock);
    }

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
        if (_race != null && map.RendererMap.RaceMinimap is { } minimap)
        {
            var route = new RaceRoute(minimap);
            var box = new OrientedBoxCollider(map.NavigationMap, gameplay.Car.Length, gameplay.Car.Width);
            var position = Player.Transform.Position;
            var side = new Vector2(-direction.Y, direction.X);
            foreach (var offset in new[] { side * 1.2f, -side * 1.2f, -direction * 1.5f })
            {
                if (!box.CanOccupy(position + offset, direction)) continue;
                position += offset; break;
            }
            _opponentDriver = new(route, map.NavigationMap, gameplay.Car, gameplay.Race, position);
            Opponent = new(_opponentDriver, _opponentDriver, map.NavigationMap, gameplay.Car)
                { Transform = new Transform { Position = position, Rotation = direction } };
            _opponentRace = new(map.RendererMap.Race!, gameplay.Race);
            // Both histories share the player's start distance, including a staggered grid.
            _playerProgress = new(route, Player.Transform.Position);
            _opponentProgress = new(route, Player.Transform.Position);
        }
    }

    public void SetPaused(bool paused)
    {
        if (Player is not CarObject car || Paused == paused) return;
        Paused = paused;
        car.Controller.ResetTiming();
        Opponent?.Controller.ResetTiming();
    }

    public void Update(float deltaMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var playerFinished = _race?.Status.Phase == RacePhase.Finished;
        if (Paused && !playerFinished || playerFinished && (_opponentRace == null || _opponentRace.Status.Phase == RacePhase.Finished)) return;
        if (_race != null)
        {
            _race.BeginCountdown();
            _opponentRace?.BeginCountdown();
            var frameTime = Math.Clamp(deltaMilliseconds, 0, 250);
            _opponentRace?.AdvanceStart(frameTime);
            deltaMilliseconds = playerFinished ? frameTime : _race.AdvanceStart(frameTime);
            if (_race.Status.Phase == RacePhase.Countdown)
            {
                _input.ConsumeJump();
                if (_input.ConsumeViewToggle()) Player.Camera.ThirdPerson = !Player.Camera.ThirdPerson;
                _input.ConsumeAction();
                _mouse.ConsumeMovement();
                return;
            }
        }
        if (Opponent != null)
        {
            var remaining = deltaMilliseconds;
            while (remaining > .0001f)
            {
                var step = MathF.Min(remaining, 1000f / 120);
                UpdateRaceCars(step);
                remaining -= step;
            }
            if (!playerFinished) UpdateInteractions(deltaMilliseconds);
            return;
        }
        var previousPosition = Player.Transform.Position;
        _objects.Update(deltaMilliseconds);
        var transform = Player.Transform;
        _race?.Update(previousPosition, transform.Position, Math.Clamp(deltaMilliseconds, 0, 250));
        UpdateInteractions(deltaMilliseconds);
    }

    private void UpdateInteractions(float milliseconds)
    {
        var transform = Player.Transform;
        if (_input.ConsumeAction() && !Map.Interact(transform.Position, transform.Rotation, Player.InteractionRadius))
            _requestedMap ??= MapInteraction.FindDestination(Map, transform.Position, transform.Rotation);
        Map.UpdateDoors(milliseconds, transform.Position, Player.InteractionRadius);
    }

    private void UpdateRaceCars(float milliseconds)
    {
        var car = (CarObject)Player;
        var playerFinished = _race!.Status.Phase == RacePhase.Finished;
        var opponentFinished = _opponentRace!.Status.Phase == RacePhase.Finished;
        var beforePlayer = car.Transform.Position;
        var beforeOpponent = Opponent!.Transform.Position;
        if (!playerFinished) car.Update(milliseconds);
        if (!opponentFinished)
        {
            _opponentDriver!.Update(Opponent, car, milliseconds / 1000);
            Opponent.Update(milliseconds);
        }
        CarContact.Resolve(car, Opponent, Map.NavigationMap, playerFinished, opponentFinished);
        _raceClock += milliseconds;
        _race.Update(beforePlayer, car.Transform.Position, milliseconds);
        _opponentRace.Update(beforeOpponent, Opponent.Transform.Position, milliseconds);
        _playerProgress!.Update(car.Transform.Position, _raceClock);
        _opponentProgress!.Update(Opponent.Transform.Position, _raceClock);
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
                map, car.Look.Pitch, carView.Elevation, sprite)
                { Opponent = Opponent == null ? null : Character.CreateSprite(Opponent, carView.Position) with { SwapRedBlue = true } };
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
        Opponent?.Dispose();
    }
}
