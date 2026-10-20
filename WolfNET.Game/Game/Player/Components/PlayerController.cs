using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;

namespace WolfNET.Game.Player.Components;

public class PlayerController : ComponentBase
{
    // World units: a cell is one unit wide.
    private float CollisionRadius => _settings.CollisionRadius;
    private const float ContactClearance = 0.00002f;
    private readonly KeyTracker _keyTracker;
    private readonly MouseTracker _mouseTracker;
    // Radians per pixel before applying the map's maximum turn rate.
    private const float MouseSensitivity = 0.0025f;
    public const float MaxPitch = MathF.PI / 6;
    private readonly CameraComponent _camera;
    private readonly INavigationMap _map;
    private readonly PlayerStartDefinition _settings;
    private float _jumpElapsed;
    private float _jumpTime;
    private float _jumpSpeed;
    private Vector2 _jumpOrigin;
    private Vector2 _jumpDirection;
    private readonly HashSet<(int X, int Y)> _landingBlockers = [];
    public bool InAir { get; private set; }
    public bool Crouching { get; private set; }
    public Vector2 JumpDestination { get; private set; }
    
    public PlayerController(KeyTracker keyTracker, MouseTracker mouseTracker, INavigationMap map, CameraComponent camera, PlayerStartDefinition settings, ObjectBase parent) : base(parent)
    {
        _keyTracker = keyTracker;
        _mouseTracker = mouseTracker;
        _map = map;
        _camera = camera;
        _settings = settings;
    }

    public override void Update(float deltaTime)
    {
        deltaTime /= 1000f;
        var moveSpeed = deltaTime * _settings.MoveSpeed;
        var rotSpeed = deltaTime * _settings.RotSpeed;

        var posX = Parent.Transform.Position.X;
        var posY = Parent.Transform.Position.Y;
        var dirX = Parent.Transform.Rotation.X;
        var dirY = Parent.Transform.Rotation.Y;

        if (_keyTracker.ConsumeViewToggle()) _camera.ThirdPerson = !_camera.ThirdPerson;
        var keys = _keyTracker.KeyState;
        // Third-person input steers the character first; translation and jump
        // planning must use the same heading that this frame's camera follows.
        if (_camera.ThirdPerson) ApplyLook(rotSpeed, ref dirX, ref dirY);
        // Stay low under an obstacle after Alt is released; stand only when the
        // complete collision circle has cleared it. Crouching never bypasses walls in flight.
        Crouching = !InAir && (keys.Crouch || (Crouching && !CanOccupy(posX, posY, inAir: false, crouching: false)));
        if (Crouching) moveSpeed *= _settings.CrouchSpeedMultiplier;
        var forward = (keys.Forward ? 1 : 0) - (keys.Backward ? 1 : 0);
        var strafe = (keys.StrafeRight ? 1 : 0) - (keys.StrafeLeft ? 1 : 0);
        var movement = new Vector2(dirX, dirY) * forward +
                       new Vector2(dirY, -dirX) * strafe;

        var jumpRequested = _keyTracker.ConsumeJump();
        if (jumpRequested && !InAir && !Crouching && _settings.JumpLength > 0 && _settings.MoveSpeed > 0)
        {
            _jumpOrigin = ResolveContacts(new(posX, posY));
            _jumpDirection = Vector2.Normalize(movement.LengthSquared() > 0 ? movement : new(dirX, dirY));
            _jumpSpeed = _settings.MoveSpeed * _settings.JumpSpeedMultiplier;
            _jumpTime = _settings.JumpLength / _jumpSpeed;
            JumpDestination = PlanJump(_jumpOrigin, _jumpOrigin + _jumpDirection * _settings.JumpLength);
            _jumpElapsed = 0;
            InAir = true;
        }

        // Combine inputs before normalizing so diagonal movement has the same speed.
        if (InAir)
        {
            // Horizontal input and subsequent camera turns cannot steer this trajectory.
            _jumpElapsed = MathF.Min(_jumpTime, _jumpElapsed + deltaTime);
            var distance = MathF.Min(_jumpElapsed * _jumpSpeed, Vector2.Distance(_jumpOrigin, JumpDestination));
            var position = _jumpOrigin + _jumpDirection * distance;
            posX = position.X;
            posY = position.Y;
            var progress = _jumpElapsed / _jumpTime;
            _camera.Elevation = 4 * _settings.JumpRise * progress * (1 - progress);
            if (progress >= 0.999999f)
            {
                posX = JumpDestination.X;
                posY = JumpDestination.Y;
                InAir = false;
                _camera.Elevation = 0;
                _landingBlockers.Clear();
            }
        }
        else if (movement.LengthSquared() > 0)
        {
            movement = Vector2.Normalize(movement) * moveSpeed;
            // Short steps prevent crossing walls during fast movement or slow frames.
            var steps = Math.Max(1, (int)MathF.Ceiling(movement.Length() / (CollisionRadius * 0.5f)));
            var step = movement / steps;
            var position = ResolveContacts(new Vector2(posX, posY));
            for (int i = 0; i < steps; i++)
            {
                var candidate = ResolveContacts(position + step);
                if (CanOccupy(candidate.X, candidate.Y)) position = candidate;
            }
            posX = position.X;
            posY = position.Y;
        }
        if (!InAir)
        {
            var target = Crouching ? -_settings.CrouchDrop : 0;
            var change = deltaTime * _settings.CrouchDrop / 0.12f;
            _camera.Elevation += Math.Clamp(target - _camera.Elevation, -change, change);
        }
        if (!_camera.ThirdPerson) ApplyLook(rotSpeed, ref dirX, ref dirY);
        Parent.Transform = new Transform()
        {
            Position = new Vector2(posX, posY),
            Rotation = new Vector2(dirX, dirY)
        };
    }

