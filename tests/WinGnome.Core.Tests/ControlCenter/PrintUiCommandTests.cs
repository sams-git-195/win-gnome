using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class PrintUiCommandTests
{
    [Theory]
    [InlineData(PrintUiAction.Queue, "/o")]
    [InlineData(PrintUiAction.Properties, "/p")]
    [InlineData(PrintUiAction.Preferences, "/e")]
    public void Build_UsesTheActionsSwitch(PrintUiAction action, string flag)
    {
        Assert.Equal($"printui.dll,PrintUIEntry {flag} /n \"HP LaserJet\"", PrintUiCommand.Build(action, "HP LaserJet"));
    }

    [Theory]
    [InlineData("Microsoft Print to PDF")]
    [InlineData("Office, 2nd floor")]
    [InlineData("\\\\server\\queue")]
    [InlineData("O'Brien's printer")]
    public void Build_QuotesTheNameWhateverItContains(string name)
    {
        Assert.Equal($"printui.dll,PrintUIEntry /o /n \"{name}\"", PrintUiCommand.Build(PrintUiAction.Queue, name));
    }

    [Theory]
    [InlineData("bad\"name")]
    [InlineData("\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Build_NameThatCannotBeQuoted_IsRejected(string? name)
    {
        Assert.Null(PrintUiCommand.Build(PrintUiAction.Queue, name));
    }
}
