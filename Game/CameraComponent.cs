using System.Numerics;

namespace WolfNET.Game;

public class CameraComponent : ComponentBase, ICamera
{
    public CameraComponent(ObjectBase parent) : base(parent)
    {
    }

    public Transform Transform => Parent.Transform;
    public Vector2 Plane { get; set; }
}