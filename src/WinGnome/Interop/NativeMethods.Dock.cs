using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>Declarations used by the dock and taskbar features.</summary>
internal static partial class NativeMethods
{
    /// <summary>ChangeWindowMessageFilterEx action that lets lower-integrity processes send the message.</summary>
    public const uint MSGFLT_ALLOW = 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint changeFilterStruct);

    /// <summary>True when <paramref name="hwnd"/> belongs to the WinGnome process itself.</summary>
    public static bool IsOwnWindow(nint hwnd) => hwnd != 0 && GetProcessId(hwnd) == GetCurrentProcessId();
}
