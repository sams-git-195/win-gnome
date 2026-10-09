using System.IO;
using System.Security;
using Microsoft.Win32;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Apps;

/// <summary>
/// Reads (never writes) the Uninstall keys Windows Settings lists desktop apps from: HKLM in its 64- and 32-bit views
/// and HKCU. Hundreds of keys, so the panel calls it on a long-running worker.
/// </summary>
internal static class InstalledAppsService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>The desktop apps Windows would list (<see cref="InstalledAppFilter"/>), sorted by name.</summary>
    public static IReadOnlyList<InstalledAppRecord> Read()
    {
        var records = new List<InstalledAppRecord>();
        ReadView(RegistryHive.LocalMachine, RegistryView.Registry64, InstalledAppScope.Machine, records);
        ReadView(RegistryHive.LocalMachine, RegistryView.Registry32, InstalledAppScope.Machine32, records);
        ReadView(RegistryHive.CurrentUser, RegistryView.Default, InstalledAppScope.User, records);
        return InstalledAppFilter.Apply(records);
    }

    private static void ReadView(RegistryHive hive, RegistryView view, InstalledAppScope scope, List<InstalledAppRecord> into)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallKey);
            if (uninstall is null)
            {
                return;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                if (ReadKey(uninstall, name, scope) is { } record)
                {
                    into.Add(record);
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Apps: could not read the {scope} Uninstall keys", ex);
        }
    }

    /// <summary>One app's key; a key that can't be opened (permissions, deleted mid-walk) is skipped and logged.</summary>
    private static InstalledAppRecord? ReadKey(RegistryKey uninstall, string name, InstalledAppScope scope)
    {
        try
        {
            using var key = uninstall.OpenSubKey(name);
            // GetValue expands REG_EXPAND_SZ values; a %VAR% left in a plain string stays unexpanded (UninstallPlan refuses it).
            return key is null ? null : InstalledAppRecord.FromValues(name, scope, value => key.GetValue(value));
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Apps: could not read the Uninstall key \"{name}\" ({scope})", ex);
            return null;
        }
    }
}
