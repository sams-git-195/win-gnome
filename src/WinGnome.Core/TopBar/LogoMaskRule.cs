namespace WinGnome.Core.TopBar;

/// <summary>
/// The pixel rule that turns a custom logo image into a solid-silhouette mask (spec 0021). Pure logic over raw
/// pixels, so it is testable without WPF: an image with any real transparency uses its alpha channel; a fully opaque
/// image uses inverted BT.601 luminance (a dark mark on a light background becomes the silhouette).
/// </summary>
public static class LogoMaskRule
{
    /// <summary>
    /// The largest dimension (in pixels) the app layer decodes a custom image to. WIC downscales during the decode,
    /// bounding the transient memory; the frozen Gray8 result is at most 256×256 = 64 KB.
    /// </summary>
    public const int MaxDimension = 256;

    /// <summary>
    /// Converts tightly packed Bgra32 pixels (4 bytes each, row-major, no padding) to a Gray8 mask (1 byte each).
    /// </summary>
    /// <param name="bgra">The source pixels; length must be at least <c>width * height * 4</c>.</param>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <returns>The mask bytes, <c>width * height</c> long.</returns>
    public static byte[] Apply(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        var count = width * height;
        if (bgra.Length < count * 4)
        {
            throw new ArgumentException("The pixel buffer holds fewer than width × height Bgra32 pixels.", nameof(bgra));
        }

        var mask = new byte[count];

        // Mode selection: a single pixel with real transparency makes the whole image use its alpha channel as the
        // mask (RGB ignored). The scan covers every pixel including the last, so one stray transparent pixel — even
        // at the very end — still selects the alpha rule (see KI-103's degenerate case).
        var anyTransparent = false;
        for (var p = 0; p < count; p++)
        {
            if (bgra[(p * 4) + 3] < 255)
            {
                anyTransparent = true;
                break;
            }
        }

        if (anyTransparent)
        {
            for (var p = 0; p < count; p++)
            {
                mask[p] = bgra[(p * 4) + 3];
            }

            return mask;
        }

        // Fully opaque: inverted BT.601 luminance, so a dark mark on a light background becomes the silhouette and
        // antialiased edges survive. Bgra32 byte order is B, G, R, A.
        for (var p = 0; p < count; p++)
        {
            int b = bgra[p * 4];
            int g = bgra[(p * 4) + 1];
            int r = bgra[(p * 4) + 2];
            var luma = (int)Math.Round((0.299 * r) + (0.587 * g) + (0.114 * b), MidpointRounding.AwayFromZero);
            mask[p] = (byte)(255 - luma);
        }

        return mask;
    }
}