    private void ApplyLook(float rotSpeed, ref float dirX, ref float dirY)
    {
        var planeX = _camera.Plane.X;
        var planeY = _camera.Plane.Y;
        var mouseMovement = _mouseTracker.ConsumeMovement();
        var turn = -mouseMovement.X * MouseSensitivity;
        var pitchChange = Math.Clamp(-mouseMovement.Y * MouseSensitivity, -rotSpeed, rotSpeed);
        _camera.Pitch = Math.Clamp(_camera.Pitch + pitchChange, -MaxPitch, MaxPitch);
        if (_keyTracker.KeyState.Right)
            turn -= rotSpeed;
        if (_keyTracker.KeyState.Left)
            turn += rotSpeed;
        turn = Math.Clamp(turn, -rotSpeed, rotSpeed);

        if (turn != 0)
        {
            var cos = MathF.Cos(turn);
            var sin = MathF.Sin(turn);
            // Rotate direction and camera plane together to preserve the field of view.
            var oldDirX = dirX;
            dirX = dirX * cos - dirY * sin;
            dirY = oldDirX * sin + dirY * cos;
            var oldPlaneX = planeX;
            planeX = planeX * cos - planeY * sin;
            planeY = oldPlaneX * sin + planeY * cos;
        }

        _camera.Plane = new Vector2(planeX, planeY);
    }

    private Vector2 PlanJump(Vector2 origin, Vector2 desired)
    {
        _landingBlockers.Clear();
        var target = desired;
        // A shortened path may end over another jumpable cell. Promote that cell
        // to a solid obstacle too, until the complete circle has a clear landing.
        for (int attempt = 0; attempt < 4096; attempt++)
        {
            for (int x = (int)MathF.Floor(target.X - CollisionRadius); x <= (int)MathF.Floor(target.X + CollisionRadius); x++)
            for (int y = (int)MathF.Floor(target.Y - CollisionRadius); y <= (int)MathF.Floor(target.Y + CollisionRadius); y++)
            {
                if (_map.IsWalkable(x, y)) continue;
                var nearest = Vector2.Clamp(target, new(x, y), new(x + 1, y + 1));
                if (Vector2.DistanceSquared(target, nearest) < CollisionRadius * CollisionRadius)
                    _landingBlockers.Add((x, y));
            }
            target = TraceJump(origin, desired);
            if (CanOccupy(target.X, target.Y, inAir: false)) return target;
        }
        return origin;
    }

