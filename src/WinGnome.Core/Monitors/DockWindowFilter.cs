using WinGnome.Core.Dock;

namespace WinGnome.Core.Monitors;

/// <summary>Which running windows a dock on one monitor shows ("only show windows on the same display").</summary>
public static class DockWindowFilter
{
    /// <param name="windows">Every running window.</param>
    /// <param name="monitorOf">The monitor key of a window handle (the app maps "unknown" to the primary's key).</param>
    /// <param name="key">The dock's monitor key.</param>
    /// <param name="isolate">When false every window is kept.</param>
    public static IReadOnlyList<RunningWindow> ForMonitor(
        IReadOnlyList<RunningWindow> windows, Func<nint, string> monitorOf, string key, bool isolate)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(monitorOf);
        return isolate
            ? windows.Where(w => string.Equals(monitorOf(w.Handle), key, StringComparison.OrdinalIgnoreCase)).ToList()
            : windows;
    }
}
