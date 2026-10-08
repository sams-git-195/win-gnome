using WinGnome.Core.Input;

namespace WinGnome.Core.Tests.Input;

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+F1", HotkeyModifiers.Alt, 0x70)]
    [InlineData("alt+f1", HotkeyModifiers.Alt, 0x70)]
    [InlineData("Ctrl+Alt+T", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x54)]
    [InlineData("control+shift+z", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x5A)]
    [InlineData("Win+Space", HotkeyModifiers.Win, 0x20)]
    [InlineData("Super+Tab", HotkeyModifiers.Win, 0x09)]
    [InlineData("Meta+1", HotkeyModifiers.Win, 0x31)]
    [InlineData("Ctrl+Alt+Shift+Win+Delete", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x2E)]
    [InlineData("  Ctrl + Alt + T  ", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x54)]
    [InlineData("Ctrl+Return", HotkeyModifiers.Control, 0x0D)]
    [InlineData("Alt+Escape", HotkeyModifiers.Alt, 0x1B)]
    [InlineData("Ctrl+PageDown", HotkeyModifiers.Control, 0x22)]
    [InlineData("Shift+Grave", HotkeyModifiers.Shift, 0xC0)]
    public void TryParse_ParsesValidHotkeys(string text, HotkeyModifiers modifiers, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(vk, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("f12")]
    [InlineData("F24")]
    public void TryParse_AllowsBareFunctionKeys(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(HotkeyModifiers.None, hotkey.Modifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("A")]
    [InlineData("Space")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Control+Ctrl+A")]
    [InlineData("Foo+A")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F25")]
    [InlineData("F25")]
    public void TryParse_RejectsInvalidHotkeys(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(default, hotkey);
    }

    [Theory]
    [InlineData("Alt+F1")]
    [InlineData("Ctrl+Alt+T")]
    [InlineData("Ctrl+Alt+Shift+Win+Delete")]
    [InlineData("Win+Space")]
    [InlineData("Shift+PageUp")]
    [InlineData("F5")]
    [InlineData("Ctrl+9")]
    [InlineData("Alt+Comma")]
    public void ToString_RoundTripsThroughTryParse(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(text, hotkey.ToString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Theory]
    [InlineData("Shift+Alt+Ctrl+T", "Ctrl+Alt+Shift+T")]
    [InlineData("Super+Spacebar", "Win+Space")]
    [InlineData("Ctrl+Return", "Ctrl+Enter")]
    [InlineData("Alt+Escape", "Alt+Esc")]
    [InlineData("control+a", "Ctrl+A")]
    public void ToString_UsesCanonicalNamesAndModifierOrder(string input, string canonical)
    {
        Assert.True(Hotkey.TryParse(input, out var hotkey));
        Assert.Equal(canonical, hotkey.ToString());
    }

    [Fact]
    public void ToString_UnknownVirtualKey_FallsBackToHex()
    {
        Assert.Equal("Alt+0xFF", new Hotkey(HotkeyModifiers.Alt, 0xFF).ToString());
    }

    [Fact]
    public void Modifiers_MatchWin32Flags()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Control);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Win);
    }
}