    private Vector2 TraceJump(Vector2 origin, Vector2 desired)
    {
        var delta = desired - origin;
        var steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / (CollisionRadius * 0.5f)));
        var previous = origin;
        for (int i = 1; i <= steps; i++)
        {
            var candidate = origin + delta * (i / (float)steps);
            if (CanOccupy(candidate.X, candidate.Y, inAir: true)) { previous = candidate; continue; }
            float clear = 0, blocked = 1;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                var fraction = (clear + blocked) * 0.5f;
                var point = Vector2.Lerp(previous, candidate, fraction);
                if (CanOccupy(point.X, point.Y, inAir: true)) clear = fraction;
                else blocked = fraction;
            }
            var distance = MathF.Max(0, Vector2.Distance(previous, candidate) * clear - ContactClearance);
            return previous + Vector2.Normalize(delta) * distance;
        }
        return desired;
    }

    private Vector2 ResolveContacts(Vector2 position)
    {
        // Push along the contact normal, preserving tangential movement around corners.
        // A small clearance prevents rounding errors from pinning the player to a wall.
        for (int iteration = 0; iteration < 8; iteration++)
        {
            var corrected = false;
            var minX = (int)MathF.Floor(position.X - CollisionRadius);
            var maxX = (int)MathF.Floor(position.X + CollisionRadius);
            var minY = (int)MathF.Floor(position.Y - CollisionRadius);
            var maxY = (int)MathF.Floor(position.Y + CollisionRadius);
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                if (_map.IsWalkable(x, y, InAir, Crouching)) continue;
                var nearest = Vector2.Clamp(position, new(x, y), new(x + 1, y + 1));
                var separation = position - nearest;
                var distanceSquared = separation.LengthSquared();
                if (distanceSquared >= CollisionRadius * CollisionRadius) continue;
                if (distanceSquared > 0)
                {
                    var distance = MathF.Sqrt(distanceSquared);
                    position += separation / distance * (CollisionRadius + ContactClearance - distance);
                }
                else
                {
                    // Recover a spawn or edited scene whose circle starts inside a cell.
                    var left = position.X - x;
                    var right = x + 1 - position.X;
                    var top = position.Y - y;
                    var bottom = y + 1 - position.Y;
                    var closest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                    if (closest == left) position.X = x - CollisionRadius - ContactClearance;
                    else if (closest == right) position.X = x + 1 + CollisionRadius + ContactClearance;
                    else if (closest == top) position.Y = y - CollisionRadius - ContactClearance;
                    else position.Y = y + 1 + CollisionRadius + ContactClearance;
                }
                corrected = true;
            }
            if (!corrected) break;
        }
        return position;
    }

    private bool CanOccupy(float x, float y, bool? inAir = null, bool? crouching = null)
    {
        var airborne = inAir ?? InAir;
        for (int cellX = (int)MathF.Floor(x - CollisionRadius); cellX <= (int)MathF.Floor(x + CollisionRadius); cellX++)
        for (int cellY = (int)MathF.Floor(y - CollisionRadius); cellY <= (int)MathF.Floor(y + CollisionRadius); cellY++)
        {
            if (_map.IsWalkable(cellX, cellY, airborne, crouching ?? Crouching) && (!airborne || !_landingBlockers.Contains((cellX, cellY)))) continue;
            // Circle versus square, including rounded clearance around corners.
            var dx = x - Math.Clamp(x, cellX, cellX + 1);
            var dy = y - Math.Clamp(y, cellY, cellY + 1);
            if (dx * dx + dy * dy < CollisionRadius * CollisionRadius) return false;
        }
        return true;
    }
}
