using System.Diagnostics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player;
using WolfNET.Graphics;

namespace WolfNET.Game;

/// <summary>Coordinates browser frames, scene loading, and renderer lifetime.</summary>
public sealed class Wolf : IDisposable
{
    private readonly ISceneRenderer _renderer;
    private readonly GameSceneLoader _loader;
    private readonly Window _window;
    private readonly BlazorJSRuntime _js;
    private readonly Stopwatch _clock = new();
    private readonly FrameTiming _timing;
    private readonly SemaphoreSlim _mapSwitchLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pendingLoad;
    private GameScene? _scene;
    private BrowserInput? _input;
    private TextureCatalog? _sceneTextures;
    private bool _disposed;
    private int _frameGeneration;

    public PlayerObject? Player => _scene?.Player;
    public KeyTracker KeyTracker => _input?.Keyboard ?? throw new InvalidOperationException("No scene is running.");
    public MouseTracker MouseTracker => _input?.Mouse ?? throw new InvalidOperationException("No scene is running.");
    public IMap Map => _scene?.Map ?? throw new InvalidOperationException("No scene is running.");
    public RendererSettings RendererSettings { get; }
    public string? CurrentMapPath { get; private set; }
    public event Action<string>? MapSwitchRequested;

    public Wolf(RendererSettings settings, ISceneRenderer renderer, GameSceneLoader loader, BlazorJSRuntime js)
    {
        settings.Validate();
        RendererSettings = settings;
        _renderer = renderer;
        _loader = loader;
        _js = js;
        _timing = new FrameTiming(settings.TargetFPS);
        _window = js.Get<Window>("window");
    }

    public Task StartAsync(HTMLCanvasElement canvas, CancellationToken cancellationToken = default) =>
        _scene != null ? Task.CompletedTask : SwitchMapAsync(canvas, "maps/level1.json", cancellationToken);

    public async Task SwitchMapAsync(HTMLCanvasElement canvas, string mapPath, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = operation.Token;
        await _mapSwitchLock.WaitAsync(token);
        BrowserInput? input = null;
        GameScene? scene = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();
            _pendingLoad = operation;
            if (reportProgress != null) await reportProgress("Loading map and objects...");
            var loaded = await _loader.LoadAsync(mapPath, token);
            await _renderer.PrepareSceneAsync(loaded.Map.RendererMap, token, reportProgress, loaded.Character);
            if (reportProgress != null) await reportProgress("Entering scene...");
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            input = new BrowserInput(canvas, _js);
            scene = new GameScene(loaded.Map, loaded.Character, input.Keyboard, input.Mouse, RendererSettings);
            _input = input;
            _scene = scene;
            input = null;
            scene = null;
            _sceneTextures = loaded.Map.RendererMap.Textures;
            CurrentMapPath = mapPath;
            _clock.Restart();
            _timing.Reset();
            int generation = ++_frameGeneration;
            Console.WriteLine($"[GAME] Started. Target FPS: {RendererSettings.TargetFPS}");
            ScheduleFrame(generation);
        }
        catch
        {
            scene?.Dispose();
            input?.Dispose();
            Stop();
            throw;
        }
        finally
        {
            _pendingLoad = null;
            _mapSwitchLock.Release();
        }
    }

    private void ScheduleFrame(int generation) => _window.RequestAnimationFrame(() => Frame(generation));
    private bool IsCurrent(int generation) => !_disposed && _scene != null && generation == _frameGeneration;

    private void Frame(int generation)
    {
        if (!IsCurrent(generation)) return;
        try
        {
            // Return to the browser between frames so input and painting can run.
            if (!_timing.TryAdvance(_clock.Elapsed.TotalMilliseconds, out var deltaTime))
            {
                ScheduleFrame(generation);
                return;
            }
            _scene!.Update(deltaTime);
            if (_scene.ConsumeMapRequest() is { } destination && destination != CurrentMapPath && MapSwitchRequested != null)
            {
                MapSwitchRequested.Invoke(destination);
                return;
            }
            if (!ReferenceEquals(_sceneTextures, _scene.Map.RendererMap.Textures))
            {
                _ = RefreshSceneAsync(generation);
                return;
            }
            _renderer.Render(_scene.CaptureFrame());
            ScheduleFrame(generation);
        }
        catch (Exception ex)
        {
            Stop();
            Console.Error.WriteLine($"[GAME] Frame failed: {ex}");
        }
    }

    // Edited maps can add textures. Serialize uploads with map switches.
    private async Task RefreshSceneAsync(int generation)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        await _mapSwitchLock.WaitAsync();
        try
        {
            if (!IsCurrent(generation)) return;
            _pendingLoad = operation;
            var map = _scene!.Map.RendererMap;
            await _renderer.PrepareSceneAsync(map, operation.Token, character: _scene.Character);
            if (!IsCurrent(generation)) return;
            _sceneTextures = map.Textures;
            _timing.Reset(_clock.Elapsed.TotalMilliseconds);
            ScheduleFrame(generation);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrent(generation)) Stop();
            Console.Error.WriteLine($"[GAME] Scene update failed: {ex}");
        }
        finally
        {
            _pendingLoad = null;
            _mapSwitchLock.Release();
        }
    }

    public void Stop()
    {
        _pendingLoad?.Cancel();
        _frameGeneration++;
        _clock.Stop();
        _input?.Dispose();
        _scene?.Dispose();
        _input = null;
        _scene = null;
        _sceneTextures = null;
        CurrentMapPath = null;
        _renderer.ReleaseScene();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Stop();
        _window.Dispose();
        // Pending operations still own linked tokens and release the semaphore in finally.
        _lifetime.Dispose();
    }
}
