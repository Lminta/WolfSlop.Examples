using System.Diagnostics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Actors;
using WolfNET.Engine.Graphics;
using WolfNET.Engine.Audio;
using WolfNET.Engine.UI;
using WolfNET.Engine.Browser.Audio;
using WolfNET.Engine.Browser.Core;
using WolfNET.Engine.Browser.Graphics;
using WolfNET.Engine.Browser.Input;
using WolfNET.Engine.Browser.UI;

namespace RaceImperialis.Game;

/// <summary>Coordinates browser frames, scene loading, and renderer lifetime.</summary>
public sealed class Wolf : IDisposable
{
    public const string DefaultMapPath = "maps/pine-loop.json";

    private readonly ISceneRenderer _renderer;
    private readonly GameSceneLoader _loader;
    private readonly HttpClient _http;
    private readonly IFrameLoop _loop;
    private readonly BlazorJSRuntime _js;
    private readonly Stopwatch _clock = new();
    private readonly SemaphoreSlim _mapSwitchLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pendingLoad;
    private GameScene? _scene;
    private BrowserInput? _input;
    private MusicPlayer? _audio;
    private SfxSystem? _sfx;
    private BrowserUiHost? _ui;
    private ExampleHud? _hud;
    private TextureCatalog? _sceneTextures;
    private bool _disposed;
    private int _frameGeneration;
    private double _lastPoseNotification = double.NegativeInfinity;
    private double _lastDrivingNotification = double.NegativeInfinity;
    private readonly GameplaySettings _gameplay;
    private readonly SoundSettings _sound;
    private double _fpsSampleStart = double.NaN;
    private int _fpsFrameCount;
    private double _lastRaceNotification = double.NegativeInfinity;
    private RaceStatus? _lastRaceState;

    public IControlledActor? Player => _scene?.Player;
    public PlayerPose? OpponentPose => _scene?.Opponent is { } rival
        ? new(rival.Transform.Position, rival.Transform.Rotation) : null;
    public PlayerPose? SecondOpponentPose => _scene?.SecondOpponent is { } rival
        ? new(rival.Transform.Position, rival.Transform.Rotation) : null;
    public DrivingViewState? DrivingState => _scene?.DrivingState;
    public RaceStatus? RaceState => _scene?.RaceState;
    public KeyTracker KeyTracker => _input?.Keyboard ?? throw new InvalidOperationException("No scene is running.");
    public MouseTracker MouseTracker => _input?.Mouse ?? throw new InvalidOperationException("No scene is running.");
    public IMap Map => _scene?.Map ?? throw new InvalidOperationException("No scene is running.");
    public RendererSettings RendererSettings { get; }
    public string? CurrentMapPath { get; private set; }
    public RaceDifficulty Difficulty { get; private set; }
    public int? FramesPerSecond { get; private set; }
    public event Action<string>? MapSwitchRequested;
    public event Action<PlayerPose>? PlayerPoseChanged;
    public event Action<DrivingViewState>? DrivingStateChanged;
    public event Action<int?>? FramesPerSecondChanged;
    public event Action<RaceStatus>? RaceStateChanged;

    public Wolf(RendererSettings settings, ISceneRenderer renderer, GameSceneLoader loader, BlazorJSRuntime js,
        GameplaySettings gameplay, SoundSettings sound, HttpClient http)
    {
        settings.Validate();
        RendererSettings = settings;
        _renderer = renderer;
        _loader = loader;
        _http = http;
        _gameplay = gameplay;
        Difficulty = gameplay.Opponents.DefaultDifficulty;
        _sound = sound;
        _js = js;
        _loop = new BrowserFrameLoop(js, settings.TargetFPS);
    }

    public void AttachUi(BrowserUiHost ui)
    {
        if (_disposed) { ui.Dispose(); throw new ObjectDisposedException(nameof(Wolf)); }
        if (_ui != null) throw new InvalidOperationException("A UI host is already attached.");
        _ui = ui;
    }

    public Task StartAsync(HTMLCanvasElement canvas, CancellationToken cancellationToken = default) =>
        _scene != null ? Task.CompletedTask : SwitchMapAsync(canvas, DefaultMapPath, cancellationToken);

