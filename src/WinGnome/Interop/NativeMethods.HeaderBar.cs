using System.Runtime.InteropServices;

namespace WinGnome.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public nint hwndTrack;
    public uint dwHoverTime;
}

/// <summary>Declarations used by the header bar of WinGnome's own windows (Controls/TrafficLights).</summary>
internal static partial class NativeMethods
{
    // ---- Non-client mouse messages ----------------------------------------------------------
    public const int WM_NCMOUSEMOVE = 0x00A0;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCLBUTTONDBLCLK = 0x00A3;
    public const int WM_NCMOUSELEAVE = 0x02A2;

    // ---- WM_NCHITTEST results ---------------------------------------------------------------
    public const int HTMAXBUTTON = 9;

    // ---- TrackMouseEvent --------------------------------------------------------------------
    public const uint TME_LEAVE = 0x00000002;
    public const uint TME_NONCLIENT = 0x00000010;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT eventTrack);

    // ---- System metrics ---------------------------------------------------------------------
    public const int SM_CXFRAME = 32;
    public const int SM_CYFRAME = 33;
    public const int SM_CXPADDEDBORDER = 92;

    /// <summary>Returns 0 on failure.</summary>
    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>Only for messages to WinGnome's own windows on the calling thread (no cross-process wait).</summary>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);
}
