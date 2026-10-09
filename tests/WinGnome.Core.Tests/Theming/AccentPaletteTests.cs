using WinGnome.Core.Theming;

namespace WinGnome.Core.Tests.Theming;

public class AccentPaletteTests
{
    [Fact]
    public void FromWindows_NoValue_IsAdwaitaBlue()
    {
        Assert.Equal(HexColor.Parse("#3584E4"), AccentPalette.FromWindows(null));
    }

    [Theory]
    [InlineData(unchecked((int)0xFFAC4191), "#9141AC")] // GNOME purple as Windows stores it (ABGR)
    [InlineData(0x00E48435, "#3584E4")] // alpha byte zero is still opaque
    [InlineData(0x00000000, "#000000")]
    public void FromWindows_Abgr_IsRgb(int abgr, string expected)
    {
        Assert.Equal(HexColor.Parse(expected), AccentPalette.FromWindows(abgr));
    }

    [Theory]
    [InlineData("#3584E4", "#FFFFFF")] // blue
    [InlineData("#9141AC", "#FFFFFF")] // purple
    [InlineData("#C88800", "#FFFFFF")] // GNOME yellow: 3.0+ contrast, still white
    [InlineData("#FFFF00", "#CC000000")] // light custom accent gets dark text
    [InlineData("#FFFFFF", "#CC000000")]
    [InlineData("#949494", "#FFFFFF")] // contrast 3.03, the lightest grey above the threshold
    [InlineData("#959595", "#CC000000")] // one step lighter, 2.995: just below it
    public void ForegroundOn_PicksReadableLabel(string accent, string expected)
    {
        Assert.Equal(HexColor.Parse(expected), AccentPalette.ForegroundOn(HexColor.Parse(accent)));
    }
}
