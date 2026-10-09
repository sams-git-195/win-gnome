using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>Declarations used by the traffic-light window buttons (Features/WindowButtons).</summary>
internal static partial class NativeMethods
{
    // ---- GetWindow --------------------------------------------------------------------------
    public const uint GW_HWNDPREV = 3;

    // ---- Errors and sentinels ---------------------------------------------------------------
    public const int ERROR_ACCESS_DENIED = 5;

    /// <summary>GetPixel's failure value (outside the clipping region, or the DC cannot be read).</summary>
    public const uint CLR_INVALID = 0xFFFFFFFF;

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowEnabled(nint hwnd);

    /// <summary>On success the system owns <paramref name="region"/>; the caller deletes it only on failure.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [LibraryImport("gdi32.dll")]
    public static partial uint GetPixel(nint hdc, int x, int y);

    // ---- Hit testing custom title bars ------------------------------------------------------
    /// <summary>WM_NCHITTEST: the point belongs to a window underneath (same thread).</summary>
    public const int HTTRANSPARENT = -1;

    public const uint CWP_SKIPINVISIBLE = 0x0001;
    public const uint CWP_SKIPTRANSPARENT = 0x0004;

    /// <summary>Right-to-left mirrored window layout: its caption buttons are on the left.</summary>
    public const long WS_EX_LAYOUTRTL = 0x00400000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hwnd, ref POINT point);

    [LibraryImport("user32.dll")]
    public static partial nint ChildWindowFromPointEx(nint parent, POINT point, uint flags);
}
