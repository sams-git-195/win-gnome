using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Dock;

/// <summary>
/// The most recent foreground window that does not belong to WinGnome. The dock's focus highlight, click
/// planning ("already focused, so minimise") and intellihide all reason about the user's app, so the dock's own
/// popups, the overview and the settings window must not count as "focused".
/// </summary>
internal sealed class ExternalForeground : IDisposable
{
    private readonly WindowTracker _tracker;

    public ExternalForeground(WindowTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        Handle = NativeMethods.IsOwnWindow(tracker.Foreground) ? 0 : tracker.Foreground;
        _tracker.ForegroundChanged += OnForegroundChanged;
    }

    /// <summary>The last non-WinGnome foreground window (0 when unknown).</summary>
    public nint Handle { get; private set; }

    /// <summary>Raised when <see cref="Handle"/> changes.</summary>
    public event EventHandler? Changed;

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        if (hwnd == 0 || hwnd == Handle || NativeMethods.IsOwnWindow(hwnd))
        {
            return;
        }

        Handle = hwnd;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _tracker.ForegroundChanged -= OnForegroundChanged;
}
