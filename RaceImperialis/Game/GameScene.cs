using System.Numerics;
using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Actors;
using WolfNET.Engine.Physics;
using WolfNET.Engine.Graphics;
using WolfNET.Engine.Audio;

namespace RaceImperialis.Game;

public readonly record struct PlayerPose(Vector2 Position, Vector2 Direction);
public readonly record struct DrivingViewState(bool ThirdPerson, bool Paused, float SteeringAngle, float Speed, float CameraYaw);

/// <summary>Owns simulation objects for one map. Browser and GPU lifetimes belong to the host.</summary>
public sealed class GameScene : IScene
{
    private readonly ObjectCollection _objects = new();
    private readonly IKeyboardInput _input;
    private readonly IMouseInput _mouse;
    private string? _requestedMap;
    private bool _disposed;
    private readonly float _aspectRatio;
    private readonly RaceSession? _race;
    private RenderMap? _greenSource;
    private RenderMap? _greenMap;
    private sealed record Rival(CarObject Car, OpponentDriver Driver, RaceSession Race, string Name);
    private readonly List<Rival> _rivals = [];
    private readonly RaceRoute? _route;
    private CarObject[] _raceCars = [];
    private CarSfxState[] _audioCars = [];
    private readonly List<SfxImpact> _audioImpacts = [];
    private Vector2[] _beforeOpponents = [];
    private double _raceClock;
    private RaceStatus? _cachedRaceState;
    private RaceStatus[] _cachedStatuses = [];
    private double _cachedClock = -1;
    internal OpponentPace GetOpponentPace(int index) => _rivals[index].Driver.Pace;
    internal int GetOpponentMistakes(int index) => _rivals[index].Driver.MistakesMade;
    public CarObject? Opponent => _rivals.Count > 0 ? _rivals[0].Car : null;
    public CarObject? SecondOpponent => _rivals.Count > 1 ? _rivals[1].Car : null;
    public IMap Map { get; }
    public IControlledActor Player { get; }
    public ICharacterAppearance Character { get; }
    public bool Paused { get; private set; }
    public RaceDifficulty Difficulty { get; }
    // Phase checks need only the player's session, not projected opponent standings.
    public RaceStatus? PlayerRaceState => _race?.Status;
    public RaceStatus? RaceState
    {
        get
        {
            if (_race == null) return null;
            var unchanged = _cachedStatuses.Length == _rivals.Count + 1 &&
                _cachedClock == _raceClock && _cachedStatuses[0] == _race.Status;
            for (var i = 0; unchanged && i < _rivals.Count; i++)
                unchanged = _cachedStatuses[i + 1] == _rivals[i].Race.Status;
            if (unchanged) return _cachedRaceState;
            if (_cachedStatuses.Length != _rivals.Count + 1) _cachedStatuses = new RaceStatus[_rivals.Count + 1];
            _cachedStatuses[0] = _race.Status;
            for (var i = 0; i < _rivals.Count; i++) _cachedStatuses[i + 1] = _rivals[i].Race.Status;
            _cachedClock = _raceClock;
            var racers = new[] { (Name: "You", Player: true, Race: _race, Car: (CarObject)Player) }
                .Concat(_rivals.Select(r => (Name: r.Name, Player: false, Race: r.Race, Car: r.Car)))
                .OrderByDescending(r => r.Race.Status.Phase == RacePhase.Finished)
                .ThenBy(r => r.Race.Status.Phase == RacePhase.Finished ? r.Race.Status.TotalMilliseconds : 0)
                .ThenByDescending(r => _route == null ? 0 : r.Race.Progress(_route, r.Car.Transform.Position))
                .ToArray();
            static OpponentRaceStatus Summary(RaceSession race) => new(race.Status.CompletedLaps,
                race.Status.TotalMilliseconds, race.Status.LapTimes, race.Status.Phase == RacePhase.Finished);
            var opponents = _rivals.Select(r => Summary(r.Race)).ToArray();
            // When leading, measure to the first opponent across the matching lap line.
            // Their order on the road can change after that crossing without changing the gap.
            var gapReference = racers[0].Player
                ? _rivals.OrderByDescending(r => r.Race.Status.CompletedLaps)
                    .ThenBy(r => r.Race.Status.LapTimes.Sum()).FirstOrDefault()?.Race
                : racers[0].Race;
            return _cachedRaceState = _race.Status with
            {
                Opponent = opponents.Length == 0 ? null : opponents[0],
                Opponents = opponents,
                Standings = racers.Select((r, i) => new RaceStanding(r.Name, i + 1, r.Player, Summary(r.Race))).ToArray(),
                GapMilliseconds = gapReference == null ? null :
                    RaceLapGap.Calculate(gapReference.Status, _race.Status, _raceClock)
            };
        }
    }
    public DrivingViewState? DrivingState => Player is CarObject car
        ? new(car.Camera.ThirdPerson, Paused, car.Controller.SteeringAngle, car.Controller.Speed, car.Look.Yaw) : null;
    public PlayerPose PlayerPose => new(Player.Transform.Position, Player.Transform.Rotation);

