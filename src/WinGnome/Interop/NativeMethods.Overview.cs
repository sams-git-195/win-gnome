using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>DWM_THUMBNAIL_PROPERTIES: which thumbnail properties to change and their new values.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    public int fVisible;
    public int fSourceClientAreaOnly;
}

/// <summary>Win32 MARGINS (DwmExtendFrameIntoClientArea). -1 on every side means "sheet of glass".</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MARGINS
{
    public int cxLeftWidth;
    public int cxRightWidth;
    public int cyTopHeight;
    public int cyBottomHeight;
}

/// <summary>Win32 WINDOWPLACEMENT.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

/// <summary>DWM thumbnail, system-backdrop and global-hotkey declarations used by the Activities overview.</summary>
internal static partial class NativeMethods
{
    // ---- DWM thumbnails ---------------------------------------------------------------------
    public const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    public const uint DWM_TNP_OPACITY = 0x00000004;
    public const uint DWM_TNP_VISIBLE = 0x00000008;
    public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    // ---- DWM system backdrop ----------------------------------------------------------------
    /// <summary>DWMSBT_TRANSIENTWINDOW: the acrylic backdrop used by flyouts (Windows 11 22H2+).</summary>
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    // ---- Monitors ---------------------------------------------------------------------------
    /// <summary>MonitorFromPoint flag: return 0 when the point is on no monitor.</summary>
    public const uint MONITOR_DEFAULTTONULL = 0;

    // ---- Hotkeys ----------------------------------------------------------------------------
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Parent handle that turns a window into a message-only window.</summary>
    public static readonly nint HWND_MESSAGE = -3;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmUnregisterThumbnail(nint thumbnail);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmUpdateThumbnailProperties(nint thumbnail, in DWM_THUMBNAIL_PROPERTIES properties);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmQueryThumbnailSourceSize(nint thumbnail, out SIZE size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, in MARGINS margins);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    /// <summary>
    /// Size of the window in its restored state (physical pixels), which is what a minimised window
    /// will look like again. Returns null when the placement cannot be read.
    /// </summary>
    public static (int Width, int Height)? GetRestoredSize(nint hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement))
        {
            return null;
        }

        var rect = placement.rcNormalPosition.ToPixelRect();
        return rect.IsEmpty ? null : (rect.Width, rect.Height);
    }
}
