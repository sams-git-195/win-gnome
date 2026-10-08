using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Theming;

public class TrafficLightAppearanceTests
{
    private static readonly TrafficLightColors Mac = TrafficLightPalette.ForPreset(TrafficLightPreset.MacOS);
    private static readonly TrafficLightColors Gnome = TrafficLightPalette.ForPreset(TrafficLightPreset.Gnome);

    private static HexColor Fill(
        CaptionButtonKind kind,
        bool enabled = true,
        bool active = true,
        bool hovered = false,
        bool dim = true,
        CaptionButtonInteraction interaction = CaptionButtonInteraction.None) =>
        TrafficLightAppearance.Fill(Mac, kind, enabled, active, hovered, dim, interaction);

    [Fact]
    public void ActiveWindow_UsesEachButtonsColour()
    {
        Assert.Equal(Mac.Close, Fill(CaptionButtonKind.Close));
        Assert.Equal(Mac.Minimize, Fill(CaptionButtonKind.Minimize));
        Assert.Equal(Mac.Maximize, Fill(CaptionButtonKind.Maximize));
    }

    [Fact]
    public void InactiveWindow_IsDimmedUntilTheGroupIsHovered()
    {
        Assert.Equal(Mac.Inactive, Fill(CaptionButtonKind.Close, active: false));
        Assert.Equal(Mac.Close, Fill(CaptionButtonKind.Close, active: false, hovered: true));
    }

    [Fact]
    public void InactiveWindow_KeepsColoursWhenDimmingIsOff()
    {
        Assert.Equal(Mac.Close, Fill(CaptionButtonKind.Close, active: false, dim: false));
    }

    [Fact]
    public void DisabledButton_IsAlwaysInactive()
    {
        Assert.Equal(Mac.Inactive, Fill(CaptionButtonKind.Maximize, enabled: false, hovered: true, interaction: CaptionButtonInteraction.Pressed));
    }

    [Fact]
    public void HoverAndPress_DarkenProgressively()
    {
        var normal = Fill(CaptionButtonKind.Close);
        var hovered = Fill(CaptionButtonKind.Close, hovered: true, interaction: CaptionButtonInteraction.Hovered);
        var pressed = Fill(CaptionButtonKind.Close, hovered: true, interaction: CaptionButtonInteraction.Pressed);

        Assert.True(hovered.RelativeLuminance < normal.RelativeLuminance);
        Assert.True(pressed.RelativeLuminance < hovered.RelativeLuminance);
        Assert.Equal(normal.A, pressed.A);
    }

    [Fact]
    public void Glyphs_FollowPresetAndSettings()
    {
        var hoverOnly = new WindowButtonSettings { ShowSymbolsOnHover = true };
        var always = new WindowButtonSettings { ShowSymbolsOnHover = false };

        Assert.False(TrafficLightAppearance.ShowGlyphs(Mac, hoverOnly, isGroupHovered: false));
        Assert.True(TrafficLightAppearance.ShowGlyphs(Mac, hoverOnly, isGroupHovered: true));
        Assert.True(TrafficLightAppearance.ShowGlyphs(Mac, always, isGroupHovered: false));
        Assert.True(TrafficLightAppearance.ShowGlyphs(Gnome, hoverOnly, isGroupHovered: false));
    }
}
