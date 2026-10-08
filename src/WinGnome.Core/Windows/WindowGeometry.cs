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
}
