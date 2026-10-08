using WinGnome.Core.Theming;
using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class AccentColorChangesTests
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    private static readonly HexColor AdwaitaBlue = HexColor.Parse("#3584E4");

    private static Dictionary<(string SubKey, string? Name), RegistryValue> Map(HexColor color) =>
        AccentColorChanges.For(color).ToDictionary(c => (c.SubKey, c.ValueName), c => c.Value);

    [Fact]
    public void For_AdwaitaBlue_WritesTheBaseColourInAbgrAndArgb()
    {
        var map = Map(AdwaitaBlue);

        Assert.Equal(RegistryValue.DWord(unchecked((int)0xFFE48435)), map[(AccentKey, "AccentColorMenu")]);
        Assert.Equal(RegistryValue.DWord(unchecked((int)0xFFE48435)), map[(DwmKey, "AccentColor")]);
        Assert.Equal(RegistryValue.DWord(unchecked((int)0xC43584E4)), map[(DwmKey, "ColorizationColor")]);
    }

    [Fact]
    public void For_AdwaitaBlue_WritesThePaletteWithTintsBaseShadesAndTheFixedSwatch()
    {
        var map = Map(AdwaitaBlue);

        var expected = new byte[]
        {
            0xAE, 0xCE, 0xF4, 0x00,
            0x86, 0xB5, 0xEF, 0x00,
            0x5D, 0x9D, 0xE9, 0x00,
            0x35, 0x84, 0xE4, 0x00,
            0x2A, 0x6A, 0xB6, 0x00,
            0x20, 0x4F, 0x89, 0x00,
            0x15, 0x35, 0x5B, 0x00,
            0x88, 0x17, 0x98, 0x00,
        };
        Assert.Equal(RegistryValue.Binary(expected), map[(AccentKey, "AccentPalette")]);
    }

    [Fact]
    public void For_AdwaitaBlue_UsesTheFirstShadeForStartAndTaskbar()
    {
        Assert.Equal(RegistryValue.DWord(unchecked((int)0xFFB66A2A)), Map(AdwaitaBlue)[(AccentKey, "StartColorMenu")]);
    }

    [Fact]
    public void For_AnyColour_TurnsOffAccentFromWallpaper()
    {
        Assert.Equal(RegistryValue.DWord(0), Map(AdwaitaBlue)[(@"Control Panel\Desktop", "AutoColorization")]);
    }

    [Fact]
    public void For_WritesExactlySixValues()
    {
        Assert.Equal(6, AccentColorChanges.For(AdwaitaBlue).Count);
    }

    [Fact]
    public void For_Black_KeepsTheFixedSwatchAndAGreyTint()
    {
        var palette = Assert.IsType<byte[]>(Map(HexColor.FromRgb(0, 0, 0))[(AccentKey, "AccentPalette")].Data);

        Assert.Equal(32, palette.Length);
        Assert.Equal([0x99, 0x99, 0x99, 0x00], palette[..4]);
        Assert.Equal([0x00, 0x00, 0x00, 0x00], palette[12..16]);
        Assert.Equal([0x88, 0x17, 0x98, 0x00], palette[28..]);
    }

    [Fact]
    public void For_IgnoresTheAlphaOfTheInputColour()
    {
        Assert.Equal(Map(AdwaitaBlue), Map(HexColor.Parse("#803584E4")));
    }
}
