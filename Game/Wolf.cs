using System.Diagnostics;
using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.Graphics;
using WolfNET.Game.InputEvents;

namespace WolfNET.Game;

public sealed class Wolf : IDisposable
{
    private readonly WebGpuRenderer _renderer;
    private readonly Window _window;
    private readonly InputEventSystem _inputEvents;
    
    private ObjectCollection _objectCollection;
    private PlayerObject _playerObject;
    public KeyTracker KeyTracker { get; private set;}
    public ICamera Camera => _playerObject.CameraComponent;
    public IMap Map { get; private set;}
    public RendererSettings RendererSettings { get; private set;}

    private readonly Stopwatch _clock = new();
    

    private double _lastFrameTime;
    private bool _running;
    private bool _disposed;

    public Wolf(
        RendererSettings settings,
        WebGpuRenderer renderer,
        InputEventSystem inputEvents,
        BlazorJSRuntime js)
    {
        RendererSettings = settings;
        _renderer = renderer;
        _inputEvents = inputEvents;

        _window = js.Get<Window>("window");
    }

    public void Start()
    {
        if (_running)
            return;
        _running = true;
        
        _objectCollection = new ObjectCollection();
        KeyTracker = new KeyTracker(_inputEvents);
        Map = new ConstantMap(this);

        _playerObject = new PlayerObject(this)
        {
            Transform = new Transform
            {
                Position = new Vector2(22, 12),
                Rotation = new Vector2(-1, 0),
            }
        };
        
        _objectCollection.Add(_playerObject);
        
        _running = true;

        _clock.Restart();
        _lastFrameTime = 0;
        
        _window.OnKeyDown += OnKeyDown;
        _window.OnKeyUp += OnKeyUp;

        Console.WriteLine(
            $"[GAME] Started. Target FPS: {RendererSettings.TargetFPS}");

        _window.RequestAnimationFrame(Frame);
    }

    private void Frame()
    {
        if (!_running)
            return;

        try
        {
            _objectCollection.Update((float) (_clock.Elapsed.TotalMilliseconds - _lastFrameTime));
            _renderer.Render(Camera.GetView());
            FrameDelay();
            _window.RequestAnimationFrame(Frame);
        }
        catch (Exception ex)
        {
            _running = false;

            Console.Error.WriteLine(
                $"[GAME] Frame failed: {ex}");
        }
    }

    private void FrameDelay()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds - _lastFrameTime;
        var diff = RendererSettings.TargetFrameInterval - elapsed;
            
        if (diff > 0)
        {
            Thread.Sleep((int) diff);
        }
            
        _lastFrameTime = _clock.Elapsed.TotalMilliseconds;
    }

    public void Stop()
    {
        _running = false;
        _clock.Stop();

        Console.WriteLine("[GAME] Stopped");
    }
    
    private void OnKeyDown(KeyboardEvent e)
    {
        Console.WriteLine(
            $"[INPUT] KeyDown: {e.Code}, repeat={e.Repeat}");
        e.PreventDefault();
        _inputEvents.TriggerEvent(new KeyDownEvent {Event = e});
    }

    private void OnKeyUp(KeyboardEvent e)
    {
        Console.WriteLine(
            $"[INPUT] KeyUp: {e.Code}");
        e.PreventDefault();
        _inputEvents.TriggerEvent(new KeyUpEvent {Event = e});
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
        
        _window.OnKeyDown -= OnKeyDown;
        _window.OnKeyUp -= OnKeyUp;

        _window.Dispose();
    }
}