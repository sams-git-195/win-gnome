using WinGnome.Core.Geometry;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Reads the colour of a window's title bar from the screen, for windows whose caption WinGnome does not (or
/// cannot) recolour. The overlay is then painted in that colour so it blends in over the native buttons.
/// </summary>
/// <remarks>
/// GetPixel on the screen DC makes DWM read back the composed desktop, which costs a few milliseconds, so it is
/// only called after activation changes and drags end, never per frame. Mica and acrylic title bars have a
/// subtle gradient, so a slight seam can remain at the overlay's edge.
/// </remarks>
internal static class TitleBarSampler
{
    /// <summary>
    /// Samples the title bar just left of the native buttons.
    /// </summary>
    /// <returns>False when the point is covered by another window (the colour would be wrong) or GDI failed.</returns>
    public static bool TrySample(nint target, PixelRect buttons, double dpiScale, out HexColor color)
    {
        color = default;
        var (x, y) = CaptionButtonGeometry.TitleBarSamplePoint(buttons, dpiScale);
        var hit = NativeMethods.WindowFromPoint(new POINT { X = x, Y = y });
        if (hit == 0 || NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) != target)
        {
            return false;
        }

        var dc = NativeMethods.GetDC(0);
        if (dc == 0)
        {
            ThrottledLog.Warn("sample-dc", "GetDC(screen) failed; cannot sample title bar colours");
            return false;
        }

        try
        {
            var pixel = NativeMethods.GetPixel(dc, x, y);
            if (pixel == NativeMethods.CLR_INVALID)
            {
                ThrottledLog.Warn("sample-pixel", $"GetPixel({x}, {y}) failed while sampling the title bar of 0x{target:X}");
                return false;
            }

            color = TitleBarPalette.FromColorRef(pixel);
            return true;
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(0, dc);
        }
    }
}
