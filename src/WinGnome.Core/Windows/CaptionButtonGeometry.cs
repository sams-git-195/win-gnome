using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>Screen-space maths for a window's native caption buttons, as reported by DWM.</summary>
public static class CaptionButtonGeometry
{
    /// <summary>Distance in DIPs between the native buttons and the point sampled for the title bar colour.</summary>
    public const double SampleInset = 3;

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
