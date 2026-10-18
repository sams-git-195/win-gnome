using WinGnome.Core.Windows;

namespace WinGnome.Core.Dock;

/// <summary>One window listed in the dock's preview popover.</summary>
/// <param name="Target">What a close from the popover is checked against (captured when the popover was built).</param>
/// <param name="Title">The window title, or the app name when the window has none.</param>
/// <param name="IsMinimized">True when the window is minimised (its item says so when DWM has no picture).</param>
public sealed record DockPreviewItem(CloseTarget Target, string Title, bool IsMinimized)
{
    public nint Handle => Target.Handle;

    /// <summary>False for elevated windows: they get no close button (WinGnome never acts on them).</summary>
    public bool CanClose => !Target.IsElevated;
}

/// <summary>Which dock icons show a preview popover, and what it lists (spec 0024).</summary>
public static class DockPreviewContent
{
    /// <summary>Apps with fewer windows keep the plain tooltip instead.</summary>
    public const int MinWindows = 2;

    /// <summary>True when hovering <paramref name="app"/>'s icon opens the popover (and its tooltip is off).</summary>
    public static bool ShowsPreview(bool enabled, DockApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return enabled && app.Windows.Count >= MinWindows;
    }

    /// <summary>
    /// The app's windows in its (stable) order, each with the tracker's facts about it. Windows the tracker no longer
    /// lists have closed and are left out; a handle listed twice appears once.
    /// </summary>
    public static IReadOnlyList<DockPreviewItem> Build(DockApp app, IReadOnlyList<WindowInfo> tracked)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(tracked);

        var byHandle = new Dictionary<nint, WindowInfo>();
        foreach (var info in tracked)
        {
            byHandle.TryAdd(info.Handle, info);
        }

        var items = new List<DockPreviewItem>(app.Windows.Count);
        var seen = new HashSet<nint>();
        foreach (var hwnd in app.Windows)
        {
            if (seen.Add(hwnd) && byHandle.TryGetValue(hwnd, out var info))
            {
                var title = string.IsNullOrWhiteSpace(info.Title) ? app.Name : info.Title.Trim();
                items.Add(new DockPreviewItem(new CloseTarget(info.Handle, info.ProcessId, info.IsElevated), title, info.IsMinimized));
            }
        }

        return items;
    }

    /// <summary>
    /// True when the popover must be laid out and its thumbnails registered again: a window came, went or moved, or
    /// was minimised or restored (which changes whether DWM has a picture). A title change alone is updated in place.
    /// </summary>
    public static bool NeedsRebuild(IReadOnlyList<DockPreviewItem> shown, IReadOnlyList<DockPreviewItem> next)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(next);
        if (shown.Count != next.Count)
        {
            return true;
        }

        for (var i = 0; i < shown.Count; i++)
        {
            if (shown[i].Handle != next[i].Handle || shown[i].IsMinimized != next[i].IsMinimized)
            {
                return true;
            }
        }

        return false;
    }
}
