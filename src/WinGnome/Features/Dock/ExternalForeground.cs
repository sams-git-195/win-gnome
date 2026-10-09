using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Dock;

/// <summary>
/// The most recent foreground window that does not belong to WinGnome. The dock's focus highlight, click
/// planning ("already focused, so minimise") and intellihide all reason about the user's app, so the dock's own
/// popups, the overview and the settings window must not count as "focused". A minimised window does not count
/// either: Windows often leaves it as the foreground window when nothing else takes over.
/// </summary>
internal sealed class ExternalForeground : IDisposable
{
    private readonly WindowTracker _tracker;
    private bool _disposed;

    public ExternalForeground(WindowTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        Handle = NativeMethods.IsOwnWindow(tracker.Foreground) ? 0 : tracker.Foreground;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _tracker.RawWindowEvent += OnRawWindowEvent;
    }

    /// <summary>The last non-WinGnome foreground window (0 when unknown or when it was minimised).</summary>
    public nint Handle { get; private set; }

    /// <summary>Raised when <see cref="Handle"/> changes.</summary>
    public event EventHandler? Changed;

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        if (hwnd == 0 || NativeMethods.IsOwnWindow(hwnd))
        {
            return;
        }

        SetHandle(hwnd);
    }

    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        if (eventType == WinEventHook.EVENT_SYSTEM_MINIMIZESTART && hwnd == Handle)
        {
            SetHandle(0);
        }
        else if (eventType == WinEventHook.EVENT_SYSTEM_MINIMIZEEND && hwnd == _tracker.Foreground && hwnd != 0
                 && !NativeMethods.IsOwnWindow(hwnd))
        {
            // A window restored while it is still the foreground window gets no foreground event.
            SetHandle(hwnd);
        }
    }

    private void SetHandle(nint hwnd)
    {
        if (hwnd == Handle)
        {
            return;
        }

        Handle = hwnd;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tracker.RawWindowEvent -= OnRawWindowEvent;
        _tracker.ForegroundChanged -= OnForegroundChanged;
    }
}
