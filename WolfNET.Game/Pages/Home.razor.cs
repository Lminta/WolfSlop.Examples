using Microsoft.AspNetCore.Components;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game.InputEvents;
using WolfNET.Graphics;

namespace WolfNET.Pages;

public partial class Home
{
    [SupplyParameterFromQuery(Name = "map")]
    public string? RequestedMap { get; set; }

    private ElementReference _canvas;
    private HTMLCanvasElement? _canvasElement;
    private readonly CancellationTokenSource _lifetime = new();
    private const string GlassworksMap = "maps/glassworks.json";
    private const string OriginalMap = "maps/level1.json";
    private const string ShowcaseMap = "maps/showcase.json";
    private const string PineLoopMap = "maps/pine-loop.json";
    private string? _selectedMap;
    private bool _loading = true;
    private string _loadingMessage = "Loading shaders…";
    private bool _disposed;
    private bool _errorPresent => _error != null;

    private string? _error;
    private MouseTracker? _observedMouse;
    private long _lastLoadingProgress;

    private async Task ShowLoadingProgressAsync(string message)
    {
        _lifetime.Token.ThrowIfCancellationRequested();
        // Counter updates must not impose a browser timer delay for every texture
        // layer. Stage transitions still paint immediately.
        var now = Environment.TickCount64;
        if (message.Contains('(') && now - _lastLoadingProgress < 75) return;
        _lastLoadingProgress = now;
        _loadingMessage = message;
        await InvokeAsync(StateHasChanged);
        // Allow the browser to paint the message before the next loading step.
        await Task.Delay(1, _lifetime.Token);
    }

    private void ObserveMouse()
    {
        if (_observedMouse != null) _observedMouse.CaptureStateChanged -= OnCaptureStateChanged;
        _observedMouse = Wolf.MouseTracker;
        _observedMouse.CaptureStateChanged += OnCaptureStateChanged;
    }

    private void OnCaptureStateChanged()
    {
        if (!_disposed) _ = InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        Console.WriteLine("[APP] Component initialized");
        Wolf.MapSwitchRequested += OnMapSwitchRequested;

        try
        {
            Console.WriteLine("[APP] Loading shader");

            var shaders = await ShaderSources.LoadAsync(Http, _lifetime.Token);
            _canvasElement = new HTMLCanvasElement(_canvas);
            await ShowLoadingProgressAsync("Starting graphics...");
            await Renderer.InitializeAsync(_canvasElement, shaders, _lifetime.Token);

            var initialMap = ShowcaseMap;
            if (!string.IsNullOrWhiteSpace(RequestedMap))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(RequestedMap, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$"))
                    throw new InvalidDataException("Invalid map name in URL.");
                initialMap = $"maps/{RequestedMap}.json";
            }
            await Wolf.SwitchMapAsync(_canvasElement, initialMap, _lifetime.Token, ShowLoadingProgressAsync);
            ObserveMouse();
            _selectedMap = initialMap;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _error = ex.ToString();

            Console.Error.WriteLine(
                $"[APP] Initialization failed: {ex}");

            if (!_disposed) StateHasChanged();
        }
        finally
        {
            _loading = false;
            if (!_disposed) StateHasChanged();
        }
    }

    private async Task SelectMapAsync(string mapPath)
    {
        if (_loading || _disposed || _canvasElement == null) return;
        _loading = true;
        _error = null;
        try
        {
            await Wolf.SwitchMapAsync(_canvasElement, mapPath, _lifetime.Token, ShowLoadingProgressAsync);
            ObserveMouse();
            _selectedMap = mapPath;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _error = $"Could not load map: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private Task RestartRaceAsync() => Wolf.CurrentMapPath is { } path
        ? SelectMapAsync(path) : Task.CompletedTask;

    private void OnMapSwitchRequested(string mapPath)
    {
        if (!_disposed) _ = InvokeAsync(async () =>
        {
            await SelectMapAsync(mapPath);
            if (!_disposed) StateHasChanged();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Wolf.MapSwitchRequested -= OnMapSwitchRequested;
        if (_observedMouse != null) _observedMouse.CaptureStateChanged -= OnCaptureStateChanged;
        _lifetime.Cancel();
        Wolf.Stop();
        _canvasElement?.Dispose();
        Console.WriteLine("[APP] Disposing");
    }
}
