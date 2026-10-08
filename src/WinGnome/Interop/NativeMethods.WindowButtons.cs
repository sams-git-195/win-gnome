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

    /// <summary>DWMWA_BORDER_COLOR value that suppresses the Windows 11 window border.</summary>
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    // ---- DWM corner preferences -------------------------------------------------------------
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [LibraryImport("gdi32.dll")]
    public static partial uint GetPixel(nint hdc, int x, int y);
}
