using System.Numerics;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Physics;

namespace WolfNET.Game.Player;

/// <summary>Steers and brakes a normal car along the circuit; never moves its transform directly.</summary>
public sealed class OpponentDriver(RaceRoute route, NavigationMap map, CarSettings car, RaceSettings settings,
    Vector2 start) : IKeyboardInput, ISteeringInput, IMouseInput
{
    private float _distance = route.Project(start);
    private readonly float _origin = route.Project(start);
    private readonly OrientedBoxCollider _box = new(map, car.Length, car.Width);
    private float _stalled;
    private float _reverse;
    public KeyState KeyState { get; private set; }
    public float Steering { get; private set; }

    public void Update(CarObject actor, CarObject other, float seconds)
    {
        var body = actor.Transform;
        var speed = actor.Controller.Speed;
        _distance = route.Project(body.Position, _distance);
        var lookAhead = 2.5f + speed * .18f;
        var target = route.Point(_distance + lookAhead);
        // Stay alongside the player on the grid, then merge onto the racing line.
        var gridOffset = Math.Clamp(1 - (_distance - _origin - 15) / 20, 0, 1) * 1.5f;
        var tangent = Vector2.Normalize(route.Point(_distance + 1) - route.Point(_distance));
        target += new Vector2(-tangent.Y, tangent.X) * gridOffset;

        var relativeCar = other.Transform.Position - body.Position;
        var ahead = Vector2.Dot(relativeCar, body.Rotation);
        var side = new Vector2(-body.Rotation.Y, body.Rotation.X);
        var blocked = ahead > 0 && ahead < 7 && MathF.Abs(Vector2.Dot(relativeCar, side)) < car.Width + .35f;
        var canPass = false;
        if (blocked)
        {
            foreach (var sign in new[] { 1, -1 })
            {
                var passing = target + side * (sign * .95f);
                if (!_box.CanOccupy(passing, tangent) || map.GetSurface((int)passing.X, (int)passing.Y) != "asphalt") continue;
                target = passing; canPass = true; break;
            }
        }

        var relative = target - body.Position;
        var distance = MathF.Max(relative.Length(), .1f);
        var cross = body.Rotation.X * relative.Y - body.Rotation.Y * relative.X;
        var angle = MathF.Atan(car.Wheelbase * 2 * cross / (distance * distance));
        Steering = Math.Clamp(angle / (car.MaxSteeringDegrees * MathF.PI / 180), -1, 1);
        var incoming = Vector2.Normalize(route.Point(_distance + 3) - route.Point(_distance));
        var outgoing = Vector2.Normalize(route.Point(_distance + 13) - route.Point(_distance + 10));
        var bend = MathF.Acos(Math.Clamp(Vector2.Dot(incoming, outgoing), -1, 1));
        var targetSpeed = settings.OpponentMaxSpeed + (settings.OpponentCornerSpeed - settings.OpponentMaxSpeed) *
            Math.Clamp(bend / .45f, 0, 1);
        if (MathF.Abs(angle) > .12f) targetSpeed = MathF.Min(targetSpeed, settings.OpponentCornerSpeed);
        if (blocked && !canPass) targetSpeed = MathF.Min(targetSpeed, MathF.Max(0, (ahead - car.Length - .3f) * 2));

        _stalled = speed < .2f ? _stalled + seconds : 0;
        if (_stalled > 2) { _reverse = 1; _stalled = 0; }
        if (_reverse > 0)
        {
            _reverse -= seconds;
            Steering = -Steering;
            KeyState = new() { Backward = true };
        }
        else KeyState = new() { Forward = speed < targetSpeed, Jump = speed > targetSpeed + .4f };
    }

    public bool ConsumeJump() => false;
    public bool ConsumeViewToggle() => false;
    public bool ConsumeAction() => false;
    public Vector2 ConsumeMovement() => Vector2.Zero;
}
