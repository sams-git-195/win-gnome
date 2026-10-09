using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class CommandLineOptionsTests
{
    [Fact]
    public void Parse_SettingsPanel_OpensSettingsAtThatPanel()
    {
        var options = CommandLineOptions.Parse(["--settings-panel", "sound"]);

        Assert.True(options.OpenSettings);
        Assert.Equal("sound", options.SettingsPanel);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void Parse_SettingsAlone_OpensTheDefaultPanel()
    {
        var options = CommandLineOptions.Parse(["--settings"]);

        Assert.True(options.OpenSettings);
        Assert.Null(options.SettingsPanel);
    }

    [Fact]
    public void Parse_SettingsPanelWithoutAValue_IsUnknown()
    {
        var options = CommandLineOptions.Parse(["--settings-panel"]);

        Assert.False(options.OpenSettings);
        Assert.Null(options.SettingsPanel);
        Assert.Equal(["--settings-panel"], options.Unknown);
    }

    [Fact]
    public void Parse_CombinesWithOtherSwitches()
    {
        var options = CommandLineOptions.Parse(["--safe", "--settings-panel", "displays", "--settings-dir", @"C:\tmp\p"]);

        Assert.True(options.Safe);
        Assert.Equal("displays", options.SettingsPanel);
        Assert.Equal(@"C:\tmp\p", options.SettingsDirectory);
    }
}
