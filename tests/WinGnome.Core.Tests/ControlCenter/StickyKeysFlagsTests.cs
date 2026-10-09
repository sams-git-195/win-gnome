using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class StickyKeysFlagsTests
{
    [Theory]
    // This machine's read on 25H2: shortcut turned off by the user (no HOTKEYACTIVE), sounds and indicator on.
    [InlineData(0x1F2u, true, 0x1F3u)]
    [InlineData(0x1F3u, false, 0x1F2u)]
    // Windows' out-of-box flags: shortcut and confirmation on.
    [InlineData(0x1FAu, true, 0x1FBu)]
    [InlineData(0x1FBu, false, 0x1FAu)]
    // Already in the requested state: unchanged.
    [InlineData(0x1FBu, true, 0x1FBu)]
    [InlineData(0x1FAu, false, 0x1FAu)]
    public void With_ValidRead_ChangesOnlyTheOnBit(uint current, bool on, uint expected)
    {
        Assert.Equal(expected, StickyKeysFlags.With(current, on));
    }

    [Theory]
    [InlineData(0x0u, true, 0x0Fu)]
    [InlineData(0x0u, false, 0x0Eu)]
    // Bits without AVAILABLE are not trusted: the whole read is replaced by Windows' defaults.
    [InlineData(0x1F1u, false, 0x0Eu)]
    public void With_ReadWithoutAvailable_UsesWindowsDefaultsSoTheShortcutStillWorks(uint current, bool on, uint expected)
    {
        Assert.Equal(expected, StickyKeysFlags.With(current, on));
    }

    [Fact]
    public void Defaults_AreAvailableHotkeyActiveAndConfirmHotkey()
    {
        Assert.Equal(0x0Eu, StickyKeysFlags.Defaults);
    }

    [Theory]
    [InlineData(0x1F3u, true)]
    [InlineData(0x1F2u, false)]
    [InlineData(0x0u, false)]
    public void IsOn_ReadsTheOnBit(uint flags, bool expected)
    {
        Assert.Equal(expected, StickyKeysFlags.IsOn(flags));
    }
}
