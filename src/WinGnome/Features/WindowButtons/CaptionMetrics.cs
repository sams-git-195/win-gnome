using System.Runtime.InteropServices;
using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Where a window's native caption buttons are on screen right now, in physical pixels. Reading it costs a
/// handful of Win32/DWM calls and no heap allocations, so it is safe to do for every location-change event
/// while a window is dragged.
/// </summary>
/// <param name="Frame">The visible frame (DWMWA_EXTENDED_FRAME_BOUNDS), without invisible resize borders.</param>
/// <param name="Buttons">
/// The visible native minimise/maximise/close buttons, clipped to <paramref name="Frame"/> and kept inside the
/// window border so an overlay covering them leaves the border visible.
/// </param>
/// <param name="Dpi">GetDpiForWindow of the target.</param>
/// <param name="Border">Visible window border thickness in physical pixels (0 when maximised).</param>
/// <param name="IsMaximized">IsZoomed of the target, read once so the border and the layout always agree.</param>
internal readonly record struct CaptionMetrics(PixelRect Frame, PixelRect Buttons, uint Dpi, int Border, bool IsMaximized)
{
    private static readonly int RectSize = Marshal.SizeOf<RECT>();

    /// <summary>Windows 11's border width, used when DWM cannot report it.</summary>
    private const int DefaultBorder = 1;

    /// <summary>Physical pixels per DIP for the target window.</summary>
    public double Scale => Dpi > 0 ? Dpi / 96.0 : 1.0;

    /// <summary>
    /// Measures <paramref name="hwnd"/>. Fails when the window is gone, has no caption buttons (fullscreen,
    /// borderless), or reports an empty rectangle because it draws its own buttons (Chrome, Electron).
    /// </summary>
    public static bool TryRead(nint hwnd, out CaptionMetrics metrics)
    {
        metrics = default;
        if (!NativeMethods.GetWindowRect(hwnd, out var window)
            || NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_BUTTON_BOUNDS, out RECT relative, RectSize) != 0)
        {
            return false;
        }

        // DWMWA_EXTENDED_FRAME_BOUNDS fails for windows without DWM frames; ToScreen then skips the clipping.
        var frame = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT extended, RectSize) == 0
            ? extended.ToPixelRect()
            : window.ToPixelRect();

        var buttons = CaptionButtonGeometry.ToScreen(relative.ToPixelRect(), window.ToPixelRect(), frame);
        if (buttons.IsEmpty)
        {
            return false;
        }

        var isMaximized = NativeMethods.IsZoomed(hwnd);
        var border = isMaximized ? 0 : ReadBorder(hwnd);
        metrics = new CaptionMetrics(
            frame, CaptionButtonGeometry.InsideBorder(buttons, frame, border), NativeMethods.GetDpiForWindow(hwnd), border, isMaximized);
        return true;
    }

    /// <summary>The border DWM draws around a restored window (maximised windows have none).</summary>
    private static int ReadBorder(nint hwnd)
    {
        // DWMWA_VISIBLE_FRAME_BORDER_THICKNESS needs Windows 11; older builds fail and get the 1 px default.
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, out int thickness, sizeof(int)) == 0
            && thickness >= 0
            ? thickness
            : DefaultBorder;
    }
}
