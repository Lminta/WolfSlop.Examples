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
    private readonly HttpClient _http;
    
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
    private int _frameGeneration;
    private readonly SemaphoreSlim _mapSwitchLock = new(1, 1);
    public string? CurrentMapPath { get; private set; }

    public Wolf(
        RendererSettings settings,
        WebGpuRenderer renderer,
        InputEventSystem inputEvents,
        HttpClient http,
        BlazorJSRuntime js)
    {
        RendererSettings = settings;
        _renderer = renderer;
        _inputEvents = inputEvents;
        _js = js;
        _http = http;

        _window = js.Get<Window>("window");
    }

    public Task StartAsync(HTMLCanvasElement canvas, CancellationToken cancellationToken = default) =>
        _running ? Task.CompletedTask : SwitchMapAsync(canvas, "maps/level1.json", cancellationToken);

    public async Task SwitchMapAsync(HTMLCanvasElement canvas, string mapPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _mapSwitchLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();
            // Input and old frames stay stopped until the selected scene is ready.
            var map = await GameMap.LoadAsync(_http, mapPath,
                RendererSettings.MaxMapWidth, RendererSettings.MaxMapHeight, cancellationToken);
            await _renderer.PrepareSceneAsync(map.RendererMap, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            _objectCollection = new ObjectCollection();
            MouseTracker = new MouseTracker(canvas, _js);
            KeyTracker = new KeyTracker(_inputEvents, _js);
            KeyTracker.EscapePressed += MouseTracker.Release;
            Map = map;

            _player = new PlayerObject(this)
            {
                Transform = new Transform
                {
                    Position = map.PlayerStartPosition,
                    Rotation = map.PlayerStartDirection,
                }
            };
            _player.Camera.Plane = new Vector2(map.PlayerStartDirection.Y, -map.PlayerStartDirection.X) *
                                   _player.Camera.Plane.Length();

            _objectCollection.Add(_player);

            _running = true;

            _clock.Restart();
            _lastFrameEndTime = 0;
            _lastFrameDeltaTime = 0;
            CurrentMapPath = mapPath;
            int generation = ++_frameGeneration;

            Console.WriteLine(
                $"[GAME] Started. Target FPS: {RendererSettings.TargetFPS}");

            _window.RequestAnimationFrame(() => Frame(generation));
        }
        finally
        {
            _mapSwitchLock.Release();
        }
    }

    private void Frame(int generation)
    {
        if (!_running || generation != _frameGeneration)
            return;

        try
        {
            var frameStartTime = _clock.Elapsed.TotalMilliseconds;
            _objectCollection.Update((float) _lastFrameDeltaTime);
            _renderer.Render(
                _player.Camera.Transform.Position, 
                _player.Camera.Transform.Rotation,
                _player.Camera.Plane,
                Map.RendererMap);
            FrameDelay();
            _lastFrameDeltaTime = _clock.Elapsed.TotalMilliseconds- frameStartTime;
            _window.RequestAnimationFrame(() => Frame(generation));
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
        _frameGeneration++;
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
