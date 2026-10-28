using System.Numerics;
using WolfNET.Engine.Input;

namespace RaceImperialis.Game.Player;

internal readonly record struct OpponentPace(float Corner, float Recovery, float Obstacle, float Traffic, float Target);

/// <summary>Steers and brakes a normal car along the circuit; never moves its transform directly.</summary>
public sealed class OpponentDriver(RaceRoute route, NavigationMap map, CarSettings car, RaceSettings settings,
    Vector2 start, bool skilled = false, float gridSide = 1.5f, OpponentDriver? racingLineSource = null,
    OpponentProfileSettings? profile = null) : IKeyboardInput, ISteeringInput, IMouseInput
{
    private readonly OpponentProfileSettings _profile = profile ?? OpponentProfileSettings.Normal(skilled);
    private readonly OpponentRacingLine _line = new(route, map, car, racingLineSource?._line);
    private float _distance = route.Project(start);
    private readonly float _origin = route.Project(start);
    private float _drivingTime;
    private float _nextMistake = (profile ?? OpponentProfileSettings.Normal(skilled)).Mistakes.FirstAfterSeconds;
    private float _mistakeRemaining;
    public int MistakesMade { get; private set; }

    public static CarSettings CreateCarSettings(CarSettings player, bool skilled)
        => CreateCarSettings(player, OpponentProfileSettings.Normal(skilled));

    public static CarSettings CreateCarSettings(CarSettings player, OpponentProfileSettings profile)
    {
        profile.Validate(player);
        var maximum = player.Asphalt.MaxForwardSpeed + profile.MaxSpeedOffset;
        return player with { Asphalt = player.Asphalt with
        {
            MaxForwardSpeed = maximum,
            Acceleration = player.Asphalt.Acceleration * profile.AccelerationMultiplier
        } };
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
    private CarObject? _player;
    private bool _allowRivalDefense = true;
    private float _rivalBattleTime;
    private float _rivalCooldown;
    private float _passTime;
    private float? _startingLane;
    public KeyState KeyState { get; private set; }
    public float Steering { get; private set; }
    internal OpponentPace Pace { get; private set; }

    public void Update(CarObject actor, CarObject other, float seconds) => Update(actor, new[] { other }, seconds, other);

    public void Update(CarObject actor, IReadOnlyList<CarObject> others, float seconds, CarObject? player = null)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) return;
        _decisionTime += seconds;
        // Perception runs at 30 Hz; the regular car controller still simulates at 120 Hz.
        if (_decisionTime + .00001f < 1f / 30) return;
        seconds = _decisionTime;
        _decisionTime = 0;
        _player = player;
        var body = actor.Transform;
        var speed = actor.Controller.Speed;
        _distance = route.Project(body.Position, _distance);
        var lookAhead = 2.5f + speed * .18f;
        var target = _line.Point(_distance + lookAhead);
        var tangent = Vector2.Normalize(_line.Point(_distance + 1) - _line.Point(_distance));
        var normal = new Vector2(-tangent.Y, tangent.X);
        // Launch in our own grid lane, then merge promptly once the cars have room.
        _startingLane ??= gridSide == 0 ? 0 : Math.Clamp(Vector2.Dot(start - _line.Point(_origin), normal),
            -MathF.Abs(gridSide), MathF.Abs(gridSide));
        var gridOffset = Math.Clamp(1 - (_distance - _origin - 5) / 12, 0, 1) * _startingLane.Value;
        target += normal * gridOffset;

        var playerLead = player == null ? 0 : route.Project(player.Transform.Position, _distance) - _distance;
        var playerThreat = player != null && playerLead <= 0 && playerLead > -24 &&
            Vector2.Dot(player.Controller.Velocity - actor.Controller.Velocity, tangent) > -.5f;
        var chasingPlayer = player != null && playerLead > car.Length + 2;
        var rivalBattle = _defendingAgainst != null && !ReferenceEquals(_defendingAgainst, player) ||
            _passingCar != null && !ReferenceEquals(_passingCar, player) && _passingCar.Controller.Speed > 2;
        _rivalCooldown = MathF.Max(0, _rivalCooldown - seconds);
        _rivalBattleTime = rivalBattle ? _rivalBattleTime + seconds : MathF.Max(0, _rivalBattleTime - seconds);
        if (_rivalBattleTime > 3)
        {
            _rivalCooldown = 6;
            _rivalBattleTime = 0;
        }
        _allowRivalDefense = _rivalCooldown == 0 &&
            (player == null || _drivingTime > 8 && !chasingPlayer && !playerThreat);

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
        var canManeuver = CanManeuver(speed);
        if (_passingCar != null)
        {
            _passTime += seconds;
            var relativePass = Vector2.Dot(_passingCar.Transform.Position - body.Position, tangent);
            var rivalPass = !ReferenceEquals(_passingCar, player) && _passingCar.Controller.Speed > 2;
            var stillBehind = relativePass > car.Length + 1;
            if (relativePass < -car.Length - 2 || relativePass > 22 || !canManeuver ||
                rivalPass && stillBehind && (playerThreat || _passTime > 5 || _rivalCooldown > 0))
            {
                if (_passTime > 5) _rivalCooldown = 4;
                _passingCar = null;
                _passTime = 0;
            }
        }
        var wantedOffset = 0f;
        var allowRivalPass = _rivalCooldown == 0 && !playerThreat && (player == null || _drivingTime > 4);
        var shouldPass = blocked && ahead < 12 && canManeuver &&
            (ReferenceEquals(other, player) || other!.Controller.Speed < 1 || allowRivalPass);
        if (_passingCar != null || shouldPass)
        {
            var passed = _passingCar ?? other!;
            var preferred = _passingCar != null ? _passingSide :
                Vector2.Dot(body.Position - passed.Transform.Position, normal) >= 0 ? 1 : -1;
            foreach (var sign in new[] { preferred, -preferred })
            {
                if (_passingCar != null && sign != _passingSide) break;
                var offset = Vector2.Dot(passed.Transform.Position - target, normal) + sign * (car.Width + .4f);
                var passing = target + normal * offset;
                if (!PassingLaneAvailable(actor, passed, others, passing, tangent) ||
                    !_line.LaneClear(_distance, offset, speed * 1.1f + 8) ||
                    !_line.RoadClear(body.Position, passing, body.Rotation, tangent)) continue;
                wantedOffset = offset;
                if (!ReferenceEquals(_passingCar, passed)) _passTime = 0;
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
        var avoidingTraffic = blocked && ahead < car.Length + speed * .35f ||
            _passingCar != null || MathF.Abs(_passingOffset) > .01f;
        target = Defend(actor, others, target, tangent, avoidingTraffic, seconds);

        var relative = target - body.Position;
        var distance = MathF.Max(relative.Length(), .1f);
        var cross = body.Rotation.X * relative.Y - body.Rotation.Y * relative.X;
        var angle = MathF.Atan(car.Wheelbase * 2 * cross / (distance * distance));
        // Anticipate the bend through the car's yaw response, then correct heading and lane error.
        if (speed > 4 && Vector2.Distance(body.Position, _line.Point(_distance)) < 1.5f)
        {
            var direction = _line.Direction(_distance);
            var laneNormal = new Vector2(-direction.Y, direction.X);
            var laneError = Vector2.Dot(_line.Point(_distance) - body.Position, laneNormal) + gridOffset;
            var headingError = MathF.Atan2(body.Rotation.X * direction.Y - body.Rotation.Y * direction.X,
                Vector2.Dot(body.Rotation, direction));
            var curvature = _line.Curvature(_distance + speed * .9f / car.AngularResponse);
            angle = MathF.Atan(car.Wheelbase * curvature * _profile.SteeringCurvatureGain) + headingError * .65f +
                MathF.Atan2(laneError * 1.5f, speed + 2) - MathF.Atan2(car.Wheelbase * actor.Controller.AngularVelocity * .3f, speed);
        }
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
        var cornerLimit = MathF.Min(car.Asphalt.MaxForwardSpeed, _line.Speed(_distance, _profile.CornerGripFraction, settings.OpponentCornerSpeed));
        var targetSpeed = cornerLimit;
        // Allow ordinary tracking error on a fast bend; slow down for a large
        // deviation or an actual verge contact, rather than repeatedly braking to 7.
        var intendedLane = Vector2.Dot(target - _line.Point(_distance + lookAhead), normal);
        var lateralError = MathF.Abs(Vector2.Dot(body.Position - _line.Point(_distance), normal) - intendedLane);
        var changingLane = MathF.Abs(_passingOffset) > .01f || MathF.Abs(_defensiveOffset) > .01f;
        var recoveryLimit = lateralError > (changingLane ? 2 : 1.2f) || !OnRoad(body.Position, body.Rotation) ? 7 : float.PositiveInfinity;
        targetSpeed = MathF.Min(targetSpeed, recoveryLimit);
        var obstacleLimit = _line.ObstacleSpeed(actor);
        targetSpeed = MathF.Min(targetSpeed, obstacleLimit);
        var trafficLimit = float.PositiveInfinity;
        if (blocked && !canPass)
        {
            var leaderSpeed = MathF.Max(0, Vector2.Dot(other!.Controller.Velocity, tangent));
            var gap = ahead - car.Length - 1 - speed * .25f;
            var followingSpeed = MathF.Sqrt(leaderSpeed * leaderSpeed +
                2 * car.Asphalt.BrakeDeceleration * .65f * MathF.Max(0, gap));
            if (ahead < car.Length + 1.5f)
                followingSpeed = leaderSpeed * Math.Clamp((ahead - car.Length - .2f) / 1.3f, 0, 1);
            trafficLimit = followingSpeed;
            targetSpeed = MathF.Min(targetSpeed, followingSpeed);
        }

        _drivingTime += seconds;
        _mistakeRemaining = MathF.Max(0, _mistakeRemaining - seconds);
        // Blue occasionally misjudges a corner entry and brakes too early, then recovers.
        // Road and barrier checks still apply; errors cost time rather than forcing a crash.
        if (_profile.Mistakes.Enabled && _drivingTime >= _nextMistake && speed > 7 && lateralError < .65f &&
            !_line.CanManeuver(_distance) && !blocked && _passingCar == null && _defendingAgainst == null)
        {
            MistakesMade++;
            _mistakeRemaining = _profile.Mistakes.DurationSeconds;
            _nextMistake = _drivingTime + _profile.Mistakes.IntervalSeconds +
                MistakesMade % 3 * _profile.Mistakes.VariationSeconds;
        }
        if (_mistakeRemaining > 0) targetSpeed *= _profile.Mistakes.SpeedMultiplier;
        Pace = new(cornerLimit, recoveryLimit, obstacleLimit, trafficLimit, targetSpeed);

        var waitingForTraffic = blocked && other!.Controller.Speed > 1;
        _stalled = speed < .2f && !waitingForTraffic ? _stalled + seconds : 0;
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
        var canManeuver = CanManeuver(actor.Controller.Speed);
        if (actor.Controller.SignedSpeed > 2 && canManeuver)
        {
            foreach (var challenger in others)
            {
                var isPlayer = ReferenceEquals(challenger, _player);
                if (!isPlayer && !_allowRivalDefense) continue;
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
                var priority = (holding ? arrival * .5f : arrival) - (isPlayer ? 1000 : 0);
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
        var rate = threat == null && canManeuver ? 1f : 2.5f;
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

    private bool PassingLaneAvailable(CarObject actor, CarObject passed, IReadOnlyList<CarObject> others,
        Vector2 target, Vector2 direction)
    {
        var normal = new Vector2(-direction.Y, direction.X);
        foreach (var other in others)
        {
            if (ReferenceEquals(other, actor) || ReferenceEquals(other, passed) || other.Controller.Speed > 2) continue;
            var ahead = Vector2.Dot(other.Transform.Position - actor.Transform.Position, direction);
            if (ahead < -car.Length || ahead > 14) continue;
            if (MathF.Abs(Vector2.Dot(other.Transform.Position - target, normal)) < car.Width + .2f) return false;
        }
        return true;
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

    // Leave time to merge before a bend and reserve grip for the lane change.
    private bool CanManeuver(float speed) => _line.CanManeuver(_distance, MathF.Max(16, speed + 8),
        MathF.Min(.035f, car.Asphalt.MaxLateralAcceleration * .5f / MathF.Max(64, speed * speed)));

    private bool OnRoad(Vector2 point, Vector2 direction) => _line.OnRoad(point, direction);

    public bool ConsumeJump() => false;
    public bool ConsumeViewToggle() => false;
    public bool ConsumeAction() => false;
    public Vector2 ConsumeMovement() => Vector2.Zero;
}
