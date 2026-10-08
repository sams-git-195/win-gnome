using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class ProcessNameParserTests
{
    [Theory]
    [InlineData("notepad", "notepad")]
    [InlineData("  Notepad.EXE ", "Notepad")]
    [InlineData(@"C:\Windows\System32\calc.exe", "calc")]
    [InlineData("C:/tools/My App.exe", "My App")]
    public void TryNormalize_AcceptsNamesAndPaths(string input, string expected)
    {
        Assert.True(ProcessNameParser.TryNormalize(input, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    [InlineData(@"C:\folder\")]
    [InlineData("bad*name")]
    [InlineData("what?")]
    public void TryNormalize_RejectsInvalidInput(string? input)
    {
        Assert.False(ProcessNameParser.TryNormalize(input, out var name));
        Assert.Equal("", name);
    }
}
