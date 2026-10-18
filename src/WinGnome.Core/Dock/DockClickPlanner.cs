using WinGnome.Core.Settings;

namespace WinGnome.Core.Dock;

/// <summary>What the app should do after a dock icon is clicked.</summary>
public enum DockClickKind { Launch, Activate, Minimize, ShowPreviews, None }

/// <summary>The planned action and, for Activate/Minimize, the window to act on (0 otherwise).</summary>
public readonly record struct DockClickResult(DockClickKind Kind, nint Window);

/// <summary>Pure decision logic for dock icon clicks.</summary>
public static class DockClickPlanner
{
    /// <summary>
    /// Plans a click. Activating goes to <see cref="DockApp.MostRecentWindow"/>, or to the first window when that is
    /// not one of the app's windows; <see cref="DockClickAction.Cycle"/> steps through <paramref name="app"/>.Windows
    /// in their given order, which the caller keeps stable so every window is visited in turn.
    /// <paramref name="lastActivated"/> is reserved for future cycling heuristics and is currently unused.
    /// </summary>
    public static DockClickResult Plan(DockApp app, nint foreground, DockClickAction action, nint lastActivated)
    {
        ArgumentNullException.ThrowIfNull(app);
        _ = lastActivated;

        if (!app.IsRunning)
        {
            return new DockClickResult(DockClickKind.Launch, 0);
        }

        var windows = app.Windows;
        var focused = app.IsFocused && foreground != 0 && IndexOf(windows, foreground) >= 0;

        switch (action)
        {
            case DockClickAction.FocusOrMinimize:
                return FocusOrMinimize(app, focused, foreground);

            case DockClickAction.Cycle:
                if (focused && windows.Count > 1)
                {
                    var index = IndexOf(windows, foreground);
                    return new DockClickResult(DockClickKind.Activate, windows[(index + 1) % windows.Count]);
                }

                return focused
                    ? new DockClickResult(DockClickKind.Minimize, foreground)
                    : new DockClickResult(DockClickKind.Activate, ActivationTarget(app));

            case DockClickAction.Previews:
                return windows.Count > 1
                    ? new DockClickResult(DockClickKind.ShowPreviews, 0)
                    : FocusOrMinimize(app, focused, foreground);

            default:
                return new DockClickResult(DockClickKind.None, 0);
        }
    }

    private static DockClickResult FocusOrMinimize(DockApp app, bool focused, nint foreground) =>
        focused
            ? new DockClickResult(DockClickKind.Minimize, foreground)
            : new DockClickResult(DockClickKind.Activate, ActivationTarget(app));

    /// <summary>The most recent window, unless it is unknown or no longer listed (then the first window).</summary>
    private static nint ActivationTarget(DockApp app) =>
        IndexOf(app.Windows, app.MostRecentWindow) >= 0 ? app.MostRecentWindow : app.Windows[0];

    private static int IndexOf(IReadOnlyList<nint> windows, nint handle)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (windows[i] == handle)
            {
                return i;
            }
        }

        return -1;
    }
}
