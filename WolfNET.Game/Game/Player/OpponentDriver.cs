using System.Numerics;
using WolfNET.Game.InputEvents;

namespace WolfNET.Game.Player;

/// <summary>Steers and brakes a normal car along the circuit; never moves its transform directly.</summary>
public sealed class OpponentDriver(RaceRoute route, NavigationMap map, CarSettings car, RaceSettings settings,
    Vector2 start, bool skilled = false, float gridSide = 1.5f) : IKeyboardInput, ISteeringInput, IMouseInput
{
    private readonly OpponentRacingLine _line = new(route, map, car);
    private float _distance = route.Project(start);
    private readonly float _origin = route.Project(start);
    private float _drivingTime;
    private float _nextMistake = 20;
    private float _mistakeRemaining;
    public int MistakesMade { get; private set; }

    public static CarSettings CreateCarSettings(CarSettings player, bool skilled)
    {
        var maximum = player.Asphalt.MaxForwardSpeed + (skilled ? -3 : 3);
        if (maximum <= 0) throw new InvalidDataException("Player forward speed must exceed 3 blocks/s to create the green rival.");
        return player with { Asphalt = player.Asphalt with { MaxForwardSpeed = maximum } };
    }

    private float _stalled;
    private float _decisionTime;
    private float _reverse;
    private float _defensiveOffset;
    private float _passingOffset;
    private CarObject? _passingCar;
    private int _passingSide;
    private CarObject? _defendingAgainst;
    private float _defenseHold;
    public KeyState KeyState { get; private set; }
    public float Steering { get; private set; }

    public void Update(CarObject actor, CarObject other, float seconds) => Update(actor, new[] { other }, seconds);

    public void Update(CarObject actor, IReadOnlyList<CarObject> others, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) return;
        _decisionTime += seconds;
        // Perception runs at 30 Hz; the regular car controller still simulates at 120 Hz.
        if (_decisionTime + .00001f < 1f / 30) return;
        seconds = _decisionTime;
        _decisionTime = 0;
        var body = actor.Transform;
        var speed = actor.Controller.Speed;
        _distance = route.Project(body.Position, _distance);
        var lookAhead = 2.5f + speed * .18f;
        var target = _line.Point(_distance + lookAhead);
        // Stay alongside the player on the grid, then merge onto the racing line.
        var gridOffset = Math.Clamp(1 - (_distance - _origin - 15) / 20, 0, 1) * gridSide;
        var tangent = Vector2.Normalize(_line.Point(_distance + 1) - _line.Point(_distance));
        target += new Vector2(-tangent.Y, tangent.X) * gridOffset;

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
        var blocked = other != null && ahead > 0 && ahead < 22 && MathF.Abs(Vector2.Dot(relativeCar, side)) < car.Width + .35f;
        var canPass = false;
        var normal = new Vector2(-tangent.Y, tangent.X);
        var canManeuver = _line.CanManeuver(_distance);
        if (_passingCar != null)
        {
            var relativePass = Vector2.Dot(_passingCar.Transform.Position - body.Position, tangent);
            if (relativePass < -car.Length - 2 || relativePass > 22 || !canManeuver) _passingCar = null;
        }
        var wantedOffset = 0f;
        if (_passingCar != null || blocked && ahead < 12 && canManeuver)
        {
            var passed = _passingCar ?? other!;
            var preferred = _passingCar != null ? _passingSide :
                Vector2.Dot(body.Position - passed.Transform.Position, normal) >= 0 ? 1 : -1;
            foreach (var sign in new[] { preferred, -preferred })
            {
                if (_passingCar != null && sign != _passingSide) break;
                var offset = Vector2.Dot(passed.Transform.Position - target, normal) + sign * (car.Width + .4f);
                var passing = target + normal * offset;
                if (!_line.LaneClear(_distance, offset, speed * 1.1f + 8) ||
                    !_line.RoadClear(body.Position, passing, body.Rotation, tangent)) continue;
                wantedOffset = offset;
                _passingCar = passed;
                _passingSide = sign;
                canPass = true;
                break;
            }
            if (!canPass) _passingCar = null;
        }
        _passingOffset += Math.Clamp(wantedOffset - _passingOffset, -seconds * 2.5f, seconds * 2.5f);
        while (MathF.Abs(_passingOffset) > .01f && !_line.LaneClear(_distance, _passingOffset, speed * .9f + 6))
            _passingOffset = MathF.CopySign(MathF.Max(0, MathF.Abs(_passingOffset) - .1f), _passingOffset);
        target += normal * _passingOffset;

        // Passing a car ahead takes priority over defending against a car behind.
        target = Defend(actor, others, target, tangent, blocked || _passingCar != null || MathF.Abs(_passingOffset) > .01f, seconds);

        var relative = target - body.Position;
        var distance = MathF.Max(relative.Length(), .1f);
        var cross = body.Rotation.X * relative.Y - body.Rotation.Y * relative.X;
        var angle = MathF.Atan(car.Wheelbase * 2 * cross / (distance * distance));
        if (_defendingAgainst != null || MathF.Abs(_defensiveOffset) > .01f || MathF.Abs(_passingOffset) > .01f)
        {
            var laneDirection = Vector2.Normalize(_line.Point(_distance + 6) - _line.Point(_distance));
            var laneNormal = new Vector2(-laneDirection.Y, laneDirection.X);
            var laneError = Vector2.Dot(target - body.Position, laneNormal);
            var headingError = MathF.Atan2(body.Rotation.X * laneDirection.Y - body.Rotation.Y * laneDirection.X,
                Vector2.Dot(body.Rotation, laneDirection));
            var desiredHeading = MathF.Atan2(laneError * 1.5f, MathF.Max(speed, 3));
            var yaw = (headingError + desiredHeading) * 4 - actor.Controller.AngularVelocity * .8f;
            angle = MathF.Atan(car.Wheelbase * yaw / MathF.Max(speed, 3));
        }
        Steering = Math.Clamp(angle / (car.MaxSteeringDegrees * MathF.PI / 180), -1, 1);
        var targetSpeed = MathF.Min(car.Asphalt.MaxForwardSpeed, _line.Speed(_distance, skilled, settings.OpponentCornerSpeed));
        // Steering corrections and road recovery need lower speed than a settled racing line.
        var lateralError = Vector2.Distance(body.Position, _line.Point(_distance));
        if (lateralError > .8f) targetSpeed = MathF.Min(targetSpeed, 7);
        targetSpeed = MathF.Min(targetSpeed, _line.ObstacleSpeed(actor));
        if (blocked && !canPass)
        {
            var leaderSpeed = MathF.Max(0, Vector2.Dot(other!.Controller.Velocity, tangent));
            var gap = ahead - car.Length - 1 - speed * .25f;
            var followingSpeed = MathF.Sqrt(leaderSpeed * leaderSpeed +
                2 * car.Asphalt.BrakeDeceleration * .65f * MathF.Max(0, gap));
            if (ahead < car.Length + 1.5f)
                followingSpeed = leaderSpeed * Math.Clamp((ahead - car.Length - .2f) / 1.3f, 0, 1);
            targetSpeed = MathF.Min(targetSpeed, followingSpeed);
        }

        _drivingTime += seconds;
        _mistakeRemaining = MathF.Max(0, _mistakeRemaining - seconds);
        // Blue occasionally misjudges a corner entry and brakes too early, then recovers.
        // Road and barrier checks still apply; errors cost time rather than forcing a crash.
        if (!skilled && _drivingTime >= _nextMistake && speed > 7 && lateralError < .65f &&
            !canManeuver && !blocked && _passingCar == null && _defendingAgainst == null)
        {
            MistakesMade++;
            _mistakeRemaining = 1.1f;
            _nextMistake = _drivingTime + 24 + MistakesMade % 3 * 7;
        }
        if (_mistakeRemaining > 0) targetSpeed *= .65f;

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
        if (actor.Controller.SignedSpeed > 2 && _line.CanManeuver(_distance))
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
            if (!_line.LaneClear(_distance, offset, actor.Controller.Speed * .9f + 6) ||
                !DefensiveLineClear(actor, others, defensive, tangent)) continue;
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

    private bool OnRoad(Vector2 point, Vector2 direction) => _line.OnRoad(point, direction);

    public bool ConsumeJump() => false;
    public bool ConsumeViewToggle() => false;
    public bool ConsumeAction() => false;
    public Vector2 ConsumeMovement() => Vector2.Zero;
}
