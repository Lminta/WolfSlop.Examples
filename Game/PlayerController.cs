using System.Numerics;
using WolfNET.Game.InputEvents;

namespace WolfNET.Game;

public class PlayerController : ComponentBase
{
    private readonly KeyTracker _keyTracker;
    private readonly CameraComponent _camera;
    private readonly IMap _map;
    
    public PlayerController(KeyTracker keyTracker, IMap map, CameraComponent camera, ObjectBase parent) : base(parent)
    {
        _keyTracker = keyTracker;
        _map = map;
        _camera = camera;
    }

    public override void Update(float deltaTime)
    {
        //speed modifiers
        var moveSpeed = deltaTime * 5.0f; //the constant value is in squares/second
        var rotSpeed = deltaTime * 3.0f; //the constant value is in radians/second

        var posX = Parent.Transform.Position.X;
        var posY = Parent.Transform.Position.Y;
        var dirX = Parent.Transform.Rotation.X;
        var dirY = Parent.Transform.Rotation.Y;

        var planeX = _camera.Plane.X;
        var planeY = _camera.Plane.Y;

        //move forward if no wall in front of you
        if (_keyTracker.KeyState.Forward)
        {
            if (_map.WorldMap[(int)(posX + dirX * moveSpeed)][(int)(posY)] == 0) posX += dirX * moveSpeed;
            if (_map.WorldMap[(int)(posX)][(int)(posY + dirY * moveSpeed)] == 0) posY += dirY * moveSpeed;
        }

        //move backwards if no wall behind you
        if (_keyTracker.KeyState.Backward)
        {
            if (_map.WorldMap[(int)(posX - dirX * moveSpeed)][(int)(posY)] == 0) posX -= dirX * moveSpeed;
            if (_map.WorldMap[(int)(posX)][(int)(posY - dirY * moveSpeed)] == 0) posY -= dirY * moveSpeed;
        }

        float cos(float x) => (float)Math.Cos(x);
        float sin(float x) => (float)Math.Sin(x);
        //rotate to the right
        if (_keyTracker.KeyState.Right)
        {
            //both camera direction and camera plane must be rotated
            var oldDirX = dirX;
            dirX = dirX * cos(-rotSpeed) - dirY * sin(-rotSpeed);
            dirY = oldDirX * sin(-rotSpeed) + dirY * cos(-rotSpeed);
            var oldPlaneX = planeX;
            planeX = planeX * cos(-rotSpeed) - planeY * sin(-rotSpeed);
            planeY = oldPlaneX * sin(-rotSpeed) + planeY * cos(-rotSpeed);
        }

        //rotate to the left
        if (_keyTracker.KeyState.Left)
        {
            //both camera direction and camera plane must be rotated
            var oldDirX = dirX;
            dirX = dirX * cos(rotSpeed) - dirY * sin(rotSpeed);
            dirY = oldDirX * sin(rotSpeed) + dirY * cos(rotSpeed);
            var oldPlaneX = planeX;
            planeX = planeX * cos(rotSpeed) - planeY * sin(rotSpeed);
            planeY = oldPlaneX * sin(rotSpeed) + planeY * cos(rotSpeed);
        }

        _camera.Plane = new Vector2(planeX, planeY);
        Parent.Transform = new Transform()
        {
            Position = new Vector2(posX, posY),
            Rotation = new Vector2(dirX, dirY)
        };
    }
}