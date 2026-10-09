using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>
/// CCHDEVICENAME (32) UTF-16 code units, inline so <see cref="MONITORINFOEXW"/> stays blittable (ushort rather than
/// char: char is not blittable under runtime marshalling).
/// </summary>
[InlineArray(32)]
internal struct DeviceNameBuffer
{
    private ushort _first;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFOEXW
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    public DeviceNameBuffer szDevice;

    /// <summary>The GDI device name (<c>\\.\DISPLAY1</c>), up to the first NUL.</summary>
    public readonly string DeviceName
    {
        get
        {
            ReadOnlySpan<char> name = MemoryMarshal.Cast<ushort, char>((ReadOnlySpan<ushort>)szDevice);
            var end = name.IndexOf('\0');
            return new string(end < 0 ? name : name[..end]);
        }
    }
}

internal static partial class NativeMethods
{
    // ---- user32: monitors (per-monitor bars and docks, spec 0010) ----------------------------
    public const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>wParam of the WM_SETTINGCHANGE broadcast after a work area changed.</summary>
    public const nint SPI_SETWORKAREA = 0x002F;

    public delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromRect(in RECT rect, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MONITORINFOEXW info);

    // Delegate parameters are not supported by LibraryImport, so this stays DllImport.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
#pragma warning restore SYSLIB1054

    /// <summary>The monitor's full information, including its device name; null when the handle is stale.</summary>
    public static MONITORINFOEXW? GetMonitorInfoEx(nint monitor)
    {
        var info = new MONITORINFOEXW { cbSize = Unsafe.SizeOf<MONITORINFOEXW>() };
        return monitor != 0 && GetMonitorInfo(monitor, ref info) ? info : null;
    }
}
