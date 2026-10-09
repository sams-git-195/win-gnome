using System.Runtime.InteropServices;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Geometry;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>A display attached to the desktop, as Windows reports it now.</summary>
/// <param name="DeviceName">GDI device name (<c>\\.\DISPLAY1</c>), the id used throughout.</param>
/// <param name="Name">Monitor name from its EDID ("DELL U2720Q"), "Built-in Display" for a laptop panel.</param>
/// <param name="Current">Current mode and position.</param>
/// <param name="Modes">Every mode at the current colour depth.</param>
/// <param name="ScalePercent">Effective scale (read-only here; see KI-062).</param>
internal sealed record DisplayInfo(string DeviceName, string Name, DisplaySetting Current, IReadOnlyList<DisplayMode> Modes, int ScalePercent);

/// <summary>
/// Reads displays and changes their mode, position and primary with the documented ChangeDisplaySettingsEx. A change
/// is first tested (CDS_TEST) and then applied for this session only, without writing the registry, so until the user
/// keeps it a reboot or sign-out drops it; keeping it writes it to the registry. Reverting reapplies the registry's
/// settings (which still hold the original) and checks the result.
/// </summary>
internal static class DisplayService
{
    private const uint ChangedFields = NativeMethods.DM_POSITION | NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT | NativeMethods.DM_DISPLAYFREQUENCY;

    /// <summary>The attached displays in Windows' order.</summary>
    public static IReadOnlyList<DisplayInfo> Read()
    {
        var names = MonitorNames();
        var displays = new List<DisplayInfo>();
        var device = DISPLAY_DEVICE.Create();
        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            var attached = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            var mirror = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0;
            if (attached && !mirror && CurrentMode(device.DeviceName) is { } mode)
            {
                var current = ToSetting(device.DeviceName, mode, (device.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0);
                displays.Add(new DisplayInfo(
                    device.DeviceName,
                    names.GetValueOrDefault(device.DeviceName) ?? device.DeviceString.Trim(),
                    current,
                    Modes(device.DeviceName, mode.dmBitsPerPel),
                    Scale(current)));
            }

            device = DISPLAY_DEVICE.Create();
        }

        return displays;
    }

    /// <summary>The current setting of every attached display.</summary>
    public static IReadOnlyList<DisplaySetting> Current() => Read().Select(d => d.Current).ToList();

    /// <summary>True when Windows accepts each display's new mode. Nothing changes.</summary>
    public static bool Test(IReadOnlyList<DisplaySetting> target) =>
        target.All(setting => Change(setting, NativeMethods.CDS_TEST, "test"));

    /// <summary>
    /// Applies <paramref name="target"/> for this session only (no registry write), the primary first. Any failure,
    /// including a mode that needs a restart, puts every display back to <paramref name="original"/>; the result says
    /// whether the change is showing and, if not, whether the original was restored. Runs on a worker thread: Windows
    /// waits on every top-level window while it changes modes.
    /// </summary>
    public static (bool Applied, bool Restored) ApplyTemporarily(IReadOnlyList<DisplaySetting> target, IReadOnlyList<DisplaySetting> original)
    {
        foreach (var setting in target.OrderByDescending(s => s.IsPrimary))
        {
            if (!Change(setting, setting.IsPrimary ? NativeMethods.CDS_SET_PRIMARY : 0, "apply"))
            {
                return (false, Revert(original));
            }
        }

        return (true, false);
    }

    /// <summary>Writes the kept settings to the registry so they survive sign-out and reboot.</summary>
    public static bool Persist(IReadOnlyList<DisplaySetting> target)
    {
        // The primary first: CDS_SET_PRIMARY moves the origin, and the others are positioned relative to it.
        foreach (var setting in target.OrderByDescending(s => s.IsPrimary))
        {
            var flags = NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET | (setting.IsPrimary ? NativeMethods.CDS_SET_PRIMARY : 0);
            if (!Change(setting, flags, "save"))
            {
                return false;
            }
        }

        return ApplyRegistry("save the kept display settings");
    }

    /// <summary>
    /// Puts the displays back to <paramref name="original"/>: first by reapplying the registry (an unconfirmed change
    /// never reached it), then, if the displays still differ, display by display. Returns true only when every attached
    /// display of <paramref name="original"/> shows its original mode and position again.
    /// </summary>
    public static bool Revert(IReadOnlyList<DisplaySetting> original)
    {
        ApplyRegistry("reapply the saved display settings");
        if (Matches(original))
        {
            return true;
        }

        var present = Current().Select(d => d.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in original.Where(s => present.Contains(s.DeviceName)).OrderByDescending(s => s.IsPrimary))
        {
            Change(setting, setting.IsPrimary ? NativeMethods.CDS_SET_PRIMARY : 0, "restore");
        }

        var restored = Matches(original);
        if (!restored)
        {
            Log.Warn("The displays could not be put back to their previous settings");
        }

        return restored;
    }

