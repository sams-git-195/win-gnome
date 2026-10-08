using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>Screen-space maths for a window's native caption buttons, as reported by DWM.</summary>
public static class CaptionButtonGeometry
{
    /// <summary>Distance in DIPs between the native buttons and the point sampled for the title bar colour.</summary>
    public const double SampleInset = 3;

    /// <summary>Corner radius in DIPs of a restored (not maximised) top-level window on Windows 11.</summary>
    public const double WindowCornerRadius = 8;

    /// <summary>
    /// The Windows 11 window corner radius in physical pixels for a window at <paramref name="dpiScale"/>
    /// (physical pixels per DIP). Invalid scales count as 100 %.
    /// </summary>
    public static int WindowCornerRadiusPixels(double dpiScale)
    {
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        return Math.Max(1, (int)Math.Round(WindowCornerRadius * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The native buttons rectangle without the window border: DWM's caption-button rectangle includes the
    /// frame's outermost pixels, where Windows 11 draws its 1 px (grey or accent) border. An overlay that covers
    /// them cuts the border off at the buttons, so drop <paramref name="border"/> pixels on the top and right
    /// wherever the buttons touch those frame edges.
    /// </summary>
    /// <param name="buttonsScreen">The visible native buttons in screen pixels.</param>
    /// <param name="visibleFrame">DWMWA_EXTENDED_FRAME_BOUNDS of the window.</param>
    /// <param name="border">Border thickness in physical pixels (0 for maximised windows, which have none).</param>
    public static PixelRect InsideBorder(PixelRect buttonsScreen, PixelRect visibleFrame, int border)
    {
        if (buttonsScreen.IsEmpty || border <= 0)
        {
            return buttonsScreen;
        }

        var top = buttonsScreen.Top <= visibleFrame.Top ? visibleFrame.Top + border : buttonsScreen.Top;
        var right = buttonsScreen.Right >= visibleFrame.Right ? visibleFrame.Right - border : buttonsScreen.Right;
        return new PixelRect(buttonsScreen.Left, top, right, buttonsScreen.Bottom);
    }

    /// <summary>The radius of the border's inner edge at a window corner of radius <paramref name="outerRadius"/>.</summary>
    public static int InnerCornerRadius(int outerRadius, int border) => Math.Max(0, outerRadius - Math.Max(0, border));

    /// <summary>
    /// Converts DWMWA_CAPTION_BUTTON_BOUNDS (relative to the window rectangle) to screen pixels, clipped to the
    /// visible frame. Maximised windows extend past the monitor by their invisible resize border, and DWM's
    /// rectangle includes that off-screen strip; clipping keeps the overlay on the monitor and vertically
    /// centred on what the user actually sees.
    /// </summary>
    /// <param name="windowRelativeButtons">The native buttons relative to <paramref name="windowRect"/>'s top-left.</param>
    /// <param name="windowRect">GetWindowRect of the window (includes invisible resize borders).</param>
    /// <param name="visibleFrame">DWMWA_EXTENDED_FRAME_BOUNDS, or empty when unknown (no clipping).</param>
    /// <returns>The visible native buttons in screen pixels, or an empty rectangle.</returns>
    public static PixelRect ToScreen(PixelRect windowRelativeButtons, PixelRect windowRect, PixelRect visibleFrame)
    {
        if (windowRelativeButtons.IsEmpty)
        {
            return default;
        }

        var screen = windowRelativeButtons.Offset(windowRect.Left, windowRect.Top);
        return visibleFrame.IsEmpty ? screen : screen.Intersect(visibleFrame);
    }

    /// <summary>
    /// True when the window's client area reaches up into the caption-button band, which means the app paints its
    /// own title bar (tabs in the title bar, Mica, XAML islands). DWM caption colours are invisible there, so the
    /// title bar colour has to be sampled instead.
    /// </summary>
    /// <param name="clientTopScreen">Screen y of the client area's top edge (ClientToScreen of 0,0).</param>
    /// <param name="buttonsScreen">The visible native buttons in screen pixels.</param>
    public static bool ClientCoversCaption(int clientTopScreen, PixelRect buttonsScreen) =>
        clientTopScreen < buttonsScreen.Bottom;

    /// <summary>
    /// The screen pixel just left of the native buttons, vertically centred, where the plain title bar
    /// background is most likely visible (no title text, no buttons).
    /// </summary>
    public static (int X, int Y) TitleBarSamplePoint(PixelRect buttonsScreen, double dpiScale)
    {
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        var inset = Math.Max(1, (int)Math.Round(SampleInset * scale, MidpointRounding.AwayFromZero));
        return (buttonsScreen.Left - inset, buttonsScreen.CenterY);
    }
}
