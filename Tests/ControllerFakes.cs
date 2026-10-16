// Browser input and object ownership are replaced here; movement uses the real controller.
using System.Numerics;

namespace WolfNET.Game.Core
{
    public sealed class ObjectBase
    {
        public Transform Transform { get; set; }
    }
}

namespace WolfNET.Game.InputEvents
{
    public struct KeyState
    {
        public bool Forward, Backward, Left, Right, StrafeLeft, StrafeRight;
    }

    public sealed class KeyTracker
    {
        public KeyState KeyState { get; set; }
    }

    public sealed class MouseTracker
    {
        public float ConsumeHorizontalMovement() => 0;
    }
}

namespace WolfNET.Game.Player.Components
{
    public sealed class CameraComponent
    {
        public Vector2 Plane { get; set; }
    }
}
