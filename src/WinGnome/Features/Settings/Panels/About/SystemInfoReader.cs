using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.About;

/// <summary>What the About panel shows. Every field has readable text, "Unknown" when Windows didn't say.</summary>
internal sealed record SystemInfo(
    string DeviceName,
    string HardwareModel,
    string OperatingSystem,
    string OsType,
    string Processor,
    string Memory,
    string Graphics,
    string DiskCapacity);

/// <summary>Reads the About panel's facts from documented sources: registry (read-only), kernel32 and display devices.</summary>
internal static class SystemInfoReader
{
    private const string Unknown = "Unknown";
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ProcessorKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";

    /// <summary>Reads everything. Each part fails on its own (logged) so one missing value doesn't hide the rest.</summary>
    public static SystemInfo Read()
    {
        var culture = CultureInfo.CurrentCulture;
        return new SystemInfo(
            Environment.MachineName,
            HardwareModel(),
            WindowsVersion(),
            Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            SystemInfoText.Processor(ReadMachineString(ProcessorKey, "ProcessorNameString")),
            Memory(culture),
            Graphics(),
            Disk(culture));
    }

    private static string WindowsVersion()
    {
        var product = ReadMachineString(CurrentVersionKey, "ProductName");
        var display = ReadMachineString(CurrentVersionKey, "DisplayVersion");
        var build = int.TryParse(ReadMachineString(CurrentVersionKey, "CurrentBuildNumber"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)
            ? b
            : Environment.OSVersion.Version.Build;
        var ubr = ReadMachineValue(CurrentVersionKey, "UBR") is int u ? u : 0;
        return SystemInfoText.WindowsVersion(product, display, build, ubr);
    }

    private static string HardwareModel()
    {
        var maker = ReadMachineString(BiosKey, "SystemManufacturer")?.Trim();
        var model = ReadMachineString(BiosKey, "SystemProductName")?.Trim();
        var text = string.Join(' ', new[] { maker, model }.Where(s => !string.IsNullOrEmpty(s)));
        return text.Length == 0 ? Unknown : text;
    }

    private static string Memory(CultureInfo culture)
    {
        if (NativeMethods.GetPhysicallyInstalledSystemMemory(out var kilobytes) && kilobytes > 0)
        {
            return SystemInfoText.Memory(kilobytes * 1024, culture);
        }

        Log.Warn($"GetPhysicallyInstalledSystemMemory failed (error {Marshal.GetLastPInvokeError()})");
        return Unknown;
    }

    /// <summary>
    /// The distinct graphics adapters, including ones not driving a display (a laptop's discrete GPU), with mirroring
    /// and remote-display drivers left out, e.g. "Intel(R) Arc(TM) Graphics / NVIDIA GeForce RTX 5070".
    /// </summary>
    private static string Graphics()
    {
        var names = new List<string>();
        var device = DISPLAY_DEVICE.Create();
        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            var mirror = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0;
            var name = device.DeviceString.Trim();
            if (!mirror && name.Length > 0 && !name.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }

            device = DISPLAY_DEVICE.Create();
        }

        return names.Count == 0 ? Unknown : string.Join(" / ", names);
    }

    private static string Disk(CultureInfo culture)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            return SystemInfoText.DiskCapacity((ulong)new DriveInfo(root).TotalSize, culture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn("Could not read the system drive's size", ex);
            return Unknown;
        }
    }

    private static string? ReadMachineString(string key, string name) => ReadMachineValue(key, name) as string;

    private static object? ReadMachineValue(string key, string name)
    {
        try
        {
            using var opened = Registry.LocalMachine.OpenSubKey(key);
            return opened?.GetValue(name);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($@"Could not read HKLM\{key}\{name}", ex);
            return null;
        }
    }
}
