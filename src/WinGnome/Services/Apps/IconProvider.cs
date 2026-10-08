using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using WinGnome.Core.Collections;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services.Apps;

/// <summary>
/// Loads app and window icons through the shell (IShellItemImageFactory) with correct alpha, and caches
/// the most recently used per (id, size). Call from the UI thread (an STA thread with COM initialised).
/// </summary>
internal sealed class IconProvider : IIconProvider
{
    private const int MaxCacheEntries = 512;
    private const uint IconMessageTimeoutMs = 100;
    private const string AppsFolderPrefix = "shell:AppsFolder\\";

    /// <summary>
    /// Keyed by "size|id". Failed lookups are cached as null so they are not retried every frame. Least recently used
    /// entries go first, so a full cache does not reload every icon on screen at once.
    /// </summary>
    private readonly LruCache<string, ImageSource?> _cache = new(MaxCacheEntries, StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetAppIcon(string launchIdOrPath, int sizePx)
    {
        if (string.IsNullOrWhiteSpace(launchIdOrPath) || sizePx <= 0)
        {
            return null;
        }

        var id = launchIdOrPath.Trim();
        var key = sizePx.ToString(CultureInfo.InvariantCulture) + "|" + id;
        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }

        var icon = LoadShellImage(ToParsingName(id), sizePx);
        _cache.Set(key, icon);
        return icon;
    }

    public ImageSource? GetWindowIcon(nint hwnd, string? processPath, int sizePx)
    {
        // ApplicationFrameHost hosts every UWP window, so its own icon would be wrong for all of them.
        if (!string.IsNullOrWhiteSpace(processPath)
            && !Path.GetFileName(processPath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
            && GetAppIcon(processPath, sizePx) is { } exeIcon)
        {
            return exeIcon;
        }

        return hwnd == 0 ? null : GetIconFromWindow(hwnd);
    }

    /// <summary>File-system paths are used as-is; anything else is treated as an AppsFolder parsing name.</summary>
    private static string ToParsingName(string id)
    {
        if (id.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return id;
        }

        return Path.IsPathRooted(id) && (File.Exists(id) || Directory.Exists(id)) ? id : AppsFolderPrefix + id;
    }

    private static BitmapSource? LoadShellImage(string parsingName, int sizePx)
    {
        object? itemObject = null;
        try
        {
            var factoryId = typeof(IShellItemImageFactory).GUID;
            if (NativeMethods.SHCreateItemFromParsingName(parsingName, 0, ref factoryId, out itemObject) < 0
                || itemObject is not IShellItemImageFactory factory)
            {
                return null;
            }

            if (factory.GetImage(new SIZE(sizePx, sizePx), SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out var hbitmap) < 0 || hbitmap == 0)
            {
                return null;
            }

            try
            {
                return BitmapSourceFromDib(hbitmap);
            }
            finally
            {
                NativeMethods.DeleteObject(hbitmap);
            }
        }
        catch (COMException ex)
        {
            Log.Warn($"IconProvider: no icon for '{parsingName}'", ex);
            return null;
        }
        finally
        {
            if (itemObject is not null)
            {
                Marshal.ReleaseComObject(itemObject);
            }
        }
    }

    /// <summary>
    /// Copies a 32bpp bitmap into a frozen BitmapSource, keeping its alpha channel.
    /// (Imaging.CreateBitmapSourceFromHBitmap drops alpha, which gives icons black backgrounds.)
    /// </summary>
    private static BitmapSource? BitmapSourceFromDib(nint hbitmap)
    {
        if (NativeMethods.GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight == 0)
        {
            return null;
        }

        var width = bitmap.bmWidth;
        var height = Math.Abs(bitmap.bmHeight);
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // Negative height asks GetDIBits for top-down rows, as WPF expects.
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };

        var stride = width * 4;
        var pixels = new byte[stride * height];
        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
        {
            return null;
        }

        int lines;
        try
        {
            lines = NativeMethods.GetDIBits(screenDc, hbitmap, 0, (uint)height, pixels, ref info, NativeMethods.DIB_RGB_COLORS);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(0, screenDc);
        }

        if (lines != height)
        {
            return null;
        }

        var image = BitmapSource.Create(width, height, 96, 96, DetectPixelFormat(pixels), null, pixels, stride);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Icons from IShellItemImageFactory carry straight (non-premultiplied) alpha. A 32bpp bitmap whose
    /// alpha is zero everywhere has no alpha channel at all (classic 24-bit icons) and must be drawn opaque.
    /// </summary>
    private static PixelFormat DetectPixelFormat(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                return PixelFormats.Bgra32;
            }
        }

        return PixelFormats.Bgr32;
    }

    /// <summary>The window's own icon (WM_GETICON, then the class icon). These icons are not ours to destroy.</summary>
    private static BitmapSource? GetIconFromWindow(nint hwnd)
    {
        foreach (var type in (ReadOnlySpan<int>)[NativeMethods.ICON_BIG, NativeMethods.ICON_SMALL2, NativeMethods.ICON_SMALL])
        {
            if (NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_GETICON, type, 0,
                    NativeMethods.SMTO_ABORTIFHUNG, IconMessageTimeoutMs, out var hicon) != 0 && hicon != 0)
            {
                return BitmapSourceFromIcon(hicon);
            }
        }

        var classIcon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICON);
        if (classIcon == 0)
        {
            classIcon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICONSM);
        }

        return classIcon == 0 ? null : BitmapSourceFromIcon(classIcon);
    }

    private static BitmapSource? BitmapSourceFromIcon(nint hicon)
    {
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(hicon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            // The owning window may have destroyed the icon between our query and the copy.
            Log.Warn("IconProvider: could not convert a window icon", ex);
            return null;
        }
    }
}
