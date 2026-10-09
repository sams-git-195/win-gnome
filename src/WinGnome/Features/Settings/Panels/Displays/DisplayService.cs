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
/// Reads displays and changes them through the documented CCD API (QueryDisplayConfig / SetDisplayConfig), which
/// validates and applies a whole configuration atomically, so a resize that moves a neighbour or a change of primary
/// lands as one. A change is applied without SDC_SAVE_TO_DATABASE, so it lasts for the session only until it is kept;
/// keeping it saves it to the database. Reverting reapplies the database's configuration (which never saw the change),
/// then the original explicitly if needed, and checks the result.
/// </summary>
internal static class DisplayService
{
    /// <summary>Everything the panel shows: names, current settings, mode lists and scale. Takes a while (mode lists).</summary>
    public static IReadOnlyList<DisplayInfo> Read()
    {
        var names = MonitorNames();
        return Attached()
            .Select(d => new DisplayInfo(
                d.Device.DeviceName,
                names.GetValueOrDefault(d.Device.DeviceName) ?? d.Device.DeviceString.Trim(),
                d.Setting,
                Modes(d.Device.DeviceName, d.BitsPerPixel),
                Scale(d.Setting)))
            .ToList();
    }

    /// <summary>The current setting of every attached display (cheap: no mode lists, names or scale).</summary>
    public static IReadOnlyList<DisplaySetting> Current() => Attached().Select(d => d.Setting).ToList();

    /// <summary>True when Windows accepts the whole configuration. Nothing changes.</summary>
    public static bool Test(IReadOnlyList<DisplaySetting> target) =>
        ApplySupplied(target, NativeMethods.SDC_VALIDATE | NativeMethods.SDC_ALLOW_CHANGES, "validate");

    /// <summary>
    /// Applies the configuration for this session only. Runs on a worker thread: Windows waits on every top-level
    /// window while it changes modes.
    /// </summary>
    public static bool ApplyForSession(IReadOnlyList<DisplaySetting> target) =>
        ApplySupplied(target, NativeMethods.SDC_APPLY | NativeMethods.SDC_ALLOW_CHANGES, "apply");

    /// <summary>
    /// Saves the kept configuration so it survives sign-out. SetDisplayConfig is atomic: if saving fails the database
    /// keeps the original, which then comes back at the next sign-in.
    /// </summary>
    public static bool Persist(IReadOnlyList<DisplaySetting> target) =>
        ApplySupplied(target, NativeMethods.SDC_APPLY | NativeMethods.SDC_ALLOW_CHANGES | NativeMethods.SDC_SAVE_TO_DATABASE, "save");

