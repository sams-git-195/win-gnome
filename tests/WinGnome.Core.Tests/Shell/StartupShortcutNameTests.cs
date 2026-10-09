using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class StartupShortcutNameTests
{
    [Fact]
    public void For_PlainName_AddsLnk() =>
        Assert.Equal("Notepad.lnk", StartupShortcutName.For("Notepad", []));

    [Theory]
    [InlineData("A<B>C", "A_B_C.lnk")]
    [InlineData(@"Back\Slash/Forward", "Back_Slash_Forward.lnk")]
    [InlineData("What? *Now*: \"yes\" | no", "What_ _Now__ _yes_ _ no.lnk")]
    [InlineData("Tab\there", "Tab_here.lnk")]
    public void For_InvalidCharacters_AreReplaced(string name, string expected) =>
        Assert.Equal(expected, StartupShortcutName.For(name, []));

    [Theory]
    [InlineData("  Spaced  ", "Spaced.lnk")]
    [InlineData("Dots...", "Dots.lnk")]
    [InlineData("Mixed. . ", "Mixed.lnk")]
    public void For_TrailingDotsAndSpaces_AreRemoved(string name, string expected) =>
        Assert.Equal(expected, StartupShortcutName.For(name, []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void For_NothingLeft_UsesApp(string? name) =>
        Assert.Equal("App.lnk", StartupShortcutName.For(name, []));

    [Theory]
    [InlineData("CON", "_CON.lnk")]
    [InlineData("nul", "_nul.lnk")]
    [InlineData("com1", "_com1.lnk")]
    [InlineData("LPT9.txt", "_LPT9.txt.lnk")]
    [InlineData("Console", "Console.lnk")]
    [InlineData("COM10", "COM10.lnk")]
    public void For_ReservedDeviceName_GetsALeadingUnderscore(string name, string expected) =>
        Assert.Equal(expected, StartupShortcutName.For(name, []));

    [Fact]
    public void For_LongName_IsCappedAt100CharactersBeforeTheExtension() =>
        Assert.Equal(new string('a', 100) + ".lnk", StartupShortcutName.For(new string('a', 150), []));

    [Fact]
    public void For_LongNameCutAtASpace_HasNoTrailingSpace() =>
        Assert.Equal(new string('a', 99) + ".lnk", StartupShortcutName.For(new string('a', 99) + " bbbb", []));

    [Fact]
    public void For_Collision_AddsANumberIgnoringCase() =>
        Assert.Equal("Notepad (2).lnk", StartupShortcutName.For("Notepad", ["NOTEPAD.LNK"]));

    [Fact]
    public void For_SeveralCollisions_TakesTheNextFreeNumber() =>
        Assert.Equal("Notepad (4).lnk", StartupShortcutName.For("Notepad", ["Notepad.lnk", "Notepad (2).lnk", "Notepad (3).lnk", "Notepad (5).lnk"]));

    [Fact]
    public void For_OtherFilesWithTheSameStem_AreNoCollision() =>
        Assert.Equal("Notepad.lnk", StartupShortcutName.For("Notepad", ["Notepad.exe", "Notepad.url"]));
}
