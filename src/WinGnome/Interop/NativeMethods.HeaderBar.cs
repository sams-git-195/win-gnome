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
}
