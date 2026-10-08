using System.Windows;
using System.Windows.Interop;
using WinGnome.Core.Geometry;

namespace WinGnome.Interop;

/// <summary>
/// Helpers that turn WPF windows into desktop-shell surfaces (bars, docks, overlays): hidden from
/// Alt+Tab, never stealing activation, and positioned in physical pixels.
/// </summary>
internal static class ShellSurface
{
    /// <summary>
    /// Applies WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE (optionally topmost) and makes mouse clicks
    /// not activate the window. Call from <c>SourceInitialized</c> or after <c>EnsureHandle</c>.
    /// </summary>
    public static void MakeNonActivating(Window window, bool topmost)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var ex = NativeMethods.GetExStyle(hwnd);
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);

        HwndSource.FromHwnd(hwnd)?.AddHook(SuppressActivationHook);

        if (topmost)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    /// <summary>Moves/resizes the window in physical pixels without activating it.</summary>
    public static void SetBounds(Window window, PixelRect bounds, bool topmost = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var flags = NativeMethods.SWP_NOACTIVATE | (topmost ? 0 : NativeMethods.SWP_NOZORDER);
        NativeMethods.SetWindowPos(hwnd, topmost ? NativeMethods.HWND_TOPMOST : 0,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, flags);
    }

    /// <summary>The DPI scale currently applied to the window (1.0 = 96 DPI).</summary>
    public static double GetScale(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd == 0 ? 1.0 : NativeMethods.GetWindowScale(hwnd);
    }

    /// <summary>Asks DWM for rounded (or square) corners on Windows 11. Ignored elsewhere.</summary>
    public static void SetCornerPreference(Window window, bool rounded)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var preference = rounded ? 2 /* DWMWCP_ROUND */ : 1 /* DWMWCP_DONOTROUND */;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    private static nint SuppressActivationHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return NativeMethods.MA_NOACTIVATE;
        }

        return 0;
    }
}
