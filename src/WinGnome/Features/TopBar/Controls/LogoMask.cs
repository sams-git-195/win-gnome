using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// Thin app-layer glue that decodes a custom logo file into the frozen Gray8 mask <see cref="LogoGlyph"/> draws
/// (spec 0021): the file is read once and let go (never locked), WIC downscales during the decode to bound transient
/// memory, and the pixel decision itself lives in Core's <see cref="LogoMaskRule"/>. Any failure returns false so the
/// caller can fall back to the Windows mark and log once.
/// </summary>
internal static class LogoMask
{
    /// <summary>Rejects files larger than this before decoding; it bounds the compressed file (the 256 px decode cap bounds the transient).</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Decodes <paramref name="path"/> to a frozen Gray8 mask (at most <see cref="LogoMaskRule.MaxDimension"/> px on a
    /// side). Returns false — with <paramref name="mask"/> null — for a missing, oversized, unreadable or non-image
    /// file; the caller logs and falls back.
    /// </summary>
    public static bool TryDecode(string path, [NotNullWhen(true)] out BitmapSource? mask)
    {
        mask = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSizeBytes)
            {
                return false;
            }

            // A cheap metadata read of the frame we will decode — ICO's Frames[0] (sub-images are stored
            // largest-first in practice), a GIF's first frame — so we can decode it down to fit the cap while
            // preserving its aspect ratio. (Setting both DecodePixelWidth and DecodePixelHeight to the cap would make
            // WPF decode to exactly that square and distort a non-square image; only a single decode dimension keeps
            // the aspect, so we compute the fitted size ourselves and stay within the cap on both sides.)
            int sourceWidth;
            int sourceHeight;
            using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Not disposable itself; disposing the stream is what releases the file.
                var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
                if (decoder.Frames.Count == 0)
                {
                    return false;
                }

                sourceWidth = decoder.Frames[0].PixelWidth;
                sourceHeight = decoder.Frames[0].PixelHeight;
            }

            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return false;
            }

            // Fit inside the cap, never upscaling: a small image decodes at its own size (it upscales softly at draw
            // time, which the spec accepts), a large one downscales during the decode (bounding transient memory).
            var scale = Math.Min(1, (double)LogoMaskRule.MaxDimension / Math.Max(sourceWidth, sourceHeight));
            var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

            // OnLoad reads the whole file during EndInit, so the stream can be disposed straight after and the file is
            // never left locked. IgnoreColorProfile avoids a colour-management detour.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = targetWidth;
            image.DecodePixelHeight = targetHeight;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            // CopyPixels to Bgra32 so Core sees a known byte order, run the mask rule, emit a frozen Gray8 bitmap.
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            bgra.Freeze();
            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);
            var gray = LogoMaskRule.Apply(pixels, width, height);

            var result = BitmapSource.Create(width, height, image.DpiX, image.DpiY, PixelFormats.Gray8, null, gray, width);
            result.Freeze();
            mask = result;
            return true;
        }
        catch (Exception)
        {
            // WIC surfaces decode problems as several exception types (IO, unsupported format, corrupt data, OOM);
            // any of them means "no mask", which the caller turns into one warning and the Windows-mark fallback.
            return false;
        }
    }
}
