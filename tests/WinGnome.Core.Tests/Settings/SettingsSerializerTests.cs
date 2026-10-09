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
    public void Deserialize_PinWithoutRunAsAdministratorField_KeepsItOff()
    {
        // A settings file written before PinnedApp.RunAsAdministrator existed.
        var settings = SettingsSerializer.Deserialize(
            """{ "Dock": { "PinnedApps": [ { "Name": "Terminal", "LaunchId": "Microsoft.WindowsTerminal", "Arguments": null } ] } }""");

        var pin = Assert.Single(settings.Dock.PinnedApps);
        Assert.Equal("Microsoft.WindowsTerminal", pin.LaunchId);
        Assert.False(pin.RunAsAdministrator);
    }

    [Fact]
    public void RoundTrip_PreservesRunAsAdministrator()
    {
        var original = new AppSettings();
        original.Dock.PinnedApps = [new PinnedApp { Name = "Terminal", LaunchId = "Microsoft.WindowsTerminal", RunAsAdministrator = true }];

        var copy = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        Assert.True(Assert.Single(copy.Dock.PinnedApps).RunAsAdministrator);
    }

    [Fact]
    public void Deserialize_WindowButtonsWithoutCustomTitleBarField_KeepsItOff()
    {
        // A settings file written before DecorateCustomTitleBars existed.
        var settings = SettingsSerializer.Deserialize("""{ "WindowButtons": { "Enabled": true, "UnifyTitleBarColor": false } }""");

        Assert.False(settings.WindowButtons.DecorateCustomTitleBars);
        Assert.False(settings.WindowButtons.UnifyTitleBarColor);
    }

    [Fact]
    public void Deserialize_WindowButtonsWithoutWebTitleBarField_KeepsItOff()
    {
        // A settings file written before DecorateWebTitleBarButtons existed, with custom title bars on.
        var settings = SettingsSerializer.Deserialize("""{ "WindowButtons": { "Enabled": true, "DecorateCustomTitleBars": true } }""");

        Assert.False(settings.WindowButtons.DecorateWebTitleBarButtons);
        Assert.True(settings.WindowButtons.DecorateCustomTitleBars);
    }

    [Fact]
    public void RoundTrip_PreservesDecorateWebTitleBarButtons()
    {
        var original = new AppSettings();
        original.WindowButtons.DecorateWebTitleBarButtons = true;

        var copy = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        Assert.True(copy.WindowButtons.DecorateWebTitleBarButtons);
    }

    [Fact]
    public void RoundTrip_PreservesDecorateCustomTitleBars()
    {
        var original = new AppSettings();
        original.WindowButtons.DecorateCustomTitleBars = true;

        var copy = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        Assert.True(copy.WindowButtons.DecorateCustomTitleBars);
    }

    [Fact]
    public void Deserialize_TopBarWithoutFontFamilyField_UsesAdwaitaSans()
    {
        // A settings file written before TopBar.FontFamily existed.
        var settings = SettingsSerializer.Deserialize("""{ "TopBar": { "Enabled": true, "FontSize": 14 } }""");

        Assert.Equal(TopBarFont.AdwaitaSans, settings.TopBar.FontFamily);
        Assert.Equal(14, settings.TopBar.FontSize);
    }

    [Fact]
    public void RoundTrip_PreservesTopBarFontFamily()
    {
        var original = new AppSettings();
        original.TopBar.FontFamily = TopBarFont.SegoeUI;

        var json = SettingsSerializer.Serialize(original);
        var copy = SettingsSerializer.Deserialize(json);

        Assert.Contains("\"FontFamily\": \"SegoeUI\"", json);
        Assert.Equal(TopBarFont.SegoeUI, copy.TopBar.FontFamily);
    }

    [Fact]
    public void Deserialize_UnknownTopBarFontFamilyNumber_UsesAdwaitaSans()
    {
        var settings = SettingsSerializer.Deserialize("""{ "TopBar": { "FontFamily": 42 } }""");

        Assert.Equal(TopBarFont.AdwaitaSans, settings.TopBar.FontFamily);
    }

    [Fact]
    public void Deserialize_UnknownTopBarFontFamilyName_UsesAdwaitaSans_AndKeepsTheRest()
    {
        // A newer build may add fonts; an older build reading its file falls back for this setting alone.
        var settings = SettingsSerializer.Deserialize(
            """{ "TopBar": { "FontFamily": "Inter", "FontSize": 15 }, "Dock": { "Position": "Left" } }""");

        Assert.Equal(TopBarFont.AdwaitaSans, settings.TopBar.FontFamily);
        Assert.Equal(15, settings.TopBar.FontSize);
        Assert.Equal(DockPosition.Left, settings.Dock.Position);
    }

    [Fact]
    public void Deserialize_UnknownWindowButtonNames_UseTheirDefaults_AndKeepTheRest()
    {
        var settings = SettingsSerializer.Deserialize(
            """{ "WindowButtons": { "Preset": "Neon", "Side": "Top", "Order": "CloseMinimizeMaximize", "ExcludedProcesses": ["code"] } }""");

        Assert.Equal(TrafficLightPreset.MacOS, settings.WindowButtons.Preset);
        Assert.Equal(ButtonSide.Right, settings.WindowButtons.Side);
        Assert.Equal(ButtonOrder.CloseMinimizeMaximize, settings.WindowButtons.Order);
        Assert.Equal(["code"], settings.WindowButtons.ExcludedProcesses);
    }

    [Fact]
    public void Deserialize_UnknownBlurName_UsesEachPropertysOwnDefault()
    {
        // The same enum has different defaults on different settings.
        var settings = SettingsSerializer.Deserialize("""{ "TopBar": { "Blur": "Frosted" }, "Dock": { "Blur": "Frosted" } }""");

        Assert.Equal(BlurEffect.None, settings.TopBar.Blur);
        Assert.Equal(BlurEffect.Acrylic, settings.Dock.Blur);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("""{ "Name": "Left" }""")]
    [InlineData("""["Left"]""")]
    [InlineData("\"\"")]
    public void Deserialize_EnumOfTheWrongJsonType_UsesTheDefault_AndKeepsTheRest(string value)
    {
        var settings = SettingsSerializer.Deserialize($$"""{ "Dock": { "Position": {{value}}, "IconSize": 64 } }""");

        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
        Assert.Equal(64, settings.Dock.IconSize);
    }

    [Fact]
    public void Deserialize_KnownEnumNumber_StillReadsIt()
    {
        var settings = SettingsSerializer.Deserialize("""{ "Dock": { "Position": 2 } }""");

        Assert.Equal(DockPosition.Right, settings.Dock.Position);
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
    public void Deserialize_IntegerEnumValues_AreAcceptedWhenDefined_AndResetWhenNot()
    {
        var settings = SettingsSerializer.Deserialize("""{ "Dock": { "Position": 2, "Visibility": 99 }, "General": { "Theme": -1 } }""");

        Assert.Equal(DockPosition.Right, settings.Dock.Position);
        Assert.Equal(DockVisibility.Intellihide, settings.Dock.Visibility);
        Assert.Equal(ThemeMode.Dark, settings.General.Theme);
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
    [InlineData("""{ "Dock": { "IconSize": "wide" } }""")]
    public void Deserialize_Invalid_ThrowsJsonException(string json)
    {
        Assert.ThrowsAny<JsonException>(() => SettingsSerializer.Deserialize(json));
    }
}