    /// <summary>True when the attached displays of <paramref name="expected"/> show those modes and positions.</summary>
    private static bool Matches(IReadOnlyList<DisplaySetting> expected)
    {
        var current = Current();
        var present = expected.Where(e => current.Any(c => string.Equals(c.DeviceName, e.DeviceName, StringComparison.OrdinalIgnoreCase))).ToList();
        return present.Count > 0 && DisplayRevertRecord.IsStillApplied(new DisplayRevert(present, present), current);
    }

    /// <summary>ChangeDisplaySettingsEx with no device and no mode applies what the registry holds to every display.</summary>
    private static bool ApplyRegistry(string what)
    {
        var result = NativeMethods.ChangeDisplaySettingsExApplyStaged(null, 0, 0, 0, 0);
        if (result == NativeMethods.DISP_CHANGE_SUCCESSFUL)
        {
            return true;
        }

        Log.Warn($"ChangeDisplaySettingsEx could not {what} (result {result})");
        return false;
    }
    private static bool Change(DisplaySetting setting, uint flags, string what)
    {
        if (CurrentMode(setting.DeviceName) is not { } mode)
        {
            return false;
        }

        mode.dmPelsWidth = (uint)setting.Width;
        mode.dmPelsHeight = (uint)setting.Height;
        mode.dmDisplayFrequency = (uint)setting.RefreshHz;
        mode.dmPositionX = setting.X;
        mode.dmPositionY = setting.Y;
        mode.dmFields = ChangedFields;
        var result = NativeMethods.ChangeDisplaySettingsEx(setting.DeviceName, ref mode, 0, flags, 0);
        if (result == NativeMethods.DISP_CHANGE_SUCCESSFUL)
        {
            return true;
        }

        Log.Warn($"ChangeDisplaySettingsEx could not {what} {setting} (result {result})");
        return false;
    }

    private static DEVMODE? CurrentMode(string deviceName)
    {
        var mode = DEVMODE.Create();
        if (NativeMethods.EnumDisplaySettingsEx(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode, 0))
        {
            return mode;
        }

        Log.Warn($"Could not read the current mode of {deviceName}");
        return null;
    }

    private static List<DisplayMode> Modes(string deviceName, uint bitsPerPixel)
    {
        var modes = new List<DisplayMode>();
        var mode = DEVMODE.Create();
        for (var i = 0; NativeMethods.EnumDisplaySettingsEx(deviceName, i, ref mode, 0); i++)
        {
            // Interlaced and other-depth modes would only duplicate the list.
            if (mode.dmBitsPerPel == bitsPerPixel && mode.dmDisplayFrequency > 1)
            {
                modes.Add(new DisplayMode((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency));
            }

            mode = DEVMODE.Create();
        }

        return modes;
    }

    private static DisplaySetting ToSetting(string deviceName, DEVMODE mode, bool isPrimary) =>
        new(deviceName, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency, mode.dmPositionX, mode.dmPositionY, isPrimary);

    private static int Scale(DisplaySetting display)
    {
        var bounds = PixelRect.FromSize(display.X, display.Y, display.Width, display.Height);
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = bounds.CenterX, Y = bounds.CenterY }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? DisplayModes.ScalePercent((int)dpi) : 100;
    }

    /// <summary>Monitor names by GDI device name, from the display configuration (documented, Windows 7+).</summary>
    private static Dictionary<string, string> MonitorNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
        {
            return names;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        var error = NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, 0);
        if (error != 0)
        {
            Log.Warn($"QueryDisplayConfig failed (error {error}); displays are named by adapter");
            return names;
        }

        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = Header(NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), path.sourceAdapterId, path.sourceId),
                viewGdiDeviceName = "",
            };
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = Header(NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), path.targetAdapterId, path.targetId),
                monitorFriendlyDeviceName = "",
                monitorDevicePath = "",
            };
            if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != 0 || NativeMethods.DisplayConfigGetDeviceInfo(ref target) != 0)
            {
                continue;
            }

            var name = target.outputTechnology == NativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                ? "Built-in Display"
                : target.monitorFriendlyDeviceName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names[source.viewGdiDeviceName] = name.Trim();
            }
        }

        return names;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(int type, int size, LUID adapter, uint id) =>
        new() { type = type, size = size, adapterId = adapter, id = id };
}
