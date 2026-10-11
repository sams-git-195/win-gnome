using System.Runtime.InteropServices;
using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// Moves other apps' maximised windows clear of a strip after <see cref="WorkAreaController"/> set a monitor's work
/// area without a broadcast, which is what Windows would have done had it broadcast (KI-103, see
/// <see cref="MaximisedWindowRefit"/>). The windows stay maximised and keep their restore position.
/// </summary>
internal static class MaximisedWindowRefitter
{
    /// <summary>Refits every visible maximised window on <paramref name="monitor"/>; returns how many were moved.</summary>
    public static int Refit(nint monitor, PixelRect oldWork, PixelRect newWork)
    {
        var ownProcess = (uint)Environment.ProcessId;
        var moved = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (TryRefit(hwnd, monitor, ownProcess, oldWork, newWork))
                {
                    moved++;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not check maximised window 0x{hwnd:X} after a work-area change", ex);
            }

            return true;
        }, 0);

        return moved;
    }

    private static bool TryRefit(nint hwnd, nint monitor, uint ownProcess, PixelRect oldWork, PixelRect newWork)
    {
        // Cheap checks first: most top-level windows are hidden or not maximised.
        if (!NativeMethods.IsWindowVisible(hwnd)
            || !NativeMethods.IsZoomed(hwnd)
            || NativeMethods.IsIconic(hwnd)
            || NativeMethods.IsCloaked(hwnd)
            || (NativeMethods.GetExStyle(hwnd) & NativeMethods.WS_EX_TOOLWINDOW) != 0
            // A window with neither caption nor sizing border (a borderless "fullscreen" game or player) is not
            // re-fitted by Windows' own broadcast either; leave it covering the monitor.
            || (NativeMethods.GetStyle(hwnd) & (NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME)) == 0
            || NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONULL) != monitor)
        {
            return false;
        }

        // Elevated windows are off limits (UIPI would refuse the move anyway); our own windows are not apps.
        var process = NativeMethods.GetProcessId(hwnd);
        if (process == ownProcess || NativeMethods.IsProcessElevated(process))
        {
            return false;
        }

        if (MaximisedWindowRefit.Refit(NativeMethods.GetWindowBounds(hwnd), oldWork, newWork) is not { } fitted)
        {
            return false;
        }

        // SWP_ASYNCWINDOWPOS: the window belongs to another thread; a synchronous SetWindowPos would block our UI
        // thread for as long as that app takes to respond (forever, if it is hung).
        const uint Flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_ASYNCWINDOWPOS;
        if (!NativeMethods.SetWindowPos(hwnd, 0, fitted.Left, fitted.Top, fitted.Width, fitted.Height, Flags))
        {
            Log.Warn($"Could not move maximised window 0x{hwnd:X} clear of the work area's new edge (error {Marshal.GetLastPInvokeError()})");
            return false;
        }

        return true;
    }
}
