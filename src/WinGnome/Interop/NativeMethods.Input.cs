using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>KBDLLHOOKSTRUCT: the payload of a WH_KEYBOARD_LL callback.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public nint dwExtraInfo;
}

/// <summary>Low-level keyboard hook, key-state and system-parameter declarations.</summary>
internal static partial class NativeMethods
{
    // ---- Low-level keyboard hook ------------------------------------------------------------
    public const int WH_KEYBOARD_LL = 13;
    public const int HC_ACTION = 0;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    /// <summary>KBDLLHOOKSTRUCT.flags bit set for events that came from SendInput / keybd_event.</summary>
    public const uint LLKHF_INJECTED = 0x00000010;

    // ---- Virtual keys -----------------------------------------------------------------------
    public const int VK_LBUTTON = 0x01;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;

    /// <summary>
    /// An unassigned virtual-key code. Tapping it while the Windows key is down turns the press into a
    /// "combination", which stops the shell from opening Start when the Windows key is released.
    /// </summary>
    public const ushort VK_UNASSIGNED_MASK = 0xE8;

    // ---- SystemParametersInfo ---------------------------------------------------------------
    public const uint SPI_GETACTIVEWINDOWTRACKING = 0x1000;
    public const uint SPI_SETACTIVEWINDOWTRACKING = 0x1001;
    public const uint SPI_GETACTIVEWNDTRKZORDER = 0x100C;
    public const uint SPI_SETACTIVEWNDTRKZORDER = 0x100D;
    public const uint SPI_GETACTIVEWNDTRKTIMEOUT = 0x2002;
    public const uint SPI_SETACTIVEWNDTRKTIMEOUT = 0x2003;
    public const uint SPIF_SENDCHANGE = 0x0002;

    public delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    // Delegate parameters are not supported by LibraryImport.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int hookType, LowLevelKeyboardProc callback, nint module, uint threadId);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? moduleName);

    /// <summary>SystemParametersInfo for SPI_GET* actions that write a BOOL or DWORD.</summary>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoGet(uint action, uint uiParam, out int value, uint winIni);

    /// <summary>SystemParametersInfo for SPI_SET* actions that take their value in pvParam itself.</summary>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoSet(uint action, uint uiParam, nint value, uint winIni);

    // ---- Mouse, keyboard and snap settings (Settings app panels) --------------------------------
    public const uint SPIF_UPDATEINIFILE = 0x0001;
    public const uint SPI_GETMOUSE = 0x0003;
    public const uint SPI_SETMOUSE = 0x0004;
    public const uint SPI_GETKEYBOARDSPEED = 0x000A;
    public const uint SPI_SETKEYBOARDSPEED = 0x000B;
    public const uint SPI_GETKEYBOARDDELAY = 0x0016;
    public const uint SPI_SETKEYBOARDDELAY = 0x0017;
    public const uint SPI_SETMOUSEBUTTONSWAP = 0x0021;
    public const uint SPI_GETWHEELSCROLLLINES = 0x0068;
    public const uint SPI_SETWHEELSCROLLLINES = 0x0069;
    public const uint SPI_GETMOUSESPEED = 0x0070;
    public const uint SPI_SETMOUSESPEED = 0x0071;
    public const uint SPI_GETWINARRANGING = 0x0082;
    public const uint SPI_SETWINARRANGING = 0x0083;
    public const int SM_SWAPBUTTON = 23;

    /// <summary>SPI_GETWHEELSCROLLLINES value meaning "scroll one screen at a time".</summary>
    public const int WHEEL_PAGESCROLL = -1;

    /// <summary>SystemParametersInfo for actions whose pvParam is an int array (SPI_GETMOUSE / SPI_SETMOUSE).</summary>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoArray(uint action, uint uiParam, [In, Out] int[] value, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    /// <summary>Fills <paramref name="layouts"/> with the input locale handles (HKLs) of the session; returns how many.</summary>
    [LibraryImport("user32.dll")]
    public static partial int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    [LibraryImport("user32.dll")]
    public static partial nint GetKeyboardLayout(uint threadId);

    /// <summary>True while the key is physically held (high bit of GetAsyncKeyState).</summary>
    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
