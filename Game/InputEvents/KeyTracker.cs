using WolfNET.Game.EventSystem.Interfaces;

namespace WolfNET.Game.InputEvents;

public struct KeyState
{
    public bool Forward;
    public bool Backward;
    public bool Left;
    public bool Right;
}

public class KeyTracker : IEventListener<InputEventSystem, KeyDownEvent>, IEventListener<InputEventSystem, KeyUpEvent>, IDisposable
{
    private readonly InputEventSystem _input;
    
    private KeyState _keyState;
    
    public KeyState KeyState => _keyState;
    
    public KeyTracker(InputEventSystem input)
    {
        _input = input;
        
        _input.AddListener<KeyDownEvent>(this);
        _input.AddListener<KeyUpEvent>(this);
    }
    
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
        }
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _input.RemoveListener<KeyDownEvent>(this);
        _input.RemoveListener<KeyUpEvent>(this);
    }
}