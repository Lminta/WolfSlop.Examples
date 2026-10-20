using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.InputEvents;

/// <summary>Owns and connects the input subscriptions for one scene.</summary>
public sealed class BrowserInput : IDisposable
{
    public KeyTracker Keyboard { get; }
    public MouseTracker Mouse { get; }

    public BrowserInput(HTMLCanvasElement canvas, BlazorJSRuntime js)
    {
        Mouse = new MouseTracker(canvas, js);
        try
        {
            Keyboard = new KeyTracker(js, () => Mouse.IsLocked);
            Keyboard.EscapePressed += Mouse.Release;
            Mouse.PointerLockReleased += Keyboard.Reset;
        }
        catch
        {
            Mouse.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Keyboard.EscapePressed -= Mouse.Release;
        Mouse.PointerLockReleased -= Keyboard.Reset;
        Keyboard.Dispose();
        Mouse.Dispose();
    }
}
