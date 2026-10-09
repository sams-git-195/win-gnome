using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class SettingsActivationRequestTests
{
    [Theory]
    [InlineData("sound", "sound")]
    [InlineData("wingnome-dock", "wingnome-dock")]
    [InlineData("  displays \r\n", "displays")]
    [InlineData("power\r\nsound", "power")]
    public void Parse_PanelId_ReturnsTheTrimmedId(string content, string expected)
    {
        Assert.Equal(expected, SettingsActivationRequest.Parse(content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void Parse_Empty_ReturnsNull(string? content)
    {
        Assert.Null(SettingsActivationRequest.Parse(content));
    }

    [Theory]
    [InlineData("Sound")]
    [InlineData("sound panel")]
    [InlineData("..\\sound")]
    [InlineData("ms-settings:sound")]
    [InlineData("sound;calc.exe")]
    public void Parse_NotAPanelId_ReturnsNull(string content)
    {
        Assert.Null(SettingsActivationRequest.Parse(content));
    }

    [Fact]
    public void Parse_AtTheLengthLimit_ReturnsTheId()
    {
        Assert.Equal(new string('a', 64), SettingsActivationRequest.Parse(new string('a', 64)));
    }

    [Fact]
    public void Parse_OneOverTheLengthLimit_ReturnsNull()
    {
        Assert.Null(SettingsActivationRequest.Parse(new string('a', 65)));
    }

    [Theory]
    [InlineData("sound", "sound")]
    [InlineData(" sound ", "sound")]
    [InlineData(null, "")]
    public void Format_ReturnsTheFileContent(string? panelId, string expected)
    {
        Assert.Equal(expected, SettingsActivationRequest.Format(panelId));
    }

    [Fact]
    public void Format_ThenParse_RoundTripsThePanelId()
    {
        Assert.Equal("datetime", SettingsActivationRequest.Parse(SettingsActivationRequest.Format("datetime")));
    }

    [Fact]
    public void Format_ThenParse_NoPanel_ReturnsNull()
    {
        Assert.Null(SettingsActivationRequest.Parse(SettingsActivationRequest.Format(null)));
    }
}
