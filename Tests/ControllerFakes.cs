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
        public bool Forward, Backward, Left, Right, StrafeLeft, StrafeRight, Jump, Crouch;
    }

    public sealed class KeyTracker
    {
        private KeyState _state;
        private bool _jumpRequested;
        public KeyState KeyState
        {
            get => _state;
            set
            {
                if (value.Jump && !_state.Jump) _jumpRequested = true;
                _state = value;
            }
        }
        public bool ConsumeJump()
        {
            var requested = _jumpRequested;
            _jumpRequested = false;
            return requested;
        }
    }

    public sealed class MouseTracker
    {
        public float HorizontalMovement { get; set; }
        public float VerticalMovement { get; set; }
        public Vector2 ConsumeMovement()
        {
            var movement = new Vector2(HorizontalMovement, VerticalMovement);
            HorizontalMovement = 0;
            VerticalMovement = 0;
            return movement;
        }
    }
}

namespace WolfNET.Game.Player.Components
{
    public sealed class CameraComponent
    {
        public Vector2 Plane { get; set; }
        public float Pitch { get; set; }
        public float Elevation { get; set; }
    }
}
