using WinGnome.Core.Shell;

namespace WinGnome.Core.Windows;

/// <summary>Decides which windows count as "real" application windows.</summary>
public static class WindowFilter
{
    /// <summary>Minimum width (physical pixels) of a window that receives traffic-light buttons.</summary>
    public const int MinDecoratedWidth = 120;

    /// <summary>Minimum height (physical pixels) of a window that receives traffic-light buttons.</summary>
    public const int MinDecoratedHeight = 60;

    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
    };

    /// <summary>
    /// The classic alt-tab rule: visible, not cloaked, has a title, and either asks to be an app window
    /// or is an un-owned, non-tool, activatable window. Shell windows are always excluded.
    /// </summary>
    public static bool IsTaskSwitcherWindow(WindowInfo w)
    {
        ArgumentNullException.ThrowIfNull(w);

        if (!w.IsVisible || w.IsCloaked || string.IsNullOrWhiteSpace(w.Title))
        {
            return false;
        }

        if (w.ClassName is not null && ExcludedClasses.Contains(w.ClassName))
        {
            return false;
        }

        return w.IsAppWindow || (!w.HasOwner && !w.IsToolWindow && !w.IsNoActivate);
    }

    /// <summary>
    /// True when WinGnome may draw traffic-light buttons over the window: a normal captioned window that is
    /// not minimised, not elevated, big enough, and whose process is not on the exclusion list.
    /// </summary>
    /// <param name="w">The window.</param>
    /// <param name="excludedProcessNames">Process names that keep their native buttons.</param>
    /// <param name="drawsOwnButtons">
    /// The window draws its own caption buttons (found by probing). Windows only draws native buttons for
    /// WS_SYSMENU windows, but apps with their own buttons (Electron) often drop WS_SYSMENU, so it is not required.
    /// </param>
    public static bool CanDecorate(WindowInfo w, IReadOnlyCollection<string> excludedProcessNames, bool drawsOwnButtons = false)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(excludedProcessNames);

        if (!IsTaskSwitcherWindow(w) || !w.HasCaption || (!w.HasSystemMenu && !drawsOwnButtons) || w.IsMinimized || w.IsElevated)
        {
            return false;
        }

        if (w.Bounds.Width < MinDecoratedWidth || w.Bounds.Height < MinDecoratedHeight)
        {
            return false;
        }

        var processName = PathText.FileNameWithoutExtension(w.ProcessPath);
        if (processName.Length == 0)
        {
            return true;
        }

        foreach (var excluded in excludedProcessNames)
        {
            if (string.IsNullOrWhiteSpace(excluded))
            {
                continue;
            }

            var name = excluded.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^4];
            }

            if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