    /// <summary>
    /// Puts the displays back to <paramref name="original"/>: first by applying the database's configuration (an
    /// unconfirmed change never reached it), then, if the displays still differ, the original explicitly. Returns true
    /// only when every attached display of <paramref name="original"/> shows its original mode and position again.
    /// </summary>
    public static bool Revert(IReadOnlyList<DisplaySetting> original)
    {
        var result = NativeMethods.SetDisplayConfig(0, null, 0, null, NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_DATABASE_CURRENT);
        if (result != 0)
        {
            Log.Warn($"SetDisplayConfig could not reapply the saved display configuration (error {result})");
        }

        if (Matches(original))
        {
            return true;
        }

        ApplySupplied(original, NativeMethods.SDC_APPLY | NativeMethods.SDC_ALLOW_CHANGES, "restore");
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
        var present = expected
            .Where(e => current.Any(c => string.Equals(c.DeviceName, e.DeviceName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return present.Count > 0 && DisplayRevertRecord.IsStillApplied(new DisplayRevert(present, present), current);
    }

    /// <summary>
    /// Queries the active configuration, rewrites each named display's source mode (size and position) and, when the
    /// refresh rate changes, its path's refresh rate (letting Windows pick the matching target timing), then calls
    /// SetDisplayConfig with the supplied configuration. Displays not named keep their settings.
    /// </summary>
    private static bool ApplySupplied(IReadOnlyList<DisplaySetting> settings, uint flags, string what)
    {
        if (QueryActive() is not { } config)
        {
            return false;
        }

        var (paths, modes) = config;

        foreach (var setting in settings)
        {
            var index = Array.FindIndex(paths, p => string.Equals(SourceName(p), setting.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || paths[index].sourceModeInfoIdx >= modes.Length
                || modes[paths[index].sourceModeInfoIdx].infoType != NativeMethods.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                Log.Warn($"Could not {what} {setting}: the display is not in the active configuration");
                return false;
            }

            ref var source = ref modes[paths[index].sourceModeInfoIdx];
            source.sourceWidth = (uint)setting.Width;
            source.sourceHeight = (uint)setting.Height;
            source.sourcePositionX = setting.X;
            source.sourcePositionY = setting.Y;

            ref var path = ref paths[index];
            var currentHz = path.refreshRateDenominator == 0 ? 0 : (double)path.refreshRateNumerator / path.refreshRateDenominator;

            // GDI reports 59.94 Hz as 59 and the path as 60000/1001: within a hertz is the same rate.
            if (Math.Abs(currentHz - setting.RefreshHz) >= 1)
            {
                path.refreshRateNumerator = (uint)setting.RefreshHz;
                path.refreshRateDenominator = 1;
                path.targetModeInfoIdx = NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }
        }

        var result = NativeMethods.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG | flags);
        if (result == 0)
        {
            return true;
        }

        Log.Warn($"SetDisplayConfig could not {what} the display configuration (error {result})");
        return false;
    }

    private static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes)? QueryActive()
    {
        var error = NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
        if (error == 0)
        {
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            error = NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, 0);
            if (error == 0)
            {
                return (paths[..(int)pathCount], modes[..(int)modeCount]);
            }
        }

        Log.Warn($"QueryDisplayConfig failed (error {error})");
        return null;
    }

    /// <summary>The GDI device name of a path's source, e.g. <c>\\.\DISPLAY1</c>, or null.</summary>
    private static string? SourceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = Header(NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), path.sourceAdapterId, path.sourceId),
            viewGdiDeviceName = "",
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref source) == 0 ? source.viewGdiDeviceName : null;
    }

    /// <summary>Attached, non-mirroring display devices with their current setting.</summary>
    private static List<(DISPLAY_DEVICE Device, DisplaySetting Setting, uint BitsPerPixel)> Attached()
    {
        var displays = new List<(DISPLAY_DEVICE, DisplaySetting, uint)>();
        var device = DISPLAY_DEVICE.Create();
        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            var attached = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            var mirror = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0;
            if (attached && !mirror && CurrentMode(device.DeviceName) is { } mode)
            {
                var isPrimary = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                var setting = new DisplaySetting(device.DeviceName, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight,
                    (int)mode.dmDisplayFrequency, mode.dmPositionX, mode.dmPositionY, isPrimary);
                displays.Add((device, setting, mode.dmBitsPerPel));
            }

            device = DISPLAY_DEVICE.Create();
        }

        return displays;
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

    private static int Scale(DisplaySetting display)
    {
        var bounds = PixelRect.FromSize(display.X, display.Y, display.Width, display.Height);
        var monitor = NativeMethods.MonitorFromPoint(new POINT { X = bounds.CenterX, Y = bounds.CenterY }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? DisplayModes.ScalePercent((int)dpi) : 100;
    }

    /// <summary>Monitor names by GDI device name, from the display configuration.</summary>
    private static Dictionary<string, string> MonitorNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (QueryActive() is not { } config)
        {
            return names;
        }

        var paths = config.Paths;

        foreach (var path in paths)
        {
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = Header(NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), path.targetAdapterId, path.targetId),
                monitorFriendlyDeviceName = "",
                monitorDevicePath = "",
            };
            if (SourceName(path) is not { } source || NativeMethods.DisplayConfigGetDeviceInfo(ref target) != 0)
            {
                continue;
            }

            var name = target.outputTechnology == NativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                ? "Built-in Display"
                : target.monitorFriendlyDeviceName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names[source] = name.Trim();
            }
        }

        return names;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(int type, int size, LUID adapter, uint id) =>
        new() { type = type, size = size, adapterId = adapter, id = id };
}
