using System.Numerics;
using WolfNET.Game.Core;

namespace WolfNET.Game.Player.Components;

public class CameraComponent : ComponentBase, ICamera
{
    public CameraComponent(ObjectBase parent) : base(parent)
    {
    }

    public Transform Transform => Parent.Transform;
    public Vector2 Plane { get; set; } = new(0, 0.66f);
    // Radians; positive looks up. Simulated pitch shifts the projected horizon.
    public float Pitch { get; set; }
}
