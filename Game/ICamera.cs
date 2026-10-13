using System.Numerics;
using WolfNET.Game.Graphics;

namespace WolfNET.Game;

public interface ICamera
{
    Transform Transform { get; }
    Vector2 Plane { get; }

    Texture GetView();
}