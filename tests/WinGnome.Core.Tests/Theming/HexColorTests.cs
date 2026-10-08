using WinGnome.Core.Theming;

namespace WinGnome.Core.Tests.Theming;

public class HexColorTests
{
    [Theory]
    [InlineData("#FF5F57", 255, 0xFF, 0x5F, 0x57)]
    [InlineData("ff5f57", 255, 0xFF, 0x5F, 0x57)]
    [InlineData("  #ff5f57  ", 255, 0xFF, 0x5F, 0x57)]
    [InlineData("#f00", 255, 0xFF, 0x00, 0x00)]
    [InlineData("0a8", 255, 0x00, 0xAA, 0x88)]
    [InlineData("#80FF0000", 0x80, 0xFF, 0x00, 0x00)]
    [InlineData("#00000000", 0, 0, 0, 0)]
    [InlineData("#B3000000", 0xB3, 0, 0, 0)]
    public void TryParse_AcceptsValidForms(string text, int a, int r, int g, int b)
    {
        Assert.True(HexColor.TryParse(text, out var color));
        Assert.Equal(new HexColor((byte)a, (byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#1234")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("#GGGGGG")]
    [InlineData("#12 456")]
    [InlineData("0xFF0000")]
    [InlineData("red")]
    [InlineData("-12345")]
    public void TryParse_RejectsInvalidForms(string? text)
    {
        Assert.False(HexColor.TryParse(text, out var color));
        Assert.Equal(default, color);
    }

    [Fact]
    public void Parse_ThrowsFormatExceptionOnInvalid()
    {
        Assert.Throws<FormatException>(() => HexColor.Parse("nope"));
    }

    [Fact]
    public void Parse_ReturnsColourOnValid()
    {
        Assert.Equal(HexColor.FromRgb(1, 2, 3), HexColor.Parse("#010203"));
    }

    [Fact]
    public void FromRgb_IsOpaque()
    {
        Assert.Equal(255, HexColor.FromRgb(1, 2, 3).A);
    }

    [Theory]
    [InlineData("#FF5F57")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#80102030")]
    [InlineData("#00FFFFFF")]
    public void ToString_RoundTripsThroughParse(string text)
    {
        Assert.Equal(text, HexColor.Parse(text).ToString());
    }

    [Fact]
    public void ToString_OpaqueOmitsAlpha_AndIsUpperCase()
    {
        Assert.Equal("#0A0B0C", new HexColor(255, 10, 11, 12).ToString());
    }

    [Fact]
    public void ToString_TranslucentIncludesAlphaFirst()
    {
        Assert.Equal("#0A0B0C0D", new HexColor(10, 11, 12, 13).ToString());
    }

    [Fact]
    public void ShortForm_ExpandsAndFormatsCanonically()
    {
        Assert.Equal("#AABBCC", HexColor.Parse("abc").ToString());
    }

    [Fact]
    public void Luminance_OfBlackAndWhite()
    {
        Assert.Equal(0, HexColor.FromRgb(0, 0, 0).RelativeLuminance, 6);
        Assert.Equal(1, HexColor.FromRgb(255, 255, 255).RelativeLuminance, 6);
    }

    [Fact]
    public void Luminance_OrdersChannelsByPerceivedBrightness()
    {
        var red = HexColor.FromRgb(255, 0, 0).RelativeLuminance;
        var green = HexColor.FromRgb(0, 255, 0).RelativeLuminance;
        var blue = HexColor.FromRgb(0, 0, 255).RelativeLuminance;

        Assert.Equal(0.2126, red, 4);
        Assert.Equal(0.7152, green, 4);
        Assert.Equal(0.0722, blue, 4);
    }

    [Theory]
    [InlineData("#FFFFFF", true)]
    [InlineData("#FFFF00", true)]
    [InlineData("#808080", false)]
    [InlineData("#000000", false)]
    [InlineData("#1C1C1E", false)]
    [InlineData("#C8C8C8", true)]
    public void IsLight_UsesLuminanceThreshold(string text, bool expected)
    {
        Assert.Equal(expected, HexColor.Parse(text).IsLight);
    }

    [Fact]
    public void Blend_Endpoints()
    {
        var a = HexColor.FromRgb(10, 20, 30);
        var b = HexColor.FromRgb(200, 100, 50);

        Assert.Equal(a, a.Blend(b, 0));
        Assert.Equal(b, a.Blend(b, 1));
    }

    [Fact]
    public void Blend_Midpoint()
    {
        var mid = HexColor.FromRgb(0, 0, 0).Blend(HexColor.FromRgb(100, 200, 50), 0.5);
        Assert.Equal(HexColor.FromRgb(50, 100, 25), mid);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(double.NegativeInfinity)]
    public void Blend_ClampsBelowZero(double amount)
    {
        var a = HexColor.FromRgb(10, 20, 30);
        Assert.Equal(a, a.Blend(HexColor.FromRgb(200, 100, 50), amount));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(double.PositiveInfinity)]
    public void Blend_ClampsAboveOne(double amount)
    {
        var b = HexColor.FromRgb(200, 100, 50);
        Assert.Equal(b, HexColor.FromRgb(10, 20, 30).Blend(b, amount));
    }

    [Fact]
    public void Blend_InterpolatesAlpha()
    {
        var result = new HexColor(0, 0, 0, 0).Blend(new HexColor(200, 0, 0, 0), 0.5);
        Assert.Equal(100, result.A);
    }
}
