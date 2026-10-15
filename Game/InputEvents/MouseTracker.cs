using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.InputEvents;

public sealed class MouseTracker : IDisposable
{
    private readonly HTMLCanvasElement _canvas;
    private readonly Document _document;
    private float _horizontalMovement;
    private bool _disposed;

    public bool IsLocked { get; private set; }

    public MouseTracker(HTMLCanvasElement canvas, BlazorJSRuntime js)
    {
        _canvas = canvas;
        _document = js.Get<Document>("document");
        _canvas.OnClick += OnCanvasClick;
        _document.OnMouseMove += OnMouseMove;
        _document.OnPointerLockChange += OnPointerLockChange;
        _document.OnPointerLockError += OnPointerLockError;
    }

    private void OnCanvasClick(MouseEvent e)
    {
        if (IsLocked)
            return;

        // Request synchronously inside the click to preserve user activation.
        _canvas.Focus();
        _canvas.RequestPointerLock();
    }

    private void OnMouseMove(MouseEvent e)
    {
        if (IsLocked)
            _horizontalMovement += (float)e.MovementX;
    }

    private void OnPointerLockChange(Event e)
    {
        using var lockedElement = _document.PointerLockElement;
        IsLocked = lockedElement != null && lockedElement.IsSameNode(_canvas);
        _horizontalMovement = 0;
    }

    private void OnPointerLockError(Event e)
    {
        IsLocked = false;
        _horizontalMovement = 0;
        Console.Error.WriteLine("[INPUT] Could not capture the mouse. Click the game canvas to try again.");
    }

    public float ConsumeHorizontalMovement()
    {
        var movement = _horizontalMovement;
        _horizontalMovement = 0;
        return movement;
    }

    public void Release()
    {
        if (IsLocked)
            _document.ExitPointerLock();

        IsLocked = false;
        _horizontalMovement = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Release();
        _canvas.OnClick -= OnCanvasClick;
        _document.OnMouseMove -= OnMouseMove;
        _document.OnPointerLockChange -= OnPointerLockChange;
        _document.OnPointerLockError -= OnPointerLockError;
        _document.Dispose();
    }
}
