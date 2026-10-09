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

/// <summary>SHELLEXECUTEINFOW. The hIcon/hMonitor union is declared as its one pointer-sized member.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHELLEXECUTEINFO
{
    public int cbSize;
    public uint fMask;
    public nint hwnd;
    public string? lpVerb;
    public string? lpFile;
    public string? lpParameters;
    public string? lpDirectory;
    public int nShow;
    public nint hInstApp;
    public nint lpIDList;
    public string? lpClass;
    public nint hkeyClass;
    public uint dwHotKey;
    public nint hIconOrMonitor;
    public nint hProcess;
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

    // ---- ShellExecuteEx ---------------------------------------------------------------------
    /// <summary>Use lpIDList and invoke the verb through the item's context menu (needed for "runas" on AppsFolder items).</summary>
    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    /// <summary>Finish the launch before returning, so the calling thread may exit straight after.</summary>
    public const uint SEE_MASK_NOASYNC = 0x00000100;

    /// <summary>Don't show an error message box when the launch fails.</summary>
    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;

    /// <summary>Win32 error for "the operation was cancelled by the user", e.g. "No" on a UAC prompt.</summary>
    public const int ERROR_CANCELLED = 1223;

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

    // SHELLEXECUTEINFO carries strings, so it is not blittable and needs the built-in marshaller too.
    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
#pragma warning restore SYSLIB1054

    /// <summary>Parses a shell name ("shell:AppsFolder\...") to an absolute ID list; free it with Marshal.FreeCoTaskMem.</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHParseDisplayName(string name, nint bindContext, out nint idList, uint attributesIn, out uint attributesOut);

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
