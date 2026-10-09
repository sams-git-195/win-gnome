namespace WinGnome.Core.Monitors;

/// <summary>Where a task-switcher window is and whether it is minimised.</summary>
public readonly record struct TrackedWindow(string MonitorKey, bool IsMinimized);

/// <summary>
/// The "focused app" of each monitor: the most recently focused task-switcher window that is still on that monitor
/// and not minimised. One recency order covers all windows, so a window moved to another monitor takes its recency
/// with it. Pure; the app supplies monitor keys. Not thread-safe: use from one thread.
/// </summary>
public sealed class MonitorFocusTracker
{
    private readonly List<nint> _recent = [];
    private readonly Dictionary<nint, TrackedWindow> _windows = [];

    /// <summary>A window came to the foreground on monitor <paramref name="key"/>.</summary>
    public void OnForeground(nint hwnd, string key)
    {
        if (hwnd == 0 || string.IsNullOrEmpty(key))
        {
            return;
        }

        _recent.Remove(hwnd);
        _recent.Insert(0, hwnd);
        _windows[hwnd] = _windows.TryGetValue(hwnd, out var known) ? known with { MonitorKey = key } : new TrackedWindow(key, false);
    }

    /// <summary>
    /// Replaces the window table: every task-switcher window with its monitor and minimised state. Windows not in the
    /// table are forgotten, so a new window that reuses a closed one's handle starts with no recency.
    /// </summary>
    public void OnWindowsChanged(IReadOnlyDictionary<nint, TrackedWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows.Clear();
        foreach (var (hwnd, window) in windows)
        {
            _windows[hwnd] = window;
        }

        _recent.RemoveAll(hwnd => !_windows.ContainsKey(hwnd));
    }

    /// <summary>Forgets windows on monitors that no longer exist until the next table says where they went.</summary>
    public void OnLayoutChanged(MonitorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var hwnd in _windows.Where(w => layout.Find(w.Value.MonitorKey) is null).Select(w => w.Key).ToList())
        {
            _windows.Remove(hwnd);
        }
    }

    /// <summary>The focused window of monitor <paramref name="key"/>, or 0 when none (the desktop).</summary>
    public nint Current(string key)
    {
        foreach (var hwnd in _recent)
        {
            if (_windows.TryGetValue(hwnd, out var window) && !window.IsMinimized
                && string.Equals(window.MonitorKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return hwnd;
            }
        }

        return 0;
    }
}
