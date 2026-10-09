namespace WinGnome.Core.Theming;

/// <summary>WinGnome's accent colour, taken from the one Windows uses so the Appearance panel's choice shows everywhere.</summary>
public static class AccentPalette
{
    /// <summary>Adwaita blue, used when Windows records no accent colour.</summary>
    public static readonly HexColor AdwaitaBlue = HexColor.FromRgb(0x35, 0x84, 0xE4);

    private static readonly HexColor White = HexColor.FromRgb(0xFF, 0xFF, 0xFF);

    // GNOME's dark text on light surfaces (#000000 at 80%), so a light accent keeps readable labels.
    private static readonly HexColor DarkText = new(0xCC, 0x00, 0x00, 0x00);

    // WCAG's minimum contrast for large and bold text: GNOME's own accents all pass it with white labels.
    private const double MinimumContrast = 3.0;

    /// <summary>
    /// The accent from Windows' DWM <c>AccentColor</c> value (ABGR, alpha ignored), or Adwaita blue when it is absent.
    /// Windows keeps this value current both for a fixed accent and for one picked from the wallpaper.
    /// </summary>
    public static HexColor FromWindows(int? dwmAccentAbgr) =>
        dwmAccentAbgr is { } abgr
            ? HexColor.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF))
            : AdwaitaBlue;

    /// <summary>The label colour on an accent fill: white, as in GNOME, unless the accent is too light for it.</summary>
    public static HexColor ForegroundOn(HexColor accent) =>
        ContrastWithWhite(accent) >= MinimumContrast ? White : DarkText;

    private static double ContrastWithWhite(HexColor color) => 1.05 / (color.RelativeLuminance + 0.05);
}
