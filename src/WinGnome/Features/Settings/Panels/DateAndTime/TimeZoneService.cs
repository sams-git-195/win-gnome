using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.DateAndTime;

/// <summary>
/// Lists Windows' time zones and changes the system time zone. Changing it needs SeTimeZonePrivilege, which standard
/// users hold but which starts disabled, so it is enabled just for the call and disabled again afterwards.
/// </summary>
internal static class TimeZoneService
{
    private const string AutoTimeZoneServiceKey = @"SYSTEM\CurrentControlSet\Services\tzautoupdate";
    private const int ServiceDisabled = 4;
    private const uint BroadcastTimeoutMs = 1000;
    private static readonly nint HwndBroadcast = 0xFFFF;

    public static IReadOnlyList<TimeZoneEntry> List() =>
        TimeZoneList.Sort(TimeZoneInfo.GetSystemTimeZones().Select(z => new TimeZoneEntry(z.Id, z.DisplayName, z.BaseUtcOffset)));

    /// <summary>The current time zone's id, re-read from Windows rather than .NET's cache.</summary>
    public static string CurrentId()
    {
        TimeZoneInfo.ClearCachedData();
        return TimeZoneInfo.Local.Id;
    }

    /// <summary>True when Windows sets the time zone from the location ("Set time zone automatically"), which can undo a manual change.</summary>
    public static bool IsAutomatic()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AutoTimeZoneServiceKey);
            return key?.GetValue("Start") is int start && start != ServiceDisabled;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read whether the time zone is set automatically", ex);
            return false;
        }
    }

    /// <summary>Sets the system time zone. Runs on a worker thread (it broadcasts WM_TIMECHANGE).</summary>
    public static bool Set(string id)
    {
        if (!TryFind(id, out var zone))
        {
            Log.Warn($"Windows doesn't list the time zone \"{id}\"");
            return false;
        }

        if (!WithTimeZonePrivilege(() => NativeMethods.SetDynamicTimeZoneInformation(ref zone)))
        {
            return false;
        }

        TimeZoneInfo.ClearCachedData();
        NativeMethods.SendMessageTimeout(HwndBroadcast, NativeMethods.WM_TIMECHANGE, 0, 0, NativeMethods.SMTO_ABORTIFHUNG, BroadcastTimeoutMs, out _);
        return true;
    }

    private static bool TryFind(string id, out DYNAMIC_TIME_ZONE_INFORMATION zone)
    {
        for (uint index = 0; ; index++)
        {
            zone = default;
            var result = NativeMethods.EnumDynamicTimeZoneInformation(index, ref zone);
            if (result == NativeMethods.ERROR_NO_MORE_ITEMS)
            {
                return false;
            }

            if (result != 0)
            {
                Log.Warn($"EnumDynamicTimeZoneInformation failed at {index} (error {result})");
                return false;
            }

            if (string.Equals(zone.TimeZoneKeyName, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
    }

    /// <summary>Runs <paramref name="call"/> with SeTimeZonePrivilege enabled on the process token, logging any failure.</summary>
    private static bool WithTimeZonePrivilege(Func<bool> call)
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var token))
        {
            Log.Warn($"OpenProcessToken failed (error {Marshal.GetLastPInvokeError()})");
            return false;
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, NativeMethods.SE_TIME_ZONE_NAME, out var luid))
            {
                Log.Warn($"LookupPrivilegeValue failed (error {Marshal.GetLastPInvokeError()})");
                return false;
            }

            var enable = new TOKEN_PRIVILEGES_ONE { PrivilegeCount = 1, Luid = luid, Attributes = NativeMethods.SE_PRIVILEGE_ENABLED };

            // AdjustTokenPrivileges reports a privilege the token lacks through the last error, not its result.
            if (!NativeMethods.AdjustTokenPrivileges(token, false, enable, 0, 0, 0) || Marshal.GetLastPInvokeError() != 0)
            {
                Log.Warn($"Could not enable {NativeMethods.SE_TIME_ZONE_NAME} (error {Marshal.GetLastPInvokeError()})");
                return false;
            }

            try
            {
                if (call())
                {
                    return true;
                }

                Log.Warn($"SetDynamicTimeZoneInformation failed (error {Marshal.GetLastPInvokeError()})");
                return false;
            }
            finally
            {
                var disable = enable with { Attributes = 0 };
                NativeMethods.AdjustTokenPrivileges(token, false, disable, 0, 0, 0);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
