using System.Runtime.InteropServices;

namespace WinGnome.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType;
    public int bmWidth;
    public int bmHeight;
    public int bmWidthBytes;
    public ushort bmPlanes;
    public ushort bmBitsPixel;
    public nint bmBits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public int biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

/// <summary>BITMAPINFO with room for the three BI_BITFIELDS masks GetDIBits may write after the header.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    public uint bmiColor0;
    public uint bmiColor1;
    public uint bmiColor2;
}

/// <summary>Shell, icon and GDI bitmap declarations used by the installed-apps services.</summary>
internal static partial class NativeMethods
{
    // ---- Window icons -----------------------------------------------------------------------
    public const int WM_GETICON = 0x007F;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int ICON_SMALL2 = 2;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>AllowSetForegroundWindow argument that lets any process take the foreground.</summary>
    public const int ASFW_ANY = -1;

    // ---- GDI --------------------------------------------------------------------------------
    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;

    // ---- user32 -----------------------------------------------------------------------------
    [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    public static partial nint GetClassLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint hdc);

    // ---- gdi32 ------------------------------------------------------------------------------
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint gdiObject);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static partial int GetObject(nint gdiObject, int size, out BITMAP bitmap);

    [LibraryImport("gdi32.dll")]
    public static partial int GetDIBits(nint hdc, nint bitmap, uint startScan, uint scanLines, [Out] byte[] bits, ref BITMAPINFO info, uint usage);

    // ---- shell32 ----------------------------------------------------------------------------
    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);

    // Returning COM interfaces needs the built-in marshaller, so these stay DllImport.
#pragma warning disable SYSLIB1054
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? item);

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderItem(
        ref Guid folderId, uint flags, nint token, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? item);
#pragma warning restore SYSLIB1054

    // ---- Helpers ----------------------------------------------------------------------------

    /// <summary>File-system path of a known folder (FOLDERID_*), or null when it has none.</summary>
    public static string? GetKnownFolderPath(Guid folderId)
    {
        var hr = SHGetKnownFolderPath(in folderId, 0, 0, out var buffer);
        try
        {
            return hr >= 0 && buffer != 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            // The buffer must be freed even when the call fails; freeing null is a no-op.
            Marshal.FreeCoTaskMem(buffer);
        }
    }
}
