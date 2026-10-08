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
    public static Task ThemeChangedAsync() => Task.Run(() =>
    {
        var parameter = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try
        {
            NativeMethods.SendMessageTimeout(HwndBroadcast, NativeMethods.WM_SETTINGCHANGE, 0, parameter,
                NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out _);
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or ExternalException)
        {
            Log.Warn("Could not broadcast the theme change", ex);
        }
        finally
        {
            Marshal.FreeHGlobal(parameter);
        }
    });
}
