using System.Runtime.InteropServices;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels;

/// <summary>SystemParametersInfo reads and persisted writes with the Win32 error logged on failure.</summary>
internal static class Spi
{
    /// <summary>Write and persist for the user, and tell running apps (as Windows Settings does).</summary>
    private const uint PersistAndNotify = NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE;

    /// <summary>Reads an SPI_GET* value returned through pvParam, or <paramref name="fallback"/> (logged) on failure.</summary>
    public static int Get(uint action, int fallback, string what)
    {
        if (NativeMethods.SystemParametersInfoGet(action, 0, out var value, 0))
        {
            return value;
        }

        Log.Warn($"Could not read {what} (SystemParametersInfo 0x{action:X}, error {Marshal.GetLastPInvokeError()})");
        return fallback;
    }

    /// <summary>Writes an SPI_SET* action; <paramref name="uiParam"/> and <paramref name="pvParam"/> as that action expects.</summary>
    public static bool Set(uint action, uint uiParam, nint pvParam, string what)
    {
        if (NativeMethods.SystemParametersInfoSet(action, uiParam, pvParam, PersistAndNotify))
        {
            return true;
        }

        Log.Warn($"Could not set {what} (SystemParametersInfo 0x{action:X}, error {Marshal.GetLastPInvokeError()})");
        return false;
    }

    /// <summary>Writes an SPI_SET* action whose pvParam is an int array.</summary>
    public static bool SetArray(uint action, int[] values, string what)
    {
        if (NativeMethods.SystemParametersInfoArray(action, 0, values, PersistAndNotify))
        {
            return true;
        }

        Log.Warn($"Could not set {what} (SystemParametersInfo 0x{action:X}, error {Marshal.GetLastPInvokeError()})");
        return false;
    }
}
