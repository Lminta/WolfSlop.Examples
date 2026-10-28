using System.Numerics;
using WolfNET.Engine.Input;

namespace RaceImperialis.Game.Player;

public sealed class PlayerAnimation
{
    public int Frame { get; private set; } = 14;
    public float HandSwing { get; private set; }
    public float HandBob { get; private set; }
    public float HandStrafe { get; private set; }
    public float HandReach => _actionElapsed >= ActionDuration ? 0 : MathF.Sin(MathF.PI * _actionElapsed / ActionDuration);
    private const float ActionDuration = 360;
    private float _actionElapsed = ActionDuration;
    private int _start = 14;
    private float _elapsed;
    private float _handPhase;

    public void PlayAction() => _actionElapsed = 0;

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

        // Use resolved motion, so walking into a wall does not keep swinging the arms.
        var time = MathF.Max(0, milliseconds);
        _actionElapsed = MathF.Min(ActionDuration, _actionElapsed + time);
        var walking = moving && !inAir;
        if (walking) _handPhase = (_handPhase + time * (crouching ? .006f : .009f)) % MathF.Tau;
        var blend = 1 - MathF.Exp(-time / 65);
        var stride = crouching ? .45f : 1;
        HandSwing += ((walking ? MathF.Sin(_handPhase) * stride : 0) - HandSwing) * blend;
        HandBob += ((walking ? (1 - MathF.Cos(_handPhase * 2)) * .5f * stride : 0) - HandBob) * blend;
        HandStrafe += ((walking ? strafe : 0) - HandStrafe) * blend;
    }
}
