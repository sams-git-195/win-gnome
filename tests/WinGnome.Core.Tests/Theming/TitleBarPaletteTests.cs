using WinGnome.Core.Theming;

namespace WinGnome.Core.Tests.Theming;

public class TitleBarPaletteTests
{
    [Fact]
    public void Header_IsAdwaita()
    {
        Assert.Equal("#303030", TitleBarPalette.Header(dark: true).ToString());
        Assert.Equal("#EBEBEB", TitleBarPalette.Header(dark: false).ToString());
        Assert.Equal("#FFFFFF", TitleBarPalette.Text(dark: true).ToString());
        Assert.Equal("#2E3436", TitleBarPalette.Text(dark: false).ToString());
    }

    [Fact]
    public void WindowsDefault_IsOpaque()
    {
        Assert.Equal(255, TitleBarPalette.WindowsDefault(dark: true).A);
        Assert.Equal(255, TitleBarPalette.WindowsDefault(dark: false).A);
    }

    [Fact]
    public void ToColorRef_IsBgrWithZeroHighByte()
    {
        Assert.Equal(0x00332211u, TitleBarPalette.ToColorRef(HexColor.FromRgb(0x11, 0x22, 0x33)));
        Assert.Equal(0x00332211u, TitleBarPalette.ToColorRef(new HexColor(0x80, 0x11, 0x22, 0x33)));
        Assert.Equal(0x00363432u, TitleBarPalette.ToColorRef(HexColor.Parse("#323436")));
    }

    [Fact]
    public void FromColorRef_RoundTrips()
    {
        var color = HexColor.FromRgb(0xEB, 0x30, 0x2E);

        Assert.Equal(color, TitleBarPalette.FromColorRef(TitleBarPalette.ToColorRef(color)));
        Assert.Equal(HexColor.FromRgb(0x11, 0x22, 0x33), TitleBarPalette.FromColorRef(0xFF332211));
    }
}
