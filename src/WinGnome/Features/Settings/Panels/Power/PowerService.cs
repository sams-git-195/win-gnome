using System.Runtime.InteropServices;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Power;

/// <summary>Which power source a timeout applies to.</summary>
internal enum PowerSource
{
    PluggedIn,
    OnBattery,
}

/// <summary>
/// The active power plan's screen-off and sleep timeouts (documented powrprof API) and the Windows 11 power mode.
/// Timeouts are written to the active plan and the plan is re-applied so the change takes effect at once, which is
/// what Windows Settings does.
/// </summary>
internal static class PowerService
{
    /// <summary>The active power plan, or null (logged) when Windows doesn't say.</summary>
    public static Guid? ActiveScheme()
    {
        var error = NativeMethods.PowerGetActiveScheme(0, out var pointer);
        if (error != 0 || pointer == 0)
        {
            Log.Warn($"PowerGetActiveScheme failed (error {error})");
            return null;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    /// <summary>A timeout in seconds (0 = never), or null when it can't be read.</summary>
    public static int? ReadTimeout(Guid scheme, Guid subgroup, Guid setting, PowerSource source)
    {
        var error = source == PowerSource.PluggedIn
            ? NativeMethods.PowerReadACValueIndex(0, scheme, subgroup, setting, out var value)
            : NativeMethods.PowerReadDCValueIndex(0, scheme, subgroup, setting, out value);
        if (error != 0)
        {
            Log.Warn($"Could not read power setting {setting} ({source}, error {error})");
            return null;
        }

        return (int)Math.Min(value, int.MaxValue);
    }

    /// <summary>Writes a timeout to the active plan and re-applies the plan. Runs on a worker thread.</summary>
    public static bool WriteTimeout(Guid scheme, Guid subgroup, Guid setting, PowerSource source, int seconds)
    {
        var value = (uint)Math.Max(0, seconds);
        var error = source == PowerSource.PluggedIn
            ? NativeMethods.PowerWriteACValueIndex(0, scheme, subgroup, setting, value)
            : NativeMethods.PowerWriteDCValueIndex(0, scheme, subgroup, setting, value);
        if (error != 0)
        {
            Log.Warn($"Could not write power setting {setting} ({source}, error {error})");
            return false;
        }

        error = NativeMethods.PowerSetActiveScheme(0, scheme);
        if (error != 0)
        {
            Log.Warn($"PowerSetActiveScheme failed after a timeout change (error {error})");
            return false;
        }

        return true;
    }

    /// <summary>The battery state, as the top bar reads it.</summary>
    public static Core.TopBar.BatteryStatus Battery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var raw))
        {
            Log.Warn($"GetSystemPowerStatus failed (error {Marshal.GetLastPInvokeError()})");
            return Core.TopBar.BatteryStatus.None;
        }

        return Core.TopBar.BatteryStatus.FromPowerStatus(raw.ACLineStatus, raw.BatteryFlag, raw.BatteryLifePercent, raw.BatteryLifeTime);
    }
}
