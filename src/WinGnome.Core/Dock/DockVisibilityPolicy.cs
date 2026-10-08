using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Dock;

/// <summary>Everything the dock's show/hide decision depends on.</summary>
/// <param name="IsPointerEngaged">The pointer is over the dock or at the reveal edge (including the short grace period after it leaves).</param>
/// <param name="IsInteracting">A context menu is open or a drag is in progress over the dock.</param>
/// <param name="IsFullScreenForeground">A full-screen app (game, video, presentation) is in the foreground.</param>
/// <param name="IsObstructed">The focused window overlaps the dock, or a maximised window is on the dock's monitor.</param>
public readonly record struct DockVisibilityInputs(
    bool IsPointerEngaged,
    bool IsInteracting,
    bool IsFullScreenForeground,
    bool IsObstructed);

/// <summary>Pure show/hide rules for the dock's visibility modes.</summary>
public static class DockVisibilityPolicy
{
    /// <summary>True when the dock should be on screen.</summary>
    public static bool ShouldShow(DockVisibility mode, DockVisibilityInputs inputs)
    {
        // Never pull a menu or a drop target out from under the user.
        if (inputs.IsInteracting)
        {
            return true;
        }

        if (inputs.IsFullScreenForeground)
        {
            return false;
        }

        return mode switch
        {
            DockVisibility.Intellihide => !inputs.IsObstructed || inputs.IsPointerEngaged,
            DockVisibility.Autohide => inputs.IsPointerEngaged,
            _ => true,
        };
    }

    /// <summary>
    /// True when the pointer has to be tracked by polling: while the dock is hidden (to notice the pointer
    /// reaching the edge) and while the pointer holds it open (to notice it leaving). "Always visible" never polls,
    /// and neither does a dock hidden behind a full-screen app, which the edge must not reveal.
    /// </summary>
    public static bool NeedsPointerPolling(DockVisibility mode, bool isShown, bool isPointerEngaged, bool isFullScreenForeground) =>
        mode != DockVisibility.AlwaysVisible && !isFullScreenForeground && (!isShown || isPointerEngaged);

    /// <summary>
    /// A window is full-screen when it covers the whole monitor without being maximised. (A maximised window
    /// covers the monitor too whenever nothing reserves screen space, but it still wants the dock.)
    /// </summary>
    public static bool IsFullScreen(PixelRect windowBounds, PixelRect monitor, bool isMaximized) =>
        !isMaximized && !monitor.IsEmpty
        && windowBounds.Left <= monitor.Left && windowBounds.Top <= monitor.Top
        && windowBounds.Right >= monitor.Right && windowBounds.Bottom >= monitor.Bottom;

    /// <summary>Intellihide obstruction: the focused window's visible bounds overlap the dock, or a window is maximised on its monitor.</summary>
    public static bool IsObstructed(PixelRect dock, PixelRect? focusedWindowBounds, bool anyMaximizedOnMonitor) =>
        anyMaximizedOnMonitor || (focusedWindowBounds is { } focused && focused.Intersects(dock));
}
