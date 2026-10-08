using System.Runtime.InteropServices;
using WinGnome.Core.Windows;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>Outcome of asking another app's window to run a caption command.</summary>
internal enum CaptionCommandResult { Sent, AccessDenied, Failed }

/// <summary>
/// Sends the same WM_SYSCOMMAND a native caption button would. Unlike <see cref="WindowActivator"/>, this reports
/// failures, because an access-denied post means UIPI isolates the window (it runs at a higher integrity level)
/// and the overlay must be removed rather than offering buttons that do nothing.
/// </summary>
internal static class CaptionCommands
{
    public static CaptionCommandResult Invoke(nint target, CaptionButtonKind kind)
    {
        var command = kind switch
        {
            CaptionButtonKind.Close => NativeMethods.SC_CLOSE,
            CaptionButtonKind.Minimize => NativeMethods.SC_MINIMIZE,
            _ => NativeMethods.IsZoomed(target) ? NativeMethods.SC_RESTORE : NativeMethods.SC_MAXIMIZE,
        };

        // Posted rather than sent: a hung or slow app must never block WinGnome's UI thread.
        if (NativeMethods.PostMessage(target, NativeMethods.WM_SYSCOMMAND, command, 0))
        {
            return CaptionCommandResult.Sent;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == NativeMethods.ERROR_ACCESS_DENIED)
        {
            return CaptionCommandResult.AccessDenied;
        }

        ThrottledLog.Warn("caption-command", $"PostMessage(WM_SYSCOMMAND {kind}) to 0x{target:X} failed: error {error}");
        return CaptionCommandResult.Failed;
    }
}
