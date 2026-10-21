namespace WolfNET.Game.InputEvents;

/// <summary>Held keys and pending actions, independent of browser event lifetimes.</summary>
public sealed class KeyboardState : IKeyboardInput
{
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private bool _jumpRequested;
    private bool _viewToggleRequested;
    private bool _actionRequested;

    public KeyState KeyState => new()
    {
        Forward = _held.Contains("KeyW"),
        Backward = _held.Contains("KeyS"),
        StrafeLeft = _held.Contains("KeyA"),
        StrafeRight = _held.Contains("KeyD"),
        Left = _held.Contains("ArrowLeft"),
        Right = _held.Contains("ArrowRight"),
        Jump = _held.Contains("Space"),
        Crouch = _held.Contains("AltLeft") || _held.Contains("AltRight")
    };

    public static bool Handles(string code) => code is
        "KeyW" or "KeyS" or "KeyA" or "KeyD" or "ArrowLeft" or "ArrowRight" or "Space" or "AltLeft" or "AltRight" or "KeyV" or "KeyF";

    public void Press(string code, bool repeat = false)
    {
        // A held key must not restart movement after capture loss clears input.
        if (repeat || !Handles(code) || !_held.Add(code)) return;
        switch (code)
        {
            case "Space": _jumpRequested = true; break;
            case "KeyV": _viewToggleRequested = !_viewToggleRequested; break;
            case "KeyF": _actionRequested = true; break;
        }
    }

    public void Release(string code) => _held.Remove(code);

    public bool ConsumeJump() => Consume(ref _jumpRequested);
    public bool ConsumeViewToggle() => Consume(ref _viewToggleRequested);
    public bool ConsumeAction() => Consume(ref _actionRequested);

    public void Reset()
    {
        _held.Clear();
        _jumpRequested = _viewToggleRequested = _actionRequested = false;
    }

    private static bool Consume(ref bool pending)
    {
        var result = pending;
        pending = false;
        return result;
    }
}
