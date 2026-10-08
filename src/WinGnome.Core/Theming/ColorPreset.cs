namespace WinGnome.Core.Theming;

/// <summary>A named swatch offered next to a colour editor.</summary>
/// <param name="Name">Short label, also the swatch tooltip.</param>
/// <param name="Hex">The colour as "#RRGGBB".</param>
/// <param name="SuggestedForeground">Text colour that reads well on this colour, applied together with it where a page supports that.</param>
public sealed record ColorPreset(string Name, string Hex, string? SuggestedForeground = null);

/// <summary>The preset swatches shown by the settings colour editors.</summary>
public static class ColorPresets
{
    private const string DarkText = "#2E3436";
    private const string LightText = "#FFFFFF";

    /// <summary>Top bar background choices; each suggests a text colour that stays readable on it.</summary>
    public static IReadOnlyList<ColorPreset> TopBarBackgrounds { get; } =
    [
        new("Black", "#000000", LightText),
        new("Adwaita dark", "#303030", LightText),
        new("Adwaita light", "#EBEBEB", DarkText),
        new("White", "#FFFFFF", DarkText),
    ];

    /// <summary>Dock background choices.</summary>
    public static IReadOnlyList<ColorPreset> DockBackgrounds { get; } =
    [
        new("Black", "#000000"),
        new("Adwaita dark", "#242424"),
        new("Adwaita light", "#EBEBEB"),
        new("White", "#FFFFFF"),
    ];

    /// <summary>Text and icon colour choices.</summary>
    public static IReadOnlyList<ColorPreset> Text { get; } =
    [
        new("White", LightText),
        new("Light grey", "#DEDDDA"),
        new("Dark grey", DarkText),
        new("Black", "#000000"),
    ];

    /// <summary>Accent colours for indicators (the Adwaita accent palette).</summary>
    public static IReadOnlyList<ColorPreset> Accents { get; } =
    [
        new("Blue", "#3584E4"),
        new("Teal", "#2190A4"),
        new("Green", "#3A944A"),
        new("Yellow", "#C88800"),
        new("Orange", "#ED5B00"),
        new("Red", "#E62D42"),
        new("Purple", "#9141AC"),
    ];
}
