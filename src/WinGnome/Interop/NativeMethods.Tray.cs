using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinGnome.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASSEX
{
    public int cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public nint lpszMenuName;
    public nint lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct COPYDATASTRUCT
{
    public nint dwData;
    public int cbData;
    public nint lpData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS
{
    public nint hwnd;
    public nint hwndInsertAfter;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public uint flags;
}

/// <summary>An HICON this process owns (a CopyIcon result); destroyed exactly once, even if never disposed.</summary>
internal sealed class IconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public IconHandle()
        : base(ownsHandle: true)
    {
    }

    /// <summary>Copies an icon owned by another process; null when the handle is no longer valid.</summary>
    public static IconHandle? Copy(nint icon)
    {
        var copy = NativeMethods.CopyIcon(icon);
        return copy.IsInvalid ? null : copy;
    }

    protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);
}

/// <summary>Window class, message loop, message forwarding and icon declarations used by the tray host.</summary>
internal static partial class NativeMethods
{
    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    public const uint WM_QUIT = 0x0012;
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_USER = 0x0400;

    /// <summary>First RegisterWindowMessage value; messages from here up are app-registered broadcasts.</summary>
    public const uint RegisteredMessageFirst = 0xC000;

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(in WNDCLASSEX windowClass);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClass(string className, nint instance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out MSG msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SendNotifyMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InSendMessage();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetTimer(nint hwnd, nint id, uint elapseMs, nint timerProc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nint id);

    /// <summary>The window receives posted "SHELLHOOK" messages (HSHELL_* in wParam) for shell window events.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeregisterShellHookWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "SetPropW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProp(nint hwnd, string name, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetProp(nint hwnd, string name);

    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint RemoveProp(nint hwnd, string name);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IconHandle CopyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();
}
