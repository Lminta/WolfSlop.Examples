using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.InputEvents;

/// <summary>Owns and connects the input subscriptions for one scene.</summary>
public sealed class BrowserInput : IDisposable
{
    private readonly Window _window;
    public KeyTracker Keyboard { get; }
    public MouseTracker Mouse { get; }

    public BrowserInput(HTMLCanvasElement canvas, BlazorJSRuntime js)
    {
        _window = js.Get<Window>("window");
        Mouse = new MouseTracker(canvas, js);
        try
        {
            Keyboard = new KeyTracker(js, () => Mouse.IsLocked);
            Keyboard.EscapePressed += Mouse.Release;
            Mouse.PointerLockReleased += Keyboard.Reset;
            _window.OnBlur += OnBlur;
        }
        catch
        {
            Mouse.Dispose();
            _window.Dispose();
            throw;
        }
    }

    private void OnBlur(Event e) => Mouse.Release();

    public void Dispose()
    {
        _window.OnBlur -= OnBlur;
        Keyboard.EscapePressed -= Mouse.Release;
        Mouse.PointerLockReleased -= Keyboard.Reset;
        Keyboard.Dispose();
        Mouse.Dispose();
        _window.Dispose();
    }
}
