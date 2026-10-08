using WinGnome.Core.Theming;

namespace WinGnome.Core.Tweaks;

/// <summary>Builds the registry values that make a colour Windows' accent colour.</summary>
public static class AccentColorChanges
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string DesktopKey = @"Control Panel\Desktop";

    /// <summary>DWM's colourisation colour keeps the 0xC4 alpha Windows writes for it.</summary>
    private const byte ColorizationAlpha = 0xC4;

    /// <summary>Index of the first shade in the palette; Windows uses it as the Start and taskbar colour.</summary>
    private const int StartShadeIndex = 4;

    /// <summary>Windows' eighth palette swatch. It does not follow the accent (it is not derived from it), so it is left as a constant.</summary>
    private static readonly byte[] FixedSwatch = [0x88, 0x17, 0x98, 0x00];

    /// <summary>How far each palette swatch sits from the base colour: tints toward white, then shades toward black.</summary>
    private static readonly (bool TowardWhite, double Amount)[] Ladder =
    [
        (true, 0.60), (true, 0.40), (true, 0.20), (true, 0.00), (false, 0.20), (false, 0.40), (false, 0.60),
    ];

    /// <summary>
    /// The writes that set <paramref name="accent"/> as a fixed (not wallpaper-derived) accent colour. Windows builds
    /// its palette with a private algorithm, so the tints and shades here are an approximation of it. The alpha of
    /// <paramref name="accent"/> is ignored.
    /// </summary>
    public static IReadOnlyList<RegistryChange> For(HexColor accent)
    {
        var swatches = Ladder.Select(step => Swatch(accent, step)).ToArray();
        var palette = swatches.SelectMany(s => new byte[] { s.R, s.G, s.B, 0 }).Concat(FixedSwatch).ToArray();

        return
        [
            DWord(AccentKey, "AccentColorMenu", Abgr(accent)),
            DWord(AccentKey, "StartColorMenu", Abgr(swatches[StartShadeIndex])),
            new RegistryChange(AccentKey, "AccentPalette", RegistryValue.Binary(palette)),
            DWord(DwmKey, "AccentColor", Abgr(accent)),
            DWord(DwmKey, "ColorizationColor", Argb(ColorizationAlpha, accent)),
            // Without this Windows replaces the accent with one taken from the wallpaper at the next theme refresh.
            DWord(DesktopKey, "AutoColorization", 0),
        ];
    }

    private static HexColor Swatch(HexColor accent, (bool TowardWhite, double Amount) step) =>
        accent.Blend(step.TowardWhite ? new HexColor(255, 255, 255, 255) : new HexColor(255, 0, 0, 0), step.Amount);

    private static RegistryChange DWord(string subKey, string name, int value) =>
        new(subKey, name, RegistryValue.DWord(value));

    private static int Abgr(HexColor c) => unchecked((int)(0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R));

    private static int Argb(byte alpha, HexColor c) => unchecked((int)(((uint)alpha << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
}
