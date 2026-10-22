using System.Numerics;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Physics;

namespace WolfNET.Game.Player;

/// <summary>Steers and brakes a normal car along the circuit; never moves its transform directly.</summary>
public sealed class OpponentDriver(RaceRoute route, NavigationMap map, CarSettings car, RaceSettings settings,
    Vector2 start, bool skilled = false, float gridSide = 1.5f) : IKeyboardInput, ISteeringInput, IMouseInput
{
    private float _distance = route.Project(start);
    private readonly float _origin = route.Project(start);
    private readonly OrientedBoxCollider _box = new(map, car.Length, car.Width);
    private float _stalled;
    private float _reverse;
    private float _defensiveOffset;
    private CarObject? _defendingAgainst;
    private float _defenseHold;
    public KeyState KeyState { get; private set; }
    public float Steering { get; private set; }

    public void Update(CarObject actor, CarObject other, float seconds) => Update(actor, new[] { other }, seconds);

    public void Update(CarObject actor, IReadOnlyList<CarObject> others, float seconds)
    {
        var body = actor.Transform;
        var speed = actor.Controller.Speed;
        _distance = route.Project(body.Position, _distance);
        var lookAhead = skilled ? 2.5f + speed * .12f : 2.5f + speed * .18f;
        var target = route.Point(_distance + lookAhead);
        // Stay alongside the player on the grid, then merge onto the racing line.
        var gridOffset = Math.Clamp(1 - (_distance - _origin - 15) / 20, 0, 1) * gridSide;
        var tangent = Vector2.Normalize(route.Point(_distance + 1) - route.Point(_distance));
        target += new Vector2(-tangent.Y, tangent.X) * gridOffset;

        if (skilled)
        {
            // Cut toward the apex only where the full car remains on the road.
            var entry = Vector2.Normalize(route.Point(_distance + lookAhead) - route.Point(_distance));
            var exit = Vector2.Normalize(route.Point(_distance + lookAhead + 8) - route.Point(_distance + lookAhead));
            var turn = entry.X * exit.Y - entry.Y * exit.X;
            var apex = target + new Vector2(-tangent.Y, tangent.X) * Math.Clamp(turn * 2, -.8f, .8f);
            if (OnRoad(apex, tangent)) target = apex;
        }

        CarObject? other = null;
        var closest = float.PositiveInfinity;
        foreach (var candidate in others)
        {
            if (ReferenceEquals(candidate, actor)) continue;
            var relativeCandidate = candidate.Transform.Position - body.Position;
            var forwardDistance = Vector2.Dot(relativeCandidate, body.Rotation);
            if (forwardDistance <= 0 || forwardDistance >= closest ||
                MathF.Abs(relativeCandidate.X * body.Rotation.Y - relativeCandidate.Y * body.Rotation.X) >= car.Width + .35f) continue;
            closest = forwardDistance;
            other = candidate;
        }
        var relativeCar = other == null ? new Vector2(10000) : other.Transform.Position - body.Position;
        var ahead = Vector2.Dot(relativeCar, body.Rotation);
        var side = new Vector2(-body.Rotation.Y, body.Rotation.X);
        var blocked = ahead > 0 && ahead < 7 && MathF.Abs(Vector2.Dot(relativeCar, side)) < car.Width + .35f;
        var canPass = false;
        if (blocked)
        {
            var preferredSide = Vector2.Dot(body.Position - other!.Transform.Position, side) >= 0 ? 1 : -1;
            foreach (var sign in new[] { preferredSide, -preferredSide })
            {
                var clearance = Vector2.Dot(other!.Transform.Position - target, side) + sign * (car.Width + .4f);
                var passing = target + side * clearance;
                if (!_box.CanOccupy(passing, tangent) || map.GetSurface((int)passing.X, (int)passing.Y) != "asphalt") continue;
                target = passing; canPass = true; break;
            }
        }

        // Passing a car ahead takes priority over defending against a car behind.
        target = Defend(actor, others, target, tangent, blocked, seconds);

        var relative = target - body.Position;
        var distance = MathF.Max(relative.Length(), .1f);
        var cross = body.Rotation.X * relative.Y - body.Rotation.Y * relative.X;
        var angle = MathF.Atan(car.Wheelbase * 2 * cross / (distance * distance));
        if (_defendingAgainst != null || MathF.Abs(_defensiveOffset) > .01f)
        {
            var laneDirection = Vector2.Normalize(route.Point(_distance + 6) - route.Point(_distance));
            var laneNormal = new Vector2(-laneDirection.Y, laneDirection.X);
            var laneError = Vector2.Dot(target - body.Position, laneNormal);
            var headingError = MathF.Atan2(body.Rotation.X * laneDirection.Y - body.Rotation.Y * laneDirection.X,
                Vector2.Dot(body.Rotation, laneDirection));
            var desiredHeading = MathF.Atan2(laneError * 1.5f, MathF.Max(speed, 3));
            var yaw = (headingError + desiredHeading) * 4 - actor.Controller.AngularVelocity * .8f;
            angle = MathF.Atan(car.Wheelbase * yaw / MathF.Max(speed, 3));
        }
        Steering = Math.Clamp(angle / (car.MaxSteeringDegrees * MathF.PI / 180), -1, 1);
        var incoming = Vector2.Normalize(route.Point(_distance + 3) - route.Point(_distance));
        var outgoing = Vector2.Normalize(route.Point(_distance + 13) - route.Point(_distance + 10));
        var bend = MathF.Acos(Math.Clamp(Vector2.Dot(incoming, outgoing), -1, 1));
        var targetSpeed = settings.OpponentMaxSpeed + (settings.OpponentCornerSpeed - settings.OpponentMaxSpeed) *
            Math.Clamp(bend / .45f, 0, 1);
        if (MathF.Abs(angle) > .12f) targetSpeed = MathF.Min(targetSpeed, settings.OpponentCornerSpeed);
        if (skilled)
        {
            targetSpeed = settings.OpponentMaxSpeed;
            // Preview the bend and brake before it, using the same tire limits as the player.
            for (float preview = 0; preview <= 28; preview += 4)
            {
                var a = route.Point(_distance + preview);
                var b = route.Point(_distance + preview + 6);
                var c = route.Point(_distance + preview + 12);
                var ab = b - a;
                var bc = c - b;
                var curvature = 2 * MathF.Abs(ab.X * bc.Y - ab.Y * bc.X) /
                    MathF.Max(.01f, ab.Length() * bc.Length() * Vector2.Distance(a, c));
                var cornerSpeed = MathF.Sqrt(car.Asphalt.MaxLateralAcceleration * .95f / MathF.Max(curvature, .001f));
                var safeSpeed = MathF.Sqrt(cornerSpeed * cornerSpeed + 2 * car.Asphalt.BrakeDeceleration * MathF.Max(0, preview - 3));
                targetSpeed = MathF.Min(targetSpeed, safeSpeed);
            }
        }
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

    private Vector2 Defend(CarObject actor, IReadOnlyList<CarObject> others, Vector2 target,
        Vector2 tangent, bool blocked, float seconds)
    {
        if (blocked) { _defensiveOffset = 0; _defendingAgainst = null; return target; }
        var body = actor.Transform;
        var normal = new Vector2(-tangent.Y, tangent.X);
        var desired = 0f;
        var soonest = float.PositiveInfinity;
        CarObject? threat = null;
        _defenseHold = MathF.Max(0, _defenseHold - seconds);
        if (actor.Controller.SignedSpeed > 2)
        {
            foreach (var challenger in others)
            {
                if (ReferenceEquals(challenger, actor) || Vector2.Dot(challenger.Transform.Rotation, tangent) < .7f) continue;
                var relative = challenger.Transform.Position - body.Position;
                var behind = -Vector2.Dot(relative, tangent);
                var lateral = Vector2.Dot(relative, normal);
                var closing = Vector2.Dot(challenger.Controller.Velocity - actor.Controller.Velocity, tangent);
                var holding = ReferenceEquals(challenger, _defendingAgainst) && _defenseHold > 0;
                if (behind < -car.Length || behind > 18 || MathF.Abs(lateral) > 3.5f) continue;
                // Hold the lane once the block works, instead of abandoning it as closing speed falls.
                if (!holding && (behind < car.Length + .3f || closing < .2f)) continue;
                var arrival = behind / MathF.Max(.5f, closing);
                if (!holding && arrival > 7) continue;
                var priority = holding ? arrival * .5f : arrival;
                if (priority >= soonest) continue;
                soonest = priority;
                threat = challenger;
                if (behind < car.Length + .3f)
                {
                    // Once alongside, keep our lane rather than chasing the other car sideways.
                    desired = _defensiveOffset;
                }
                else
                {
                    var lateralMotion = Math.Clamp(Vector2.Dot(challenger.Controller.Velocity, normal) * .4f, -.5f, .5f);
                    desired = Math.Clamp(Vector2.Dot(challenger.Transform.Position - target, normal) + lateralMotion, -2.2f, 2.2f);
                    if (closing >= .2f) _defenseHold = 1.5f;
                }
            }
        }
        _defendingAgainst = threat;
        // Respond early, with a controlled lane change that remains stable at racing speed.
        var rate = threat == null ? 1f : 2.5f;
        var offset = _defensiveOffset + Math.Clamp(desired - _defensiveOffset, -seconds * rate, seconds * rate);
        if (MathF.Abs(offset) < .001f) { _defensiveOffset = 0; return target; }
        for (var attempt = 0; attempt < 24 && MathF.Abs(offset) > .001f; attempt++, offset = MathF.CopySign(MathF.Max(0, MathF.Abs(offset) - .1f), offset))
        {
            var defensive = target + normal * offset;
            if (!DefensiveLineClear(actor, others, defensive, tangent)) continue;
            _defensiveOffset = offset;
            return defensive;
        }
        _defensiveOffset = 0;
        return target;
    }

    private bool DefensiveLineClear(CarObject actor, IReadOnlyList<CarObject> others, Vector2 target, Vector2 tangent)
    {
        var body = actor.Transform;
        for (var step = 1; step <= 4; step++)
        {
            var point = Vector2.Lerp(body.Position, target, step / 4f);
            if (!OnRoad(point, tangent)) return false;
            foreach (var other in others)
            {
                if (ReferenceEquals(other, actor)) continue;
                // Keep the lane-change corridor clear of cars next to or ahead of us.
                var relative = other.Transform.Position - body.Position;
                if (Vector2.Dot(relative, tangent) < -car.Length - .3f) continue;
                if (Vector2.DistanceSquared(point, other.Transform.Position) <
                    (car.Length + .25f) * (car.Length + .25f)) return false;
            }
        }
        return true;
    }

    private bool OnRoad(Vector2 point, Vector2 direction)
    {
        if (!_box.CanOccupy(point, direction)) return false;
        var side = new Vector2(-direction.Y, direction.X) * (car.Width * .5f + .15f);
        return new[] { point, point + side, point - side }.All(p => map.GetSurface((int)p.X, (int)p.Y) == "asphalt");
    }

    public bool ConsumeJump() => false;
    public bool ConsumeViewToggle() => false;
    public bool ConsumeAction() => false;
    public Vector2 ConsumeMovement() => Vector2.Zero;
}
