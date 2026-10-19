using System.Numerics;

namespace WolfNET.Game;

public interface ICamera
{
    Transform Transform { get; }
    Vector2 Plane { get; }
    float Pitch { get; }
    float Elevation { get; }
}
