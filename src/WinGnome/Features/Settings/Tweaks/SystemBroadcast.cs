using System.Runtime.InteropServices;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Tweaks;

/// <summary>Tells running applications that a system setting changed.</summary>
internal static class SystemBroadcast
{
    private const nint HwndBroadcast = 0xFFFF;
    private const uint TimeoutMs = 1000;

    /// <summary>
    /// Broadcasts <c>WM_SETTINGCHANGE</c> with "ImmersiveColorSet" so apps re-read the light/dark preference.
    /// Runs on a worker thread because a hung top-level window can hold the call for its full timeout.
    /// </summary>
    public static Task ThemeChangedAsync() => Task.Run(() => SettingChanged("ImmersiveColorSet"));

    /// <summary>
    /// Broadcasts <c>WM_SETTINGCHANGE</c> naming <paramref name="area"/> (for Explorer's own options, "TraySettings").
    /// Blocks for up to a second per hung window, so call it from a worker thread.
    /// </summary>
    public static void SettingChanged(string area)
    {
        var parameter = Marshal.StringToHGlobalUni(area);
        try
        {
            NativeMethods.SendMessageTimeout(HwndBroadcast, NativeMethods.WM_SETTINGCHANGE, 0, parameter,
                NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out _);
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or ExternalException)
        {
            Log.Warn($"Could not broadcast the {area} setting change", ex);
        }
        finally
        {
            Marshal.FreeHGlobal(parameter);
        }
    }
}