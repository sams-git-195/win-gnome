using System.Text.Json;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class SettingsSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEveryChangedValue()
    {
        var original = new AppSettings();
        original.General.Theme = ThemeMode.Light;
        original.General.CenterNewWindows = true;
        original.TopBar.ClockStyle = ClockStyle.TwelveHour;
        original.TopBar.Height = 40;
        original.Dock.Position = DockPosition.Right;
        original.Dock.Visibility = DockVisibility.Autohide;
        original.Dock.ClickAction = DockClickAction.Cycle;
        original.Dock.PinnedApps = [new PinnedApp { Name = "Calc", LaunchId = @"C:\Windows\System32\calc.exe", Arguments = "/x" }];
        original.WindowButtons.Preset = TrafficLightPreset.Pastel;
        original.WindowButtons.Side = ButtonSide.Left;
        original.WindowButtons.Order = ButtonOrder.CloseMinimizeMaximize;
        original.WindowButtons.ExcludedProcesses = ["code"];
        original.Activities.Hotkey = "Ctrl+Alt+T";
        original.Activities.SuperKeyOpensOverview = true;
        original.EnabledTweaks = ["dark-mode"];

        var copy = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        Assert.Equal(ThemeMode.Light, copy.General.Theme);
        Assert.True(copy.General.CenterNewWindows);
        Assert.Equal(ClockStyle.TwelveHour, copy.TopBar.ClockStyle);
        Assert.Equal(40, copy.TopBar.Height);
        Assert.Equal(DockPosition.Right, copy.Dock.Position);
        Assert.Equal(DockVisibility.Autohide, copy.Dock.Visibility);
        Assert.Equal(DockClickAction.Cycle, copy.Dock.ClickAction);
        var pinned = Assert.Single(copy.Dock.PinnedApps);
        Assert.Equal(@"C:\Windows\System32\calc.exe", pinned.LaunchId);
        Assert.Equal("/x", pinned.Arguments);
        Assert.Equal(TrafficLightPreset.Pastel, copy.WindowButtons.Preset);
        Assert.Equal(ButtonSide.Left, copy.WindowButtons.Side);
        Assert.Equal(ButtonOrder.CloseMinimizeMaximize, copy.WindowButtons.Order);
        Assert.Equal(["code"], copy.WindowButtons.ExcludedProcesses);
        Assert.Equal("Ctrl+Alt+T", copy.Activities.Hotkey);
        Assert.True(copy.Activities.SuperKeyOpensOverview);
        Assert.Equal(["dark-mode"], copy.EnabledTweaks);
    }

    [Fact]
    public void Serialize_WritesEnumsAsStrings()
    {
        var settings = new AppSettings();
        settings.Dock.Position = DockPosition.Left;
        settings.WindowButtons.Preset = TrafficLightPreset.Gnome;

        var json = SettingsSerializer.Serialize(settings);

        Assert.Contains("\"Position\": \"Left\"", json);
        Assert.Contains("\"Preset\": \"Gnome\"", json);
        Assert.Contains("\"Theme\": \"Dark\"", json);
    }

    [Fact]
    public void Serialize_IsIndented()
    {
        Assert.Contains('\n', SettingsSerializer.Serialize(new AppSettings()));
    }

    [Fact]
    public void Deserialize_AcceptsCommentsAndTrailingCommas()
    {
        const string json = """
            {
              // line comment
              "Dock": {
                "Position": "Right", /* block comment */
                "IconSize": 64,
              },
              "EnabledTweaks": ["dark-mode",],
            }
            """;

        var settings = SettingsSerializer.Deserialize(json);

        Assert.Equal(DockPosition.Right, settings.Dock.Position);
        Assert.Equal(64, settings.Dock.IconSize);
        Assert.Equal(["dark-mode"], settings.EnabledTweaks);
    }

    [Fact]
    public void Deserialize_PropertyNamesAreCaseInsensitive()
    {
        var settings = SettingsSerializer.Deserialize("""{ "dock": { "position": "Left" } }""");
        Assert.Equal(DockPosition.Left, settings.Dock.Position);
    }

    [Fact]
    public void Deserialize_EnumNamesAreCaseInsensitive()
    {
        var settings = SettingsSerializer.Deserialize("""{ "General": { "Theme": "light" } }""");
        Assert.Equal(ThemeMode.Light, settings.General.Theme);
    }

    [Fact]
    public void Deserialize_EmptyObject_GivesDefaults()
    {
        var settings = SettingsSerializer.Deserialize("{}");
        Assert.Equal(SettingsSerializer.Serialize(new AppSettings().Normalize()), SettingsSerializer.Serialize(settings));
    }

    [Fact]
    public void Deserialize_JsonNull_GivesDefaults()
    {
        var settings = SettingsSerializer.Deserialize("null");
        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
    }

    [Fact]
    public void Deserialize_PartialFile_KeepsDefaultsForMissingParts()
    {
        var settings = SettingsSerializer.Deserialize("""{ "TopBar": { "Height": 30 } }""");

        Assert.Equal(30, settings.TopBar.Height);
        Assert.True(settings.TopBar.ShowDate);
        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
        Assert.Equal(4, settings.Dock.PinnedApps.Count);
    }

    [Fact]
    public void Deserialize_NormalisesOutOfRangeValues()
    {
        var settings = SettingsSerializer.Deserialize("""{ "Dock": { "IconSize": 9999 }, "Activities": { "Hotkey": "bogus" } }""");

        Assert.Equal(128, settings.Dock.IconSize);
        Assert.Equal("Alt+F1", settings.Activities.Hotkey);
    }

    [Fact]
    public void Deserialize_ExplicitNullSection_IsRepaired()
    {
        var settings = SettingsSerializer.Deserialize("""{ "Dock": null, "EnabledTweaks": null }""");

        Assert.NotNull(settings.Dock);
        Assert.NotNull(settings.EnabledTweaks);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("""{ "Dock": { "Position": "Sideways" } }""")]
    [InlineData("""{ "Dock": { "IconSize": "wide" } }""")]
    public void Deserialize_Invalid_ThrowsJsonException(string json)
    {
        Assert.ThrowsAny<JsonException>(() => SettingsSerializer.Deserialize(json));
    }
}
