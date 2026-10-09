using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class LogoMaskRuleTests
{
    // Bgra32 pixel byte order is B, G, R, A. Gray8 mask is one byte per pixel.

    [Fact]
    public void MaxDimension_Is256()
    {
        Assert.Equal(256, LogoMaskRule.MaxDimension);
    }

    [Fact]
    public void Apply_ImageWithTransparency_UsesTheAlphaChannel()
    {
        // 2×1: an opaque pixel with non-zero RGB and a translucent pixel. Any alpha < 255 selects the alpha rule,
        // so RGB is ignored and the mask is exactly the alpha bytes.
        byte[] bgra = [10, 20, 30, 255, 0, 0, 0, 128];

        var mask = LogoMaskRule.Apply(bgra, 2, 1);

        Assert.Equal([255, 128], mask);
    }

    [Fact]
    public void Apply_FullyOpaqueImage_UsesInvertedLuminance()
    {
        // 2×1 opaque greys: luma of a grey equals its value (0.299+0.587+0.114 = 1), so the mask is 255 − grey.
        byte[] bgra = [200, 200, 200, 255, 0, 0, 0, 255];

        var mask = LogoMaskRule.Apply(bgra, 2, 1);

        Assert.Equal([55, 255], mask);
    }

    [Fact]
    public void Apply_OpaquePrimaryColours_UsesBt601Weights()
    {
        // 3×1 opaque red, green, blue. Pins each BT.601 coefficient: flipping R/B (or any) changes these bytes.
        //   red   255: 255 − round(0.299·255) = 255 − 76  = 179
        //   green 255: 255 − round(0.587·255) = 255 − 150 = 105
        //   blue  255: 255 − round(0.114·255) = 255 − 29  = 226
        byte[] bgra = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255];

        var mask = LogoMaskRule.Apply(bgra, 3, 1);

        Assert.Equal([179, 105, 226], mask);
    }

    [Fact]
    public void Apply_SingleTransparentPixelInAnOpaqueImage_TakesTheAlphaPath()
    {
        // 2×2, opaque grey 200 everywhere except the LAST pixel (alpha 100). The stray transparent pixel switches the
        // whole image to the alpha rule, giving a near-full-square mask (KI-103's degenerate case). An off-by-one
        // alpha scan that skips the last pixel would miss it and fall back to luma ([55,55,55,55]).
        byte[] bgra =
        [
            200, 200, 200, 255,
            200, 200, 200, 255,
            200, 200, 200, 255,
            200, 200, 200, 100,
        ];

        var mask = LogoMaskRule.Apply(bgra, 2, 2);

        Assert.Equal([255, 255, 255, 100], mask);
    }

    [Fact]
    public void Apply_EmptyImage_ReturnsEmptyMask()
    {
        var mask = LogoMaskRule.Apply([], 0, 0);

        Assert.Empty(mask);
    }
}
