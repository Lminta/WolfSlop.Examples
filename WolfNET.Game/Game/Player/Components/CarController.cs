using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Physics;

namespace WolfNET.Game.Player.Components;

/// <summary>Fixed-step, surface-aware car movement. One map cell is one world unit.</summary>
public sealed class CarController : ComponentBase
{
    private const double StepSeconds = 1d / 120;
    private readonly IKeyboardInput _input;
    private readonly Func<Vector2, string> _surfaceAt;
    private readonly CarSettings _settings;
    private readonly OrientedBoxCollider _collider;
    private double _accumulator;

    public Vector2 Velocity { get; private set; }
    public float SteeringAngle { get; private set; }
    public float AngularVelocity { get; private set; }
    public float Speed => Velocity.Length();
    public float SignedSpeed => Vector2.Dot(Velocity, Parent.Transform.Rotation);

    public CarController(ObjectBase parent, IKeyboardInput input, INavigationMap navigation,
        Func<Vector2, string> surfaceAt, CarSettings settings) : base(parent)
    {
        _input = input;
        _surfaceAt = surfaceAt;
        _settings = settings;
        _collider = new OrientedBoxCollider(navigation, settings.Length, settings.Width);
    }

    // Pausing discards unfinished time while preserving all physical state.
    public void ResetTiming() => _accumulator = 0;

    public override void Update(float deltaTime)
    {
        _input.ConsumeJump();
        if (!float.IsFinite(deltaTime) || deltaTime <= 0) return;
        // A suspended browser frame must not fast-forward the car through the course.
        _accumulator += Math.Min(deltaTime / 1000d, .25);
        var keys = _input.KeyState;
        while (_accumulator + 1e-7 >= StepSeconds)
        {
            Step((float)StepSeconds, keys);
            _accumulator = Math.Max(0, _accumulator - StepSeconds);
        }
    }

    private void Step(float seconds, KeyState keys)
    {
        var transform = Parent.Transform;
        var forward = Vector2.Normalize(transform.Rotation);
        var right = new Vector2(forward.Y, -forward.X);
        var surface = _surfaceAt(transform.Position) == "asphalt" ? _settings.Asphalt : _settings.OffRoad;
        var steeringInput = (keys.StrafeLeft || keys.Left ? 1 : 0) - (keys.StrafeRight || keys.Right ? 1 : 0);
        var steeringTarget = steeringInput * _settings.MaxSteeringDegrees * MathF.PI / 180;
        var steeringRate = (steeringInput == 0 ? _settings.SteeringReturnDegrees : _settings.SteeringRateDegrees) * MathF.PI / 180;
        SteeringAngle = Approach(SteeringAngle, steeringTarget, steeringRate * seconds);

        var longitudinal = Vector2.Dot(Velocity, forward);
        var lateral = Vector2.Dot(Velocity, right);
        var braking = keys.Jump || (keys.Forward && keys.Backward) ||
                      (keys.Backward && longitudinal >= -_settings.StopSpeed &&
                       Velocity.LengthSquared() > _settings.StopSpeed * _settings.StopSpeed) ||
                      (keys.Forward && longitudinal < -_settings.StopSpeed);
        var throttle = braking ? 0 : keys.Forward ? 1 : keys.Backward ? -1 : 0;
        if (braking)
        {
            var speed = Velocity.Length();
            var scale = speed > 0 ? MathF.Max(0, speed - surface.BrakeDeceleration * seconds) / speed : 0;
            longitudinal *= scale;
            lateral *= scale;
        }
        else if (throttle != 0)
        {
            var maximum = throttle > 0 ? surface.MaxForwardSpeed : surface.MaxReverseSpeed;
            var acceleration = throttle > 0 ? surface.Acceleration : surface.ReverseAcceleration;
            var ratio = MathF.Max(0, longitudinal * throttle) / maximum;
            var engine = acceleration * MathF.Max(0, 1 - ratio * ratio);
            longitudinal += throttle * engine * seconds;
        }

        var limit = longitudinal < 0 ? surface.MaxReverseSpeed : surface.MaxForwardSpeed;
        var totalSpeed = MathF.Sqrt(longitudinal * longitudinal + lateral * lateral);
        var excess = MathF.Max(0, totalSpeed - limit);
        if (excess > 0)
        {
            // The terrain penalty acts on motion, including sideways sliding.
            var scale = (limit + excess * MathF.Exp(-surface.SurfacePenaltyCoefficient * seconds)) / totalSpeed;
            longitudinal *= scale;
            lateral *= scale;
        }
        // The engine compensates rolling resistance under throttle, allowing
        // acceleration to approach the configured terminal speed.
        if (throttle == 0) longitudinal = Approach(longitudinal, 0, surface.RollingResistance * seconds);
        // Lateral grip is tire friction. Strong asphalt grip arrests skids faster;
        // lower off-road grip preserves the velocity vector longer through turns.
        lateral *= MathF.Exp(-surface.LateralGrip * seconds);
        Velocity = forward * longitudinal + right * lateral;
        if (throttle == 0 && Velocity.LengthSquared() < _settings.StopSpeed * _settings.StopSpeed)
            Velocity = Vector2.Zero;

        var yawTarget = longitudinal / _settings.Wheelbase * MathF.Tan(SteeringAngle);
        var yawLimit = surface.MaxLateralAcceleration / MathF.Max(MathF.Abs(longitudinal), _settings.StopSpeed);
        yawTarget = Math.Clamp(yawTarget, -yawLimit, yawLimit);
        AngularVelocity += (yawTarget - AngularVelocity) * (1 - MathF.Exp(-_settings.AngularResponse * seconds));
        if (Velocity == Vector2.Zero) AngularVelocity = 0;
        var rotation = AngularVelocity * seconds;
        var result = _collider.MoveAndSlide(transform.Position, forward, Velocity * seconds, rotation, Velocity);
        Velocity = result.Velocity;
        if (result.Collided && Vector2.DistanceSquared(result.Direction, Rotate(forward, rotation)) > .00000001f)
            AngularVelocity = 0;
        Parent.Transform = new Transform { Position = result.Position, Rotation = result.Direction };
    }

    private static float Approach(float value, float target, float change) =>
        value + Math.Clamp(target - value, -change, change);

    private static Vector2 Rotate(Vector2 direction, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return new(direction.X * cosine - direction.Y * sine, direction.X * sine + direction.Y * cosine);
    }
}
