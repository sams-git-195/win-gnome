using System.Runtime.InteropServices;

namespace WinGnome.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEMTIME
{
    public ushort wYear;
    public ushort wMonth;
    public ushort wDayOfWeek;
    public ushort wDay;
    public ushort wHour;
    public ushort wMinute;
    public ushort wSecond;
    public ushort wMilliseconds;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DYNAMIC_TIME_ZONE_INFORMATION
{
    public int Bias;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string StandardName;

    public SYSTEMTIME StandardDate;
    public int StandardBias;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DaylightName;

    public SYSTEMTIME DaylightDate;
    public int DaylightBias;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string TimeZoneKeyName;

    [MarshalAs(UnmanagedType.U1)]
    public bool DynamicDaylightTimeDisabled;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_PRIVILEGES_ONE
{
    public uint PrivilegeCount;
    public LUID Luid;
    public uint Attributes;
}

/// <summary>Time zone and system information for the Date &amp; Time and About panels.</summary>
internal static partial class NativeMethods
{
    public const uint ERROR_NO_MORE_ITEMS = 259;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    public const string SE_TIME_ZONE_NAME = "SeTimeZonePrivilege";
    public const int WM_TIMECHANGE = 0x001E;

    // Struct marshalling (ByValTStr) needs the built-in marshaller, so these stay DllImport.
#pragma warning disable SYSLIB1054
    /// <summary>Enumerates the time zones Windows knows (Windows 8+); returns ERROR_NO_MORE_ITEMS past the last one.</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern uint EnumDynamicTimeZoneInformation(uint index, ref DYNAMIC_TIME_ZONE_INFORMATION timeZone);

    /// <summary>Needs SeTimeZonePrivilege enabled on the calling token; standard users hold it but it starts disabled.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDynamicTimeZoneInformation(ref DYNAMIC_TIME_ZONE_INFORMATION timeZone);
#pragma warning restore SYSLIB1054

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, in TOKEN_PRIVILEGES_ONE newState,
        uint bufferLength, nint previousState, nint returnLength);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    /// <summary>Installed RAM in kilobytes, as the firmware reports it.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