    public async Task SwitchMapAsync(HTMLCanvasElement canvas, string mapPath, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null, RaceDifficulty? difficulty = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = operation.Token;
        await _mapSwitchLock.WaitAsync(token);
        BrowserInput? input = null;
        GameScene? scene = null;
        ExampleHud? hud = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();
            _pendingLoad = operation;
            if (reportProgress != null) await reportProgress("Loading map and objects...");
            var loaded = await _loader.LoadAsync(mapPath, token);
            await _renderer.PrepareSceneAsync(loaded.Map.RendererMap, token, reportProgress, new(loaded.Character.AddTextures(loaded.Map.RendererMap.Textures)));
            hud = new(loaded.Map.Spawn.Controller == "car", loaded.Map.RendererMap.GetRaceMinimap());
            hud.RestartRequested += () => { if (CurrentMapPath is { } path) MapSwitchRequested?.Invoke(path); };
            _ui?.SetDocument(hud.Document);
            if (_ui != null) await _ui.PrepareImagesAsync(token);
            try
            {
                if (loaded.Map.Music is { Tracks.Length: > 0 } music && _sound.GetMusicVolume(music) > 0)
                    _audio = new MusicPlayer(new BrowserAudioBackend(canvas), music, _sound);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AUDIO] Music unavailable: {ex.Message}");
            }
            if (_sound.Volume > 0 && _sound.Sfx.Volume > 0)
            {
                if (reportProgress != null) await reportProgress("Loading sound effects...");
                try
                {
                    var effects = await BrowserSfxBackend<SfxCue>.CreateAsync(_http, _js, canvas, _sound.Sfx.Clips, token,
                        new SfxVariantSelector().Select, (cue, channels) => cue is SfxCue.MudSlow or SfxCue.MudMid or SfxCue.MudHigh
                            ? SfxClipLevels.MudGain(channels) : 1,
                        analyzeClip: cue => cue is SfxCue.MudSlow or SfxCue.MudMid or SfxCue.MudHigh);
                    var gate = loaded.Map.RendererMap.GetRace()?.StartFinish.Center;
                    _sfx = new SfxSystem(effects, _sound, loaded.Map.AmbientEmitters.Where(emitter => emitter.Cue == "crowd").Select(emitter => emitter.Position).ToArray(),
                        gate == null ? loaded.Map.SpawnPosition : new(gate[0], gate[1]));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Console.WriteLine($"[SFX] Effects unavailable: {ex.Message}"); }
            }
            if (reportProgress != null) await reportProgress("Entering scene...");
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            input = new BrowserInput(canvas, _js);
            var selectedDifficulty = difficulty ?? Difficulty;
            scene = new GameScene(loaded.Map, loaded.Character, input.Keyboard, input.Mouse, RendererSettings, _gameplay, selectedDifficulty);
            scene.SetPaused(!input.Mouse.IsLocked);
            _input = input;
            _scene = scene;
            _hud = hud;
            input = null;
            scene = null;
            hud = null;
            _sceneTextures = loaded.Map.RendererMap.Textures;
            CurrentMapPath = mapPath;
            Difficulty = selectedDifficulty;
            _clock.Restart();
            _lastPoseNotification = double.NegativeInfinity;
            _lastDrivingNotification = double.NegativeInfinity;
            _lastRaceNotification = double.NegativeInfinity;
            _lastRaceState = null;
            int generation = ++_frameGeneration;
            Console.WriteLine($"[GAME] Started. Target FPS: {RendererSettings.TargetFPS}");
            ScheduleFrame(generation);
        }
        catch
        {
            scene?.Dispose();
            input?.Dispose();
            hud?.Dispose();
            Stop();
            throw;
        }
        finally
        {
            _pendingLoad = null;
            _mapSwitchLock.Release();
        }
    }

    private void ScheduleFrame(int generation) => _loop.Start(delta => Frame(generation, delta));
    private bool IsCurrent(int generation) => !_disposed && _scene != null && generation == _frameGeneration;

    private void Frame(int generation, float deltaTime)
    {
        if (!IsCurrent(generation)) return;
        try
        {
            using var capture = _input!.Mouse.SampleForFrame();
            var wasPaused = _scene!.Paused;
            _scene.SetPaused(!_input!.Mouse.IsLocked);
            // Recapture resumes from the preserved pose, without spending the
            // fraction of this frame that elapsed before controls were active.
            _scene.Update(wasPaused != _scene.Paused ? 0 : deltaTime);
            var musicRace = _scene.PlayerRaceState;
            var musicVolume = 1f;
            if (musicRace is { Phase: RacePhase.Countdown } countdown)
            {
                var progress = countdown.CountdownProgress;
                // Smoothstep starts softly and reaches full volume with a flat
                // slope at GO. Simulation progress also freezes during pauses.
                musicVolume = _sound.CountdownVolumeScale(progress);
            }
            _audio?.SetPlaying(!_scene.Paused &&
                (musicRace == null || musicRace.Value.Phase is RacePhase.Countdown or RacePhase.Racing), musicVolume);
            _audio?.Update();
            if (_scene.PlayerRaceState is { Phase: RacePhase.Finished } && _input.Mouse.IsLocked)
            {
                _input.Keyboard.Reset();
                _input.Mouse.Release();
            }
            if (_scene.ConsumeMapRequest() is { } destination && destination != CurrentMapPath && MapSwitchRequested != null)
            {
                MapSwitchRequested.Invoke(destination);
                return;
            }
            if (!ReferenceEquals(_sceneTextures, _scene.Map.RendererMap.Textures))
            {
                _loop.Stop();
                _ = RefreshSceneAsync(generation);
                return;
            }
            var renderFrame = _scene.CaptureFrame();
            _sfx?.Update(_scene.CaptureAudioFrame(renderFrame, _input.Mouse.IsLocked && !_scene.Paused, deltaTime / 1000));
            _renderer.Render(renderFrame);
            ReportFrameRate();
            if (_ui != null && _hud != null)
            {
                var viewport = _ui.Viewport;
                _hud.Update(_scene, renderFrame, viewport, FramesPerSecond, deltaTime);
                _ui.Render(viewport.X, viewport.Y);
            }
            if (_scene.PlayerRaceState is { } race && RaceStateChanged != null &&
                (_clock.Elapsed.TotalMilliseconds - _lastRaceNotification >= 100 ||
                 _lastRaceState?.Phase != race.Phase || _lastRaceState?.CountdownNumber != race.CountdownNumber ||
                 _lastRaceState?.ShowGo != race.ShowGo))
            {
                _lastRaceNotification = _clock.Elapsed.TotalMilliseconds;
                _lastRaceState = race;
                RaceStateChanged.Invoke(_scene.RaceState!.Value);
            }
            if (_scene.DrivingState is { } driving && DrivingStateChanged != null &&
                _clock.Elapsed.TotalMilliseconds - _lastDrivingNotification >= 1000d / 30)
            {
                _lastDrivingNotification = _clock.Elapsed.TotalMilliseconds;
                DrivingStateChanged.Invoke(driving);
            }
            if (_scene.Map.RendererMap.GetRaceMinimap() != null && PlayerPoseChanged != null &&
                _clock.Elapsed.TotalMilliseconds - _lastPoseNotification >= 50)
            {
                _lastPoseNotification = _clock.Elapsed.TotalMilliseconds;
                PlayerPoseChanged.Invoke(_scene.PlayerPose);
            }
        }
        catch (Exception ex)
        {
            Stop();
            Console.Error.WriteLine($"[GAME] Frame failed: {ex}");
        }
    }

    private void ReportFrameRate()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (double.IsNaN(_fpsSampleStart)) { _fpsSampleStart = now; return; }
        _fpsFrameCount++;
        var elapsed = now - _fpsSampleStart;
        if (elapsed < 500) return;
        var fps = (int)Math.Round(_fpsFrameCount * 1000d / elapsed);
        _fpsSampleStart = now;
        _fpsFrameCount = 0;
        if (FramesPerSecond == fps) return;
        FramesPerSecond = fps;
        FramesPerSecondChanged?.Invoke(fps);
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
            await _renderer.PrepareSceneAsync(map, operation.Token, resources: new(_scene.Character.AddTextures(map.Textures)));
            if (!IsCurrent(generation)) return;
            _sceneTextures = map.Textures;
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
        _loop.Stop();
        _clock.Stop();
        _fpsSampleStart = double.NaN;
        _fpsFrameCount = 0;
        FramesPerSecond = null;
        FramesPerSecondChanged?.Invoke(null);
        _audio?.Dispose();
        _audio = null;
        _sfx?.Dispose();
        _sfx = null;
        _ui?.SetDocument(null);
        _hud?.Dispose(); _hud = null;
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
        _loop.Dispose();
        _ui?.Dispose(); _ui = null;
        // Pending operations still own linked tokens and release the semaphore in finally.
        _lifetime.Dispose();
    }
}