    public GameScene(IMap map, ICharacterAppearance character, IKeyboardInput input, IMouseInput mouse,
        RendererSettings settings, GameplaySettings? gameplay = null, RaceDifficulty? difficulty = null)
    {
        _aspectRatio = (float)settings.Width / settings.Height;
        gameplay ??= new();
        Difficulty = difficulty ?? gameplay.Opponents.DefaultDifficulty;
        var profiles = gameplay.Opponents.Get(Difficulty);
        Map = map;
        Character = character;
        _input = input;
        _mouse = mouse;
        var start = map.Spawn;
        var direction = Vector2.Normalize(new(start.DirectionX, start.DirectionY));
        Player = start.Controller == "car"
            ? new CarObject(input, mouse, map.NavigationMap, gameplay.Car, gameplay.CameraShake)
            : new PlayerObject(input, mouse, map.NavigationMap, start);
        Player.Transform = new Transform { Position = new(start.X, start.Y), Rotation = direction };
        Player.Camera.Plane = new Vector2(direction.Y, -direction.X) * settings.CameraPlaneScale;
        _objects.Add((ObjectBase)Player);
        if (Player is CarObject && map.RendererMap.GetRace() is { } race) _race = new RaceSession(race, gameplay.Race);
        if (_race != null && map.RendererMap.GetRaceMinimap() is { } minimap)
        {
            _route = new RaceRoute(minimap);
            var box = new OrientedBoxCollider(map.NavigationMap, gameplay.Car.Length, gameplay.Car.Width);
            var origin = Player.Transform.Position;
            var side = new Vector2(-direction.Y, direction.X);
            for (var i = 0; i < 2; i++)
            {
                var offsets = i == 0
                    ? new[] { side * 1.2f, -side * 1.2f, -direction * 1.5f }
                    : new[] { -side * 1.2f, -direction * 1.5f, -direction * 3f, side * 2.4f };
                Vector2? position = null;
                foreach (var offset in offsets)
                {
                    var candidate = origin + offset;
                    if (!box.CanOccupy(candidate, direction) ||
                        _rivals.Any(r => Vector2.Distance(r.Car.Transform.Position, candidate) < gameplay.Car.Length + .2f)) continue;
                    position = candidate;
                    break;
                }
                if (position == null) throw new InvalidDataException("Race grid needs room for two opponents.");
                var profile = profiles.Get(i == 1);
                var rivalSettings = OpponentDriver.CreateCarSettings(gameplay.Car, profile);
                var driver = new OpponentDriver(_route, map.NavigationMap, rivalSettings, gameplay.Race,
                    position.Value, skilled: i == 1, gridSide: i == 0 ? 1.5f : -1.5f,
                    racingLineSource: _rivals.Count == 0 ? null : _rivals[0].Driver, profile: profile);
                var rival = new CarObject(driver, driver, map.NavigationMap, rivalSettings, gameplay.CameraShake)
                    { Transform = new Transform { Position = position.Value, Rotation = direction } };
                _rivals.Add(new(rival, driver, new(map.RendererMap.GetRace()!, gameplay.Race), i == 0 ? "Blue car" : "Green car"));
            }
            _beforeOpponents = new Vector2[_rivals.Count];
            _raceCars = new[] { (CarObject)Player }.Concat(_rivals.Select(r => r.Car)).ToArray();
        }
        if (_raceCars.Length == 0 && Player is CarObject solo) _raceCars = [solo];
        _audioCars = new CarSfxState[_raceCars.Length];
        for (var i = 0; i < _raceCars.Length; i++)
        {
            var id = i;
            _raceCars[i].Controller.WorldImpact += impact =>
            {
                if (_audioImpacts.Count < 64) _audioImpacts.Add(new(id, impact.Position,
                    Map.GetImpactMaterial(impact.CellX, impact.CellY), impact.Speed));
            };
        }
    }

