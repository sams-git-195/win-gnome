using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>Geometry decisions about other applications' windows (centring, full-screen detection).</summary>
public static class WindowGeometry
{
    /// <summary>
    /// The new top-left for SetWindowPos that centres a window's <em>visible</em> frame in
    /// <paramref name="workArea"/>. Windows 10/11 frames include invisible resize borders, so the visible
    /// bounds (DWMWA_EXTENDED_FRAME_BOUNDS) are centred and the same offset is applied to the window rectangle.
    /// </summary>
    /// <param name="windowRect">GetWindowRect result (includes invisible borders).</param>
    /// <param name="visibleBounds">Extended frame bounds (what the user sees).</param>
    /// <param name="workArea">Monitor work area to centre in.</param>
    public static (int X, int Y) CenteredOrigin(PixelRect windowRect, PixelRect visibleBounds, PixelRect workArea)
    {
        var visible = visibleBounds.IsEmpty ? windowRect : visibleBounds;
        var target = visible.CenteredIn(workArea);
        return (windowRect.Left + (target.Left - visible.Left), windowRect.Top + (target.Top - visible.Top));
    }

    /// <summary>
    /// True when a window covers its whole monitor (a full-screen game, video or presentation), so the
    /// shell should stay out of the way.
    /// </summary>
    public static bool IsFullScreen(PixelRect windowBounds, PixelRect monitor) =>
        !monitor.IsEmpty
        && windowBounds.Left <= monitor.Left
        && windowBounds.Top <= monitor.Top
        && windowBounds.Right >= monitor.Right
        && windowBounds.Bottom >= monitor.Bottom;

    /// <summary>
    /// Like <see cref="IsFullScreen(PixelRect, PixelRect)"/>, but a maximised window with a title bar never
    /// counts: when the work area fills the whole monitor (taskbar hidden or auto-hidden, no top bar) such a
    /// window covers the monitor too, invisible borders included, yet it is an ordinary window.
    /// </summary>
    public static bool IsFullScreenApp(PixelRect windowBounds, PixelRect monitor, bool isMaximized, bool hasCaption) =>
        !(isMaximized && hasCaption) && IsFullScreen(windowBounds, monitor);
}
