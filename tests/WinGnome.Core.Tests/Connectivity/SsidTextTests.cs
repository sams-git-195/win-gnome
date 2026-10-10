using System.Text;
using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class SsidTextTests
{
    [Fact]
    public void Display_Ascii_ReturnsTheText() =>
        Assert.Equal("Home WiFi", SsidText.Display(Encoding.ASCII.GetBytes("Home WiFi")));

    [Fact]
    public void Display_Utf8_DecodesIt() =>
        Assert.Equal("Café 📶", SsidText.Display(Encoding.UTF8.GetBytes("Café 📶")));

    [Fact]
    public void Display_Empty_ReturnsEmpty() => Assert.Equal("", SsidText.Display([]));

    [Fact]
    public void Display_ThirtyTwoBytes_KeepsAllOfThem() =>
        Assert.Equal(new string('x', 32), SsidText.Display(Encoding.ASCII.GetBytes(new string('x', 32))));

    [Fact]
    public void Display_InvalidUtf8_EscapesNonPrintableBytes() =>
        Assert.Equal("ab\\xFF\\xFEc", SsidText.Display([0x61, 0x62, 0xFF, 0xFE, 0x63]));

    [Fact]
    public void Display_ControlCharacter_EscapesItInsteadOfShowingIt() =>
        Assert.Equal("a\\x07b", SsidText.Display([0x61, 0x07, 0x62]));

    [Fact]
    public void ToHex_ReturnsUpperCaseHex() => Assert.Equal("486F6D65", SsidText.ToHex("Home"u8));

    [Theory]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0, 0x41, 0 }, false)]
    [InlineData(new byte[] { 0x20 }, false)]
    public void IsHidden_NoNameOrAllZeros_IsTrue(byte[] ssid, bool expected) =>
        Assert.Equal(expected, SsidText.IsHidden(ssid));
}
