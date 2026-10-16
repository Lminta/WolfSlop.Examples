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
}

public class KeyTracker : IEventListener<InputEventSystem, KeyDownEvent>, IEventListener<InputEventSystem, KeyUpEvent>, IDisposable
{
    private readonly InputEventSystem _input;
    private readonly Window _window;
    private bool _disposed;

    public event Action? EscapePressed;
    
    private KeyState _keyState;
    
    public KeyState KeyState => _keyState;
    
    public KeyTracker(InputEventSystem input, BlazorJSRuntime js)
    {
        _input = input;
        _window = js.Get<Window>("window");
        
        _input.AddListener<KeyDownEvent>(this);
        _input.AddListener<KeyUpEvent>(this);
        _window.OnKeyDown += OnKeyDown;
        _window.OnKeyUp += OnKeyUp;
        _window.OnBlur += OnBlur;
    }

    private void OnKeyDown(KeyboardEvent e)
    {
        if (e.Code == "Escape")
        {
            EscapePressed?.Invoke();
            return;
        }

        if (IsMovementKey(e.Code)) e.PreventDefault();
        _input.TriggerEvent(new KeyDownEvent { Event = e });
    }

    private void OnKeyUp(KeyboardEvent e)
    {
        if (e.Code == "Escape")
            return;

        if (IsMovementKey(e.Code)) e.PreventDefault();
        _input.TriggerEvent(new KeyUpEvent { Event = e });
    }

    private void OnBlur(Event e) => _keyState = default;

    private static bool IsMovementKey(string code) => code is
        "KeyW" or "KeyS" or "KeyA" or "KeyD" or "KeyQ" or "KeyE";
    
    public Task OnEvent(KeyDownEvent gameEvent)
    {
        switch (gameEvent.Event.Code)
        {
            case "KeyW":
                _keyState.Forward = true;
                break;

            case "KeyS":
                _keyState.Backward = true;
                break;

            case "KeyA":
                _keyState.Left = true;
                break;

            case "KeyD":
                _keyState.Right = true;
                break;

            case "KeyQ":
                _keyState.StrafeLeft = true;
                break;

            case "KeyE":
                _keyState.StrafeRight = true;
                break;
        }

        return Task.CompletedTask;
    }

    public Task OnEvent(KeyUpEvent gameEvent)
    {
        switch (gameEvent.Event.Code)
        {
            case "KeyW":
                _keyState.Forward = false;
                break;

            case "KeyS":
                _keyState.Backward = false;
                break;

            case "KeyA":
                _keyState.Left = false;
                break;

            case "KeyD":
                _keyState.Right = false;
                break;

            case "KeyQ":
                _keyState.StrafeLeft = false;
                break;

            case "KeyE":
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
        _keyState = default;
        EscapePressed = null;
        _window.Dispose();
    }
}
