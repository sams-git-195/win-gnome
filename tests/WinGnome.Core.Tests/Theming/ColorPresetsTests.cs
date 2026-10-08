using WinGnome.Core.Theming;

namespace WinGnome.Core.Tests.Theming;

public class ColorPresetsTests
{
    public static TheoryData<string> ListNames => new()
    {
        nameof(ColorPresets.TopBarBackgrounds),
        nameof(ColorPresets.DockBackgrounds),
        nameof(ColorPresets.Text),
        nameof(ColorPresets.Accents),
    };

    private static IReadOnlyList<ColorPreset> List(string name) => name switch
    {
        nameof(ColorPresets.TopBarBackgrounds) => ColorPresets.TopBarBackgrounds,
        nameof(ColorPresets.DockBackgrounds) => ColorPresets.DockBackgrounds,
        nameof(ColorPresets.Text) => ColorPresets.Text,
        _ => ColorPresets.Accents,
    };

    [Theory]
    [MemberData(nameof(ListNames))]
    public void EveryPreset_HasAValidOpaqueColourAndUniqueName(string listName)
    {
        var presets = List(listName);
        Assert.NotEmpty(presets);
        Assert.Equal(presets.Count, presets.Select(p => p.Name).Distinct().Count());
        foreach (var preset in presets)
        {
            Assert.True(HexColor.TryParse(preset.Hex, out var colour), $"{listName}/{preset.Name}");
            Assert.Equal(255, colour.A);
            if (preset.SuggestedForeground is not null)
            {
                Assert.True(HexColor.TryParse(preset.SuggestedForeground, out _), $"{listName}/{preset.Name} foreground");
            }
        }
    }

    [Fact]
    public void TopBarSuggestedForeground_ContrastsWithTheBackground()
    {
        foreach (var preset in ColorPresets.TopBarBackgrounds)
        {
            var background = HexColor.Parse(preset.Hex);
            var foreground = HexColor.Parse(preset.SuggestedForeground!);
            Assert.NotEqual(background.IsLight, foreground.IsLight);
        }
    }
}
