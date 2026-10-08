using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Overview;

/// <summary>
/// Gives the overview a blurred view of the desktop behind it.
/// </summary>
/// <remarks>
/// DWM thumbnails are not drawn into layered windows, so the overview cannot use WPF's
/// <c>AllowsTransparency</c>. Instead it is an ordinary opaque window whose whole client area is turned
/// into DWM "glass" (DwmExtendFrameIntoClientArea with -1 margins) and given the Windows 11 acrylic
/// system backdrop; WPF then clears to transparent so the backdrop shows through. On systems without
/// system backdrops (Windows 10, Windows 11 before 22H2) a solid dark colour is used instead.
/// </remarks>
internal static class OverviewBackdrop
{
    private static readonly Color FallbackColor = Color.FromRgb(0x1E, 0x1E, 0x1E);

    /// <summary>Applies acrylic, or the solid fallback. Call once the window handle exists.</summary>
    /// <returns>True when the acrylic backdrop is active.</returns>
    public static bool Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != 0 && TryApplyAcrylic(hwnd))
        {
            window.Background = Brushes.Transparent;
            return true;
        }

        window.Background = new SolidColorBrush(FallbackColor);
        return false;
    }

    private static bool TryApplyAcrylic(nint hwnd)
    {
        var useDarkMode = 1;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        var backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        if (hr < 0)
        {
            Log.Info($"Overview: system backdrop unavailable (hr=0x{hr:X8}); using a solid background");
            return false;
        }

        var glass = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        hr = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, glass);
        if (hr < 0)
        {
            Log.Warn($"DwmExtendFrameIntoClientArea failed (hr=0x{hr:X8}); using a solid background");
            var none = 0;
            _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            return false;
        }

        // WPF normally clears its surface to opaque black; transparent lets the DWM backdrop through.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        return true;
    }
}
