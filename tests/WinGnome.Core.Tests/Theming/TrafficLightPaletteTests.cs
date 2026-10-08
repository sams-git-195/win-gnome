using WinGnome.Core.Settings;
using WinGnome.Core.Theming;

namespace WinGnome.Core.Tests.Theming;

public class TrafficLightPaletteTests
{
    [Fact]
    public void Presets_ListsEveryPresetOnce()
    {
        var all = Enum.GetValues<TrafficLightPreset>();

        Assert.Equal(all.Length, TrafficLightPalette.Presets.Count);
        Assert.Equal(all.OrderBy(p => p), TrafficLightPalette.Presets.OrderBy(p => p));
    }

    [Fact]
    public void MacOS_UsesTheClassicColours()
    {
        var c = TrafficLightPalette.ForPreset(TrafficLightPreset.MacOS);

        Assert.Equal(HexColor.Parse("#FF5F57"), c.Close);
        Assert.Equal(HexColor.Parse("#FEBC2E"), c.Minimize);
        Assert.Equal(HexColor.Parse("#28C840"), c.Maximize);
        Assert.Equal(HexColor.Parse("#C8C8C8"), c.Inactive);
        Assert.False(c.AlwaysShowGlyphs);
        Assert.Equal(0xB3, c.Glyph.A);
        Assert.Equal(HexColor.Parse("#33000000"), c.Border);
    }

    [Fact]
    public void Gnome_IsNeutralWithAlwaysVisibleWhiteGlyphs()
    {
        var c = TrafficLightPalette.ForPreset(TrafficLightPreset.Gnome);

        Assert.Equal(HexColor.Parse("#5E5E5E"), c.Close);
        Assert.Equal(c.Close, c.Minimize);
        Assert.Equal(c.Close, c.Maximize);
        Assert.Equal(HexColor.Parse("#FFFFFF"), c.Glyph);
        Assert.Equal(HexColor.Parse("#484848"), c.Inactive);
        Assert.True(c.AlwaysShowGlyphs);
    }

    [Fact]
    public void Graphite_IsMonochrome()
    {
        var c = TrafficLightPalette.ForPreset(TrafficLightPreset.Graphite);

        Assert.Equal(HexColor.Parse("#8E8E93"), c.Close);
        Assert.Equal(c.Close, c.Minimize);
        Assert.Equal(c.Close, c.Maximize);
        Assert.Equal(HexColor.Parse("#1C1C1E"), c.Glyph);
        Assert.Equal(HexColor.Parse("#C8C8C8"), c.Inactive);
    }

    [Fact]
    public void Pastel_UsesSoftColours()
    {
        var c = TrafficLightPalette.ForPreset(TrafficLightPreset.Pastel);

        Assert.Equal(HexColor.Parse("#F4A6A6"), c.Close);
        Assert.Equal(HexColor.Parse("#F6D58E"), c.Minimize);
        Assert.Equal(HexColor.Parse("#A9E3A0"), c.Maximize);
    }

    [Fact]
    public void ForPreset_Custom_ReturnsMacDefaults()
    {
        Assert.Equal(
            TrafficLightPalette.ForPreset(TrafficLightPreset.MacOS),
            TrafficLightPalette.ForPreset(TrafficLightPreset.Custom));
    }

    [Fact]
    public void AllPresets_HaveTheSameBorder()
    {
        var borders = TrafficLightPalette.Presets.Select(p => TrafficLightPalette.ForPreset(p).Border).Distinct();
        Assert.Single(borders);
    }

    [Theory]
    [InlineData(TrafficLightPreset.MacOS)]
    [InlineData(TrafficLightPreset.Gnome)]
    [InlineData(TrafficLightPreset.Graphite)]
    [InlineData(TrafficLightPreset.Pastel)]
    public void For_BuiltInPresetIgnoresCustomColours(TrafficLightPreset preset)
    {
        var settings = new WindowButtonSettings { Preset = preset, CloseColor = "#123456" };

        Assert.Equal(TrafficLightPalette.ForPreset(preset), TrafficLightPalette.For(settings));
    }

    [Fact]
    public void For_Custom_UsesSettingsColours_WithMacInactiveAndGlyph()
    {
        var settings = new WindowButtonSettings
        {
            Preset = TrafficLightPreset.Custom,
            CloseColor = "#112233",
            MinimizeColor = "#445566",
            MaximizeColor = "#778899",
        };

        var c = TrafficLightPalette.For(settings);
        var mac = TrafficLightPalette.ForPreset(TrafficLightPreset.MacOS);

        Assert.Equal(HexColor.Parse("#112233"), c.Close);
        Assert.Equal(HexColor.Parse("#445566"), c.Minimize);
        Assert.Equal(HexColor.Parse("#778899"), c.Maximize);
        Assert.Equal(mac.Inactive, c.Inactive);
        Assert.Equal(mac.Glyph, c.Glyph);
        Assert.Equal(mac.Border, c.Border);
        Assert.Equal(mac.AlwaysShowGlyphs, c.AlwaysShowGlyphs);
    }

    [Fact]
    public void For_Custom_InvalidColoursFallBackToMacDefaults()
    {
        var settings = new WindowButtonSettings
        {
            Preset = TrafficLightPreset.Custom,
            CloseColor = "nope",
            MinimizeColor = "",
            MaximizeColor = "#00FF00",
        };

        var c = TrafficLightPalette.For(settings);

        Assert.Equal(HexColor.Parse("#FF5F57"), c.Close);
        Assert.Equal(HexColor.Parse("#FEBC2E"), c.Minimize);
        Assert.Equal(HexColor.Parse("#00FF00"), c.Maximize);
    }

    [Fact]
    public void For_Custom_AcceptsAlphaColours()
    {
        var settings = new WindowButtonSettings { Preset = TrafficLightPreset.Custom, CloseColor = "#80FF0000" };
        Assert.Equal(0x80, TrafficLightPalette.For(settings).Close.A);
    }
}
