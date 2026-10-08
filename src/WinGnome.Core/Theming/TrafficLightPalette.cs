using WinGnome.Core.Settings;

namespace WinGnome.Core.Theming;

/// <summary>Maps <see cref="TrafficLightPreset"/> values (and custom settings colours) to concrete colours.</summary>
public static class TrafficLightPalette
{
    private static readonly HexColor MacInactive = HexColor.FromRgb(0xC8, 0xC8, 0xC8);
    private static readonly HexColor MacGlyph = new(0xB3, 0, 0, 0);
    private static readonly HexColor Border = new(0x33, 0, 0, 0);

    private static readonly TrafficLightColors MacOS = new(
        HexColor.FromRgb(0xFF, 0x5F, 0x57),
        HexColor.FromRgb(0xFE, 0xBC, 0x2E),
        HexColor.FromRgb(0x28, 0xC8, 0x40),
        MacInactive,
        MacGlyph,
        Border,
        AlwaysShowGlyphs: false);

    private static readonly TrafficLightColors Gnome = new(
        HexColor.FromRgb(0x4A, 0x4A, 0x4A),
        HexColor.FromRgb(0x4A, 0x4A, 0x4A),
        HexColor.FromRgb(0x4A, 0x4A, 0x4A),
        HexColor.FromRgb(0x3A, 0x3A, 0x3A),
        HexColor.FromRgb(0xFF, 0xFF, 0xFF),
        Border,
        AlwaysShowGlyphs: true);

    private static readonly TrafficLightColors Graphite = new(
        HexColor.FromRgb(0x8E, 0x8E, 0x93),
        HexColor.FromRgb(0x8E, 0x8E, 0x93),
        HexColor.FromRgb(0x8E, 0x8E, 0x93),
        MacInactive,
        HexColor.FromRgb(0x1C, 0x1C, 0x1E),
        Border,
        AlwaysShowGlyphs: false);

    private static readonly TrafficLightColors Pastel = new(
        HexColor.FromRgb(0xF4, 0xA6, 0xA6),
        HexColor.FromRgb(0xF6, 0xD5, 0x8E),
        HexColor.FromRgb(0xA9, 0xE3, 0xA0),
        MacInactive,
        MacGlyph,
        Border,
        AlwaysShowGlyphs: false);

    /// <summary>Every preset, in the order a settings page should list them.</summary>
    public static IReadOnlyList<TrafficLightPreset> Presets { get; } = Enum.GetValues<TrafficLightPreset>();

    /// <summary>Resolves the colours for the user's settings, reading the custom colours when the preset is Custom.</summary>
    public static TrafficLightColors For(WindowButtonSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Preset != TrafficLightPreset.Custom)
        {
            return ForPreset(s.Preset);
        }

        return MacOS with
        {
            Close = ParseOr(s.CloseColor, MacOS.Close),
            Minimize = ParseOr(s.MinimizeColor, MacOS.Minimize),
            Maximize = ParseOr(s.MaximizeColor, MacOS.Maximize),
        };
    }

    /// <summary>Colours for a built-in preset. Custom returns the macOS defaults (the settings defaults).</summary>
    public static TrafficLightColors ForPreset(TrafficLightPreset p) => p switch
    {
        TrafficLightPreset.Gnome => Gnome,
        TrafficLightPreset.Graphite => Graphite,
        TrafficLightPreset.Pastel => Pastel,
        _ => MacOS,
    };

    private static HexColor ParseOr(string? text, HexColor fallback) =>
        HexColor.TryParse(text, out var color) ? color : fallback;
}
