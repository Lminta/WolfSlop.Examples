using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.InputEvents;

/// <summary>Adapts browser keyboard events to the engine's synchronous input state.</summary>
public sealed class KeyTracker : IKeyboardInput, IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _isActive;
    private readonly KeyboardState _state = new();
    private bool _disposed;

    public event Action? EscapePressed;
    public KeyState KeyState => Active ? _state.KeyState : default;
    private bool Active => !_disposed && _isActive();

    public KeyTracker(BlazorJSRuntime js, Func<bool> isActive)
    {
        _isActive = isActive;
        _window = js.Get<Window>("window");
        _window.OnKeyDown += OnKeyDown;
        _window.OnKeyUp += OnKeyUp;
        _window.OnBlur += OnBlur;
    }

    private void OnKeyDown(KeyboardEvent e)
    {
        if (!Active) return;
        if (e.Code == "Escape")
        {
            Reset();
            EscapePressed?.Invoke();
            return;
        }
        if (!KeyboardState.Handles(e.Code)) return;
        e.PreventDefault();
        _state.Press(e.Code, e.Repeat);
    }

    private void OnKeyUp(KeyboardEvent e)
    {
        if (Active && KeyboardState.Handles(e.Code)) e.PreventDefault();
        _state.Release(e.Code);
    }

    public void Reset() => _state.Reset();
    public bool ConsumeJump() => _state.ConsumeJump() && Active;
    public bool ConsumeViewToggle() => _state.ConsumeViewToggle() && Active;
    public bool ConsumeAction() => _state.ConsumeAction() && Active;
    private void OnBlur(Event e) => Reset();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.OnKeyDown -= OnKeyDown;
        _window.OnKeyUp -= OnKeyUp;
        _window.OnBlur -= OnBlur;
        Reset();
        EscapePressed = null;
        _window.Dispose();
    }
}
