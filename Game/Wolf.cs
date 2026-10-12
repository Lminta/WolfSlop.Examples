using System.Diagnostics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.InputEvents;
using WolfNET.Graphics;

namespace WolfNET.Game;

public sealed class Wolf : IDisposable
{
    private readonly WebGpuRenderer _renderer;
    private readonly RendererSettings _settings;
    private readonly Window _window;
    private readonly InputEventSystem _inputEvents;

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
        _settings = settings;
        _renderer = renderer;
        _inputEvents = inputEvents;

        _window = js.Get<Window>("window");
    }

    public void Start()
    {
        if (_running)
            return;

        _running = true;

        _clock.Restart();
        _lastFrameTime = 0;
        
        _window.OnKeyDown += OnKeyDown;
        _window.OnKeyUp += OnKeyUp;

        Console.WriteLine(
            $"[GAME] Started. Target FPS: {_settings.TargetFPS}");

        _window.RequestAnimationFrame(Frame);
    }

    private void Frame()
    {
        if (!_running)
            return;

        try
        {
            var now = _clock.Elapsed.TotalMilliseconds;
            var interval = 1000.0 / _settings.TargetFPS;
            var elapsed = now - _lastFrameTime;

            if (elapsed >= interval)
            {
                // Keep fractional timing remainder.
                _lastFrameTime = now - (elapsed % interval);

                _renderer.Render();
            }

            // Schedule exactly one more callback.
            _window.RequestAnimationFrame(Frame);
        }
        catch (Exception ex)
        {
            _running = false;

            Console.Error.WriteLine(
                $"[GAME] Frame failed: {ex}");
        }
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