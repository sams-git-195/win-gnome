using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class ContrastThemesTests
{
    [Fact]
    public void All_ListsWindows11sFourThemesInSettingsOrder()
    {
        Assert.Equal(
            [
                new ContrastTheme("Aquatic", "hcblack.theme", "High Contrast Black"),
                new ContrastTheme("Desert", "hcwhite.theme", "High Contrast White"),
                new ContrastTheme("Dusk", "hc1.theme", "High Contrast #1"),
                new ContrastTheme("Night sky", "hc2.theme", "High Contrast #2"),
            ],
            ContrastThemes.All);
    }

    [Theory]
    [InlineData("High Contrast Black", "Aquatic")]
    [InlineData("high contrast white", "Desert")]
    [InlineData("Dusk", "Dusk")]
    [InlineData("NIGHT SKY", "Night sky")]
    [InlineData("hc1.theme", "Dusk")]
    [InlineData(@"C:\WINDOWS\resources\Ease of Access Themes\hcblack.theme", "Aquatic")]
    [InlineData(@"%WINDIR%\Resources\Ease of Access Themes\HC2.THEME", "Night sky")]
    [InlineData("  High Contrast #2  ", "Night sky")]
    public void FromScheme_MatchesNameSchemeOrFile(string scheme, string expected)
    {
        Assert.Equal(expected, ContrastThemes.FromScheme(scheme)?.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("My scheme")]
    [InlineData("Contraste élevé n°1")]
    [InlineData(@"C:\Themes\hc3.theme")]
    public void FromScheme_UnknownOrEmpty_IsNull(string? scheme)
    {
        Assert.Null(ContrastThemes.FromScheme(scheme));
    }

    [Fact]
    public void FromDisplayName_IsExact()
    {
        Assert.Equal("hc2.theme", ContrastThemes.FromDisplayName("Night sky")?.FileName);
        Assert.Null(ContrastThemes.FromDisplayName("night sky"));
        Assert.Null(ContrastThemes.FromDisplayName(ContrastThemes.None));
        Assert.Null(ContrastThemes.FromDisplayName(null));
    }

    [Theory]
    [InlineData(false, "", "None")]
    [InlineData(false, "High Contrast Black", "None")]
    [InlineData(true, "High Contrast Black", "Aquatic")]
    [InlineData(true, @"C:\WINDOWS\resources\Ease of Access Themes\hcwhite.theme", "Desert")]
    public void ChoicesFor_KnownStates_SelectFromTheFixedList(bool on, string scheme, string expected)
    {
        var (choices, selected) = ContrastThemes.ChoicesFor(on, scheme);

        Assert.Equal(["None", "Aquatic", "Desert", "Dusk", "Night sky"], choices);
        Assert.Equal(expected, selected);
    }

    [Theory]
    [InlineData("My scheme", "My scheme")]
    [InlineData("", "Custom")]
    [InlineData(null, "Custom")]
    public void ChoicesFor_OnWithAnotherScheme_AddsItSoTheRowShowsWhatWindowsHas(string? scheme, string expected)
    {
        var (choices, selected) = ContrastThemes.ChoicesFor(true, scheme);

        Assert.Equal(["None", "Aquatic", "Desert", "Dusk", "Night sky", expected], choices);
        Assert.Equal(expected, selected);
    }
}
