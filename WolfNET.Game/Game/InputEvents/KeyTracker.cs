using WolfNET.Game.EventSystem.Interfaces;

using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.InputEvents;

public struct KeyState
{
    public bool Forward;
    public bool Backward;
    public bool Left;
    public bool Right;
    public bool StrafeLeft;
    public bool StrafeRight;
    public bool Jump;
    public bool Crouch;
}

public class KeyTracker : IEventListener<InputEventSystem, KeyDownEvent>, IEventListener<InputEventSystem, KeyUpEvent>, IDisposable
{
    private readonly InputEventSystem _input;
    private readonly Window _window;
    private readonly Func<bool> _isActive;
    private bool _disposed;

    public event Action? EscapePressed;
    
    private KeyState _keyState;
    private bool _jumpRequested;
    private bool _viewToggleRequested;
    private bool _actionRequested;
    private bool _altLeft;
    private bool _altRight;
    
    public KeyState KeyState => _isActive() ? _keyState : default;
    
    public KeyTracker(InputEventSystem input, BlazorJSRuntime js, Func<bool> isActive)
    {
        _input = input;
        _isActive = isActive;
        _window = js.Get<Window>("window");
        
        _input.AddListener<KeyDownEvent>(this);
        _input.AddListener<KeyUpEvent>(this);
        _window.OnKeyDown += OnKeyDown;
        _window.OnKeyUp += OnKeyUp;
        _window.OnBlur += OnBlur;
    }

    private void OnKeyDown(KeyboardEvent e)
    {
        if (!_isActive()) return;
        if (e.Code == "Escape")
        {
            Reset();
            EscapePressed?.Invoke();
            return;
        }

        if (IsMovementKey(e.Code))
        {
            e.PreventDefault();
            // A held key must not restart movement after Esc clears input.
            if (e.Repeat) return;
        }
        _input.TriggerEvent(new KeyDownEvent { Event = e });
    }

    private void OnKeyUp(KeyboardEvent e)
    {
        if (!_isActive()) return;
        if (e.Code == "Escape")
            return;

        if (IsMovementKey(e.Code)) e.PreventDefault();
        _input.TriggerEvent(new KeyUpEvent { Event = e });
    }

    public void Reset()
    {
        _keyState = default;
        _jumpRequested = false;
        _viewToggleRequested = false;
        _actionRequested = false;
        _altLeft = _altRight = false;
    }

    public bool ConsumeJump()
    {
        var requested = _isActive() && _jumpRequested;
        _jumpRequested = false;
        return requested;
    }

    public bool ConsumeViewToggle()
    {
        var requested = _isActive() && _viewToggleRequested;
        _viewToggleRequested = false;
        return requested;
    }

    public bool ConsumeAction()
    {
        var requested = _isActive() && _actionRequested;
        _actionRequested = false;
        return requested;
    }

    private void OnBlur(Event e) => Reset();

    private static bool IsMovementKey(string code) => code is
        "KeyF" or "KeyV" or "KeyW" or "KeyS" or "KeyA" or "KeyD" or "Space" or "AltLeft" or "AltRight";
    
    public Task OnEvent(KeyDownEvent gameEvent)
    {
        if (!_isActive()) return Task.CompletedTask;
        switch (gameEvent.Event.Code)
        {
            case "KeyF":
                if (!gameEvent.Event.Repeat) _actionRequested = true;
                break;
            case "KeyV":
                if (!gameEvent.Event.Repeat) _viewToggleRequested = !_viewToggleRequested;
                break;
            case "AltLeft":
                _altLeft = true;
                _keyState.Crouch = true;
                break;
            case "AltRight":
                _altRight = true;
                _keyState.Crouch = true;
                break;
            case "Space":
                _keyState.Jump = true;
                if (!gameEvent.Event.Repeat) _jumpRequested = true;
                break;
            case "KeyW":
                _keyState.Forward = true;
                break;

            case "KeyS":
                _keyState.Backward = true;
                break;

            case "KeyA":
                _keyState.StrafeLeft = true;
                break;

            case "KeyD":
                _keyState.StrafeRight = true;
                break;
        }

        return Task.CompletedTask;
    }

    public Task OnEvent(KeyUpEvent gameEvent)
    {
        switch (gameEvent.Event.Code)
        {
            case "AltLeft":
                _altLeft = false;
                _keyState.Crouch = _altRight;
                break;
            case "AltRight":
                _altRight = false;
                _keyState.Crouch = _altLeft;
                break;
            case "Space":
                _keyState.Jump = false;
                break;
            case "KeyW":
                _keyState.Forward = false;
                break;

            case "KeyS":
                _keyState.Backward = false;
                break;

            case "KeyA":
                _keyState.StrafeLeft = false;
                break;

            case "KeyD":
                _keyState.StrafeRight = false;
                break;
        }
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.OnKeyDown -= OnKeyDown;
        _window.OnKeyUp -= OnKeyUp;
        _window.OnBlur -= OnBlur;
        _input.RemoveListener<KeyDownEvent>(this);
        _input.RemoveListener<KeyUpEvent>(this);
        Reset();
        EscapePressed = null;
        _window.Dispose();
    }
}
