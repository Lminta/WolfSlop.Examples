using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;

namespace WolfNET.Game.Player.Components;

public class PlayerController : ComponentBase
{
    private readonly KeyTracker _keyTracker;
    private readonly MouseTracker _mouseTracker;
    // Radians per pixel; mouse turning is independent of frame duration.
    private const float MouseSensitivity = 0.0025f;
    private readonly CameraComponent _camera;
    private readonly INavigationMap _map;
    
    public PlayerController(KeyTracker keyTracker, MouseTracker mouseTracker, INavigationMap map, CameraComponent camera, ObjectBase parent) : base(parent)
    {
        _keyTracker = keyTracker;
        _mouseTracker = mouseTracker;
        _map = map;
        _camera = camera;
    }

    public override void Update(float deltaTime)
    {
        //speed modifiers
        deltaTime /= 1000f; 
        var moveSpeed = deltaTime * 8f; //the constant value is in squares/second
        var rotSpeed = deltaTime * 3f; //the constant value is in radians/second

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
            if (_map.IsWalkable((int)MathF.Floor(posX + movement.X), (int)MathF.Floor(posY)))
                posX += movement.X;
            if (_map.IsWalkable((int)MathF.Floor(posX), (int)MathF.Floor(posY + movement.Y)))
                posY += movement.Y;
        }
        var turn = -_mouseTracker.ConsumeHorizontalMovement() * MouseSensitivity;
        if (_keyTracker.KeyState.Right)
            turn -= rotSpeed;
        if (_keyTracker.KeyState.Left)
            turn += rotSpeed;

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
}
