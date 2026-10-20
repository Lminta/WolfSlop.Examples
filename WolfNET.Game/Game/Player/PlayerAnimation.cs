using System.Numerics;
using WolfNET.Game.InputEvents;

namespace WolfNET.Game.Player;

public sealed class PlayerAnimation
{
    public int Frame { get; private set; } = 14;
    private int _start = 14;
    private float _elapsed;

    public void Update(float milliseconds, Vector2 displacement, KeyState keys, bool inAir, bool crouching)
    {
        var moving = displacement.LengthSquared() > .00000001f;
        var forward = (keys.Forward ? 1 : 0) - (keys.Backward ? 1 : 0);
        var strafe = (keys.StrafeRight ? 1 : 0) - (keys.StrafeLeft ? 1 : 0);
        var start = inAir ? 12 : crouching ? 10 : !moving ? 14 :
            strafe == 0 ? 0 : forward != 0 ? (strafe < 0 ? 2 : 4) : (strafe < 0 ? 6 : 8);
        if (start != _start) { _elapsed = 0; _start = start; }
        else if (inAir || moving) _elapsed = (_elapsed + MathF.Max(0, milliseconds)) % 320;
        else _elapsed = 0;
        Frame = start + (start == 14 ? 0 : (int)(_elapsed / 160));
    }
}
