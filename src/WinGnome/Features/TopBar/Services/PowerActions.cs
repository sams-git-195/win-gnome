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
    /// ExitWindowsEx directly would first need SE_SHUTDOWN_NAME enabled on our token. With
    /// <paramref name="installUpdates"/> they use InitiateShutdown instead, because shutdown.exe can't ask Windows to
    /// install pending updates first; if that fails they fall back to shutdown.exe (without installing).
    /// </summary>
    public static void Restart(bool installUpdates = false)
    {
        if (!installUpdates || !TryInitiateShutdown(NativeMethods.SHUTDOWN_RESTART))
        {
            RunShutdown("/r /t 0");
        }
    }

    public static void ShutDown(bool installUpdates = false)
    {
        if (!installUpdates || !TryInitiateShutdown(NativeMethods.SHUTDOWN_POWEROFF))
        {
            RunShutdown("/s /t 0");
        }
    }

    public static void SignOut()
    {
        if (!NativeMethods.ExitWindowsEx(NativeMethods.EWX_LOGOFF, NativeMethods.SHTDN_REASON_FLAG_PLANNED))
        {
            Log.Warn($"ExitWindowsEx(EWX_LOGOFF) failed (error {Marshal.GetLastPInvokeError()})");
        }
    }

    /// <summary>Restarts or powers off with SHUTDOWN_INSTALL_UPDATES. Returns false (after logging) if it didn't start.</summary>
    private static bool TryInitiateShutdown(uint action)
    {
        if (!EnableShutdownPrivilege())
        {
            return false;
        }

        const uint reason = NativeMethods.SHTDN_REASON_FLAG_PLANNED | NativeMethods.SHTDN_REASON_MAJOR_OPERATINGSYSTEM
            | NativeMethods.SHTDN_REASON_MINOR_UPGRADE;
        var error = NativeMethods.InitiateShutdown(null, null, 0, action | NativeMethods.SHUTDOWN_INSTALL_UPDATES, reason);
        if (error != 0)
        {
            Log.Warn($"InitiateShutdown(0x{action:X}, install updates) failed (error {error}); using shutdown.exe");
            return false;
        }

        return true;
    }

    /// <summary>Standard users hold SeShutdownPrivilege but it starts disabled on the token. It stays enabled: the machine is going down.</summary>
    private static bool EnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var token))
        {
            Log.Warn($"OpenProcessToken failed (error {Marshal.GetLastPInvokeError()})");
            return false;
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, NativeMethods.SE_SHUTDOWN_NAME, out var luid))
            {
                Log.Warn($"LookupPrivilegeValue failed (error {Marshal.GetLastPInvokeError()})");
                return false;
            }

            var enable = new TOKEN_PRIVILEGES_ONE { PrivilegeCount = 1, Luid = luid, Attributes = NativeMethods.SE_PRIVILEGE_ENABLED };

            // AdjustTokenPrivileges reports a privilege the token lacks through the last error, not its result.
            if (!NativeMethods.AdjustTokenPrivileges(token, false, enable, (uint)Marshal.SizeOf<TOKEN_PRIVILEGES_ONE>(), out _, out _)
                || Marshal.GetLastPInvokeError() != 0)
            {
                Log.Warn($"Could not enable {NativeMethods.SE_SHUTDOWN_NAME} (error {Marshal.GetLastPInvokeError()})");
                return false;
            }

            return true;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
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
