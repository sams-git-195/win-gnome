using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>DEVMODEW, display-device layout (the printer fields share the union and are not used).</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEVMODE
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmDeviceName;

    public ushort dmSpecVersion;
    public ushort dmDriverVersion;
    public ushort dmSize;
    public ushort dmDriverExtra;
    public uint dmFields;
    public int dmPositionX;
    public int dmPositionY;
    public uint dmDisplayOrientation;
    public uint dmDisplayFixedOutput;
    public short dmColor;
    public short dmDuplex;
    public short dmYResolution;
    public short dmTTOption;
    public short dmCollate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmFormName;

    public ushort dmLogPixels;
    public uint dmBitsPerPel;
    public uint dmPelsWidth;
    public uint dmPelsHeight;
    public uint dmDisplayFlags;
    public uint dmDisplayFrequency;
    public uint dmICMMethod;
    public uint dmICMIntent;
    public uint dmMediaType;
    public uint dmDitherType;
    public uint dmReserved1;
    public uint dmReserved2;
    public uint dmPanningWidth;
    public uint dmPanningHeight;

    public static DEVMODE Create() => new() { dmDeviceName = "", dmFormName = "", dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAY_DEVICE
{
    public int cb;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;

    public uint StateFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;

    public static DISPLAY_DEVICE Create() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>(), DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

/// <summary>DISPLAYCONFIG_PATH_INFO (72 bytes). Only the ids are read; the rest is kept for the layout.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_INFO
{
    public LUID sourceAdapterId;
    public uint sourceId;
    public uint sourceModeInfoIdx;
    public uint sourceStatusFlags;
    public LUID targetAdapterId;
    public uint targetId;
    public uint targetModeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;
    public uint refreshRateNumerator;
    public uint refreshRateDenominator;
    public uint scanLineOrdering;
    public int targetAvailable;
    public uint targetStatusFlags;
    public uint flags;
}

/// <summary>DISPLAYCONFIG_MODE_INFO, kept opaque: QueryDisplayConfig needs the buffer but WinGnome reads no mode from it.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct DISPLAYCONFIG_MODE_INFO
{
    public uint infoType;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public int type;
    public int size;
    public LUID adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string viewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint flags;
    public uint outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;
}

/// <summary>Display modes, arrangement and monitor names for the Displays panel (all documented APIs).</summary>
internal static partial class NativeMethods
{
    public const int ENUM_CURRENT_SETTINGS = -1;

    public const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    public const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    public const uint DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008;

    public const uint DM_POSITION = 0x00000020;
    public const uint DM_BITSPERPEL = 0x00040000;
    public const uint DM_PELSWIDTH = 0x00080000;
    public const uint DM_PELSHEIGHT = 0x00100000;
    public const uint DM_DISPLAYFREQUENCY = 0x00400000;

    public const uint CDS_UPDATEREGISTRY = 0x00000001;
    public const uint CDS_TEST = 0x00000002;
    public const uint CDS_SET_PRIMARY = 0x00000010;
    public const uint CDS_NORESET = 0x10000000;
    public const int DISP_CHANGE_SUCCESSFUL = 0;

    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    public const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    public const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;

    // Struct marshalling (ByValTStr) needs the built-in marshaller, so these stay DllImport.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE displayDevice, uint flags);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettingsEx(string device, int modeNum, ref DEVMODE devMode, uint flags);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE devMode, nint hwnd, uint flags, nint param);

    /// <summary>With every argument null/zero, applies the registry's settings (including any staged with CDS_NORESET) to all displays.</summary>
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsExApplyStaged(string? device, nint devMode, nint hwnd, uint flags, nint param);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    public static partial int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, nint currentTopologyId);
}
