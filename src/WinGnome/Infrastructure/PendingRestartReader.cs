using System.IO;
using Microsoft.Win32;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Infrastructure;

/// <summary>
/// Reads the registry signals that say a Windows update is waiting on a restart. Read-only (HKLM is never written)
/// and fast, so it is safe on the UI thread. Every read failure is logged and counts as "no signal".
/// </summary>
internal static class PendingRestartReader
{
    private const string WindowsUpdateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate";
    private const string ServicingKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing";
    private const string OrchestratorKey = @"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator";

    /// <summary>Undocumented (KI-107): non-zero makes Start's power menu offer "Update and restart / shut down".</summary>
    private const string ShutdownFlyoutValue = "ShutdownFlyoutOptions";

    /// <summary>The registry half of the signals; the caller fills in the Windows Update Agent's own flag.</summary>
    public static PendingRestartSignals Read(bool wuaRebootRequired)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return new PendingRestartSignals(
            wuaRebootRequired,
            KeyExists(hklm, WindowsUpdateKey + @"\Auto Update\RebootRequired"),
            KeyExists(hklm, ServicingKey + @"\RebootPending"),
            ReadFlyoutOptions(hklm));
    }

    private static bool KeyExists(RegistryKey hklm, string path)
    {
        try
        {
            using var key = hklm.OpenSubKey(path, writable: false);
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Could not read HKLM\\{path}", ex);
            return false;
        }
    }

    private static int? ReadFlyoutOptions(RegistryKey hklm)
    {
        try
        {
            using var key = hklm.OpenSubKey(OrchestratorKey, writable: false);
            return key?.GetValue(ShutdownFlyoutValue) as int?;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Could not read HKLM\\{OrchestratorKey}\\{ShutdownFlyoutValue}", ex);
            return null;
        }
    }
}
