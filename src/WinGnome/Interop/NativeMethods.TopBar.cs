using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinGnome.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

/// <summary>Power, session and registry-notification declarations used by the top bar.</summary>
internal static partial class NativeMethods
{
    // ---- Activation -------------------------------------------------------------------------
    public const int WM_ACTIVATE = 0x0006;
    public const int WA_INACTIVE = 0;

    // ---- Power / session --------------------------------------------------------------------
    public const uint EWX_LOGOFF = 0x00000000;
    public const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;
    public const int SC_MONITORPOWER = 0xF170;
    public const int MONITOR_POWER_OFF = 2;
    public static readonly nint HWND_BROADCAST = 0xFFFF;

    // ---- Registry change notifications ------------------------------------------------------
    public const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;

    /// <summary>
    /// Windows 8+: the notification survives the registering thread exiting, so thread-pool threads may
    /// (re-)arm it. Without this flag the registration is silently dropped when that thread ends.
    /// </summary>
    public const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ExitWindowsEx(uint flags, uint reason);

    // The parameters and return value are BOOLEAN (one byte), not BOOL.
    [LibraryImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);

    /// <summary>EXTENDED_NAME_FORMAT.NameDisplay: the account's full name ("Jane Doe").</summary>
    private const int NameDisplay = 3;

    [LibraryImport("secur32.dll", EntryPoint = "GetUserNameExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserNameEx(int nameFormat, [Out] char[] buffer, ref uint size);

    /// <summary>The signed-in user's display name, or the account name when there is none (some local accounts).</summary>
    public static string GetUserDisplayName()
    {
        var buffer = new char[256];
        var size = (uint)buffer.Length;
        return GetUserNameEx(NameDisplay, buffer, ref size) && size > 0
            ? new string(buffer, 0, (int)size)
            : Environment.UserName;
    }

    /// <summary>Returns a Win32 error code (0 = success).</summary>
    [LibraryImport("advapi32.dll")]
    public static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        SafeWaitHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
