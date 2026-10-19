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

        var planeX = _camera.Plane.X;
        var planeY = _camera.Plane.Y;

        var keys = _keyTracker.KeyState;
        var forward = (keys.Forward ? 1 : 0) - (keys.Backward ? 1 : 0);
        var strafe = (keys.StrafeRight ? 1 : 0) - (keys.StrafeLeft ? 1 : 0);
        var movement = new Vector2(dirX, dirY) * forward +
                       new Vector2(dirY, -dirX) * strafe;

        // Combine inputs before normalizing so diagonal movement has the same speed.
        if (movement.LengthSquared() > 0)
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
        Parent.Transform = new Transform()
        {
            Position = new Vector2(posX, posY),
            Rotation = new Vector2(dirX, dirY)
        };
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
                if (_map.IsWalkable(x, y)) continue;
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

    private bool CanOccupy(float x, float y)
    {
        for (int cellX = (int)MathF.Floor(x - CollisionRadius); cellX <= (int)MathF.Floor(x + CollisionRadius); cellX++)
        for (int cellY = (int)MathF.Floor(y - CollisionRadius); cellY <= (int)MathF.Floor(y + CollisionRadius); cellY++)
        {
            if (_map.IsWalkable(cellX, cellY)) continue;
            // Circle versus square, including rounded clearance around corners.
            var dx = x - Math.Clamp(x, cellX, cellX + 1);
            var dy = y - Math.Clamp(y, cellY, cellY + 1);
            if (dx * dx + dy * dy < CollisionRadius * CollisionRadius) return false;
        }
        return true;
    }
}
