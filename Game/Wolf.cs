using System.Diagnostics;
using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player;
using WolfNET.Graphics;

namespace WolfNET.Game;

public sealed class Wolf : IDisposable
{
    private readonly WebGpuRenderer _renderer;
    private readonly Window _window;
    private readonly InputEventSystem _inputEvents;
    private readonly BlazorJSRuntime _js;
    
    private ObjectCollection _objectCollection;
    private PlayerObject _player;
    public KeyTracker KeyTracker { get; private set;}
    public MouseTracker MouseTracker { get; private set; } = null!;
    public IMap Map { get; private set;}
    public RendererSettings RendererSettings { get; private set;}

    private readonly Stopwatch _clock = new();
    

    private double _lastFrameEndTime;
    private double _lastFrameDeltaTime;
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
        _js = js;

        _window = js.Get<Window>("window");
    }

    public void Start(HTMLCanvasElement canvas)
    {
        if (_running)
            return;
        _running = true;
        
        _objectCollection = new ObjectCollection();
        MouseTracker = new MouseTracker(canvas, _js);
        KeyTracker = new KeyTracker(_inputEvents, _js);
        KeyTracker.EscapePressed += MouseTracker.Release;
        Map = new ConstantMap(this);

        _player = new PlayerObject(this)
        {
            Transform = new Transform
            {
                Position = new Vector2(22, 11.5f),
                Rotation = new Vector2(-1, 0),
            }
        };
        
        _objectCollection.Add(_player);
        
        _running = true;

        _clock.Restart();
        _lastFrameEndTime = 0;
        
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
            var frameStartTime = _clock.Elapsed.TotalMilliseconds;
            _objectCollection.Update((float) _lastFrameDeltaTime);
            _renderer.Render(
                _player.Camera.Transform.Position, 
                _player.Camera.Transform.Rotation,
                _player.Camera.Plane,
                Map.WorldMap,
                Map.Revision,
                Map.Sprites);
            FrameDelay();
            _lastFrameDeltaTime = _clock.Elapsed.TotalMilliseconds- frameStartTime;
            _window.RequestAnimationFrame(Frame);
        }
        catch (Exception ex)
        {
            Stop();

            Console.Error.WriteLine(
                $"[GAME] Frame failed: {ex}");
        }
    }

    private void FrameDelay()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds - _lastFrameEndTime;
        var diff = RendererSettings.TargetFrameInterval - elapsed;
            
        if (diff > 0)
        {
            Thread.Sleep((int) diff);
        }
            
        _lastFrameEndTime = _clock.Elapsed.TotalMilliseconds;
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;
        _clock.Stop();
        KeyTracker.Dispose();
        MouseTracker.Dispose();

        Console.WriteLine("[GAME] Stopped");
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
        
        _window.Dispose();
    }
}
