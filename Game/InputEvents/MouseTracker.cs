using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.InputEvents;

public sealed class MouseTracker : IDisposable
{
    private readonly HTMLCanvasElement _canvas;
    private readonly Document _document;
    private float _horizontalMovement;
    private bool _disposed;
    private bool _isLocked;

    public bool IsLocked => !_disposed && _isLocked && HasPointerLock();
    public event Action? PointerLockReleased;

    private bool HasPointerLock()
    {
        using var lockedElement = _document.PointerLockElement;
        return lockedElement != null && lockedElement.IsSameNode(_canvas);
    }

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
        var wasLocked = _isLocked;
        _isLocked = HasPointerLock();
        _horizontalMovement = 0;
        if (wasLocked && !_isLocked) PointerLockReleased?.Invoke();
    }

    private void OnPointerLockError(Event e)
    {
        _isLocked = false;
        _horizontalMovement = 0;
        Console.Error.WriteLine("[INPUT] Could not capture the mouse. Click the game canvas to try again.");
    }

    public float ConsumeHorizontalMovement()
    {
        // Escape can release the browser lock before pointerlockchange is delivered.
        // Drop queued deltas during that gap, including cursor restoration movement.
        var movement = IsLocked ? _horizontalMovement : 0;
        _horizontalMovement = 0;
        return movement;
    }

    public void Release()
    {
        var wasLocked = _isLocked;
        _isLocked = false;
        _horizontalMovement = 0;
        if (wasLocked) PointerLockReleased?.Invoke();
        if (wasLocked && HasPointerLock())
            _document.ExitPointerLock();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Release();
        PointerLockReleased = null;
        _canvas.OnClick -= OnCanvasClick;
        _document.OnMouseMove -= OnMouseMove;
        _document.OnPointerLockChange -= OnPointerLockChange;
        _document.OnPointerLockError -= OnPointerLockError;
        _document.Dispose();
    }
}
