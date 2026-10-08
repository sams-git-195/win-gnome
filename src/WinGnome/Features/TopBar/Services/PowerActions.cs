using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Services;

/// <summary>Session and power actions of the system menu. Callers are responsible for asking for confirmation.</summary>
internal static class PowerActions
{
    public static void Lock()
    {
        if (!NativeMethods.LockWorkStation())
        {
            Log.Warn($"LockWorkStation failed (error {Marshal.GetLastPInvokeError()})");
        }
    }

    /// <summary>Sleeps the machine. Modern Standby (S0) devices reject SetSuspendState, so fall back to switching the display off, which enters standby there.</summary>
    public static void Suspend()
    {
        if (NativeMethods.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
        {
            return;
        }

        Log.Warn($"SetSuspendState failed (error {Marshal.GetLastPInvokeError()}); turning the display off instead");
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SYSCOMMAND,
            NativeMethods.SC_MONITORPOWER, NativeMethods.MONITOR_POWER_OFF);
    }

    /// <summary>
    /// Restart and shut down go through shutdown.exe, which holds the shutdown privilege itself; calling
    /// ExitWindowsEx directly would first need SE_SHUTDOWN_NAME enabled on our token.
    /// </summary>
    public static void Restart() => RunShutdown("/r /t 0");

    public static void ShutDown() => RunShutdown("/s /t 0");

    public static void SignOut()
    {
        if (!NativeMethods.ExitWindowsEx(NativeMethods.EWX_LOGOFF, NativeMethods.SHTDN_REASON_FLAG_PLANNED))
        {
            Log.Warn($"ExitWindowsEx(EWX_LOGOFF) failed (error {Marshal.GetLastPInvokeError()})");
        }
    }

    private static void RunShutdown(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn($"shutdown.exe {arguments} failed", ex);
        }
    }
}