    public SfxFrame CaptureAudioFrame(ExampleRenderFrame frame, bool active, float seconds)
    {
        for (var i = 0; i < _raceCars.Length; i++)
        {
            var car = _raceCars[i];
            var controller = car.Controller;
            _audioCars[i] = new(i, car.Transform.Position, controller.Speed, car.Settings.Asphalt.MaxForwardSpeed,
                controller.Throttle, controller.Acceleration, controller.Braking, controller.OnMud,
                car.Settings.OffRoad.MaxForwardSpeed);
        }
        return new(frame.Position, frame.Direction, _audioCars, _audioImpacts,
            _race?.Status.Phase, _race?.Status.CountdownNumber, active, seconds);
    }

    public void SetPaused(bool paused)
    {
        if (Player is not CarObject car || Paused == paused) return;
        Paused = paused;
        car.Controller.ResetTiming();
        foreach (var rival in _rivals) rival.Car.Controller.ResetTiming();
    }

    public void Update(float deltaMilliseconds)
    {
        _audioImpacts.Clear();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var playerFinished = _race?.Status.Phase == RacePhase.Finished;
        if (Paused && !playerFinished || playerFinished && _rivals.All(r => r.Race.Status.Phase == RacePhase.Finished)) return;
        if (_race != null)
        {
            _race.BeginCountdown();
            foreach (var rival in _rivals) rival.Race.BeginCountdown();
            var frameTime = Math.Clamp(deltaMilliseconds, 0, 250);
            foreach (var rival in _rivals) rival.Race.AdvanceStart(frameTime);
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
        if (_input.ConsumeAction())
        {
            if (Player is PlayerObject human) human.Animation.PlayAction();
            if (!Map.Interact(transform.Position, transform.Rotation, Player.InteractionRadius))
                _requestedMap ??= MapInteraction.FindTarget(Map, transform.Position, transform.Rotation);
        }
        Map.UpdateDoors(milliseconds, transform.Position, Player.InteractionRadius);
    }

    private void UpdateRaceCars(float milliseconds)
    {
        var car = (CarObject)Player;
        var playerFinished = _race!.Status.Phase == RacePhase.Finished;
        var beforePlayer = car.Transform.Position;
        for (var i = 0; i < _rivals.Count; i++) _beforeOpponents[i] = _rivals[i].Car.Transform.Position;
        if (!playerFinished) car.Update(milliseconds);
        foreach (var rival in _rivals)
        {
            if (rival.Race.Status.Phase == RacePhase.Finished) continue;
            rival.Driver.Update(rival.Car, _raceCars, milliseconds / 1000, playerFinished ? null : car);
            rival.Car.Update(milliseconds);
        }
        for (var i = 0; i < _raceCars.Length; i++)
            for (var j = i + 1; j < _raceCars.Length; j++)
            {
                var collision = CarContact.Resolve(_raceCars[i], _raceCars[j], Map.NavigationMap, milliseconds / 1000,
                    i == 0 ? playerFinished : _rivals[i - 1].Race.Status.Phase == RacePhase.Finished,
                    _rivals[j - 1].Race.Status.Phase == RacePhase.Finished);
                if (collision is { } hit)
                {
                    if (i == 0 && !playerFinished) car.Look.TriggerImpact(hit.Speed);
                    if (_audioImpacts.Count < 64)
                        _audioImpacts.Add(new(100 + i * 16 + j, hit.Position, "car", hit.Speed));
                }
            }
        _raceClock += milliseconds;
        _race.Update(beforePlayer, car.Transform.Position, milliseconds);
        if (_race.Status.Phase == RacePhase.Finished && !playerFinished)
            car.Controller.ResolveCarContact(car.Transform.Position, Vector2.Zero);
        for (var i = 0; i < _rivals.Count; i++)
        {
            var rival = _rivals[i];
            rival.Race.Update(_beforeOpponents[i], rival.Car.Transform.Position, milliseconds);
            if (rival.Race.Status.Phase == RacePhase.Finished && rival.Car.Controller.Speed > 0)
                rival.Car.Controller.ResolveCarContact(rival.Car.Transform.Position, Vector2.Zero);
        }
    }

    public string? ConsumeMapRequest()
    {
        var requested = _requestedMap;
        _requestedMap = null;
        return requested;
    }

    public ExampleRenderFrame CaptureFrame()
    {
        var camera = Player.Camera;
        var map = Map.RendererMap;
        if (_race?.Status.Phase is RacePhase.Racing or RacePhase.Finished)
        {
            if (!ReferenceEquals(_greenSource, map))
            {
                _greenSource = map;
                _greenMap = map with { Sprites = Array.AsReadOnly(map.Sprites.Select(sprite => sprite.Tag == "race-start-light"
                    ? sprite with { TextureLayer = sprite.TextureLayer + 1, Atlas = null } : sprite).ToArray()) };
            }
            map = _greenMap!;
        }
        if (Player is CarObject car)
        {
            var carView = car.Look.View(car.Transform, camera.ThirdPerson, map, car.Settings);
            var shake = _race?.Status.Phase != RacePhase.Finished
                ? car.Look.Shake : Vector2.Zero;
            var direction = car.Look.Direction(car.Transform);
            var cosine = MathF.Cos(shake.X);
            var sine = MathF.Sin(shake.X);
            direction = new(direction.X * cosine - direction.Y * sine, direction.X * sine + direction.Y * cosine);
            var plane = new Vector2(direction.Y, -direction.X) * camera.Plane.Length();
            Sprite? sprite = camera.ThirdPerson && carView.Distance > .35f ? Character.CreateSprite(car, carView.Position) : null;
            return new(carView.Position, direction, plane,
                map, car.Look.Pitch + shake.Y, carView.Elevation, sprite)
                {
                    AspectRatio = _aspectRatio,
                    Opponent = Opponent == null ? null : Character.CreateSprite(Opponent, carView.Position) with { SwapRedBlue = true },
                    SecondOpponent = SecondOpponent == null ? null : Character.CreateSprite(SecondOpponent, carView.Position) with { SwapRedGreen = true },
                    FirstPerson = camera.ThirdPerson ? default : new(FirstPersonViewKind.Cockpit,
                        WheelRotation: -Math.Clamp(car.Controller.SteeringAngle / (car.Settings.MaxSteeringDegrees * MathF.PI / 180), -1, 1)
                            * car.Settings.SteeringWheelDegrees * MathF.PI / 180)
                };
        }
        var view = camera.View(map);
        Sprite? character = camera.ThirdPerson && view.Distance > .35f
            ? Character.CreateSprite(Player, view.Position)
            : null;
        var human = (PlayerObject)Player;
        var animation = human.Animation;
        return new(view.Position, camera.Transform.Rotation, camera.Plane, map, camera.Pitch, view.Elevation, character)
        {
            AspectRatio = _aspectRatio,
            FirstPerson = camera.ThirdPerson ? default : new(FirstPersonViewKind.Hands,
                new(animation.HandSwing, animation.HandBob, animation.HandStrafe, animation.HandReach), human.InAir, human.Crouching)
        };
    }

    RenderFrame IScene.CaptureFrame() => CaptureFrame();

    public void Dispose()
    {
        _disposed = true;
        _objects.Dispose();
        foreach (var rival in _rivals) rival.Car.Dispose();
    }
}
