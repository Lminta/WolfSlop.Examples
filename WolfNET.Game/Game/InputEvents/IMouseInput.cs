using System.Numerics;

namespace WolfNET.Game.InputEvents;

public interface IMouseInput
{
    Vector2 ConsumeMovement();
}
