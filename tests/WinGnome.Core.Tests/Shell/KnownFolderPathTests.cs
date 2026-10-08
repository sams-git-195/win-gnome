using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class KnownFolderPathTests
{
    private static readonly Guid ProgramFilesX86 = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");
    private const string FirefoxPath = @"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Mozilla Firefox\firefox.exe";

    [Fact]
    public void TryParse_SplitsGuidAndRelativePath()
    {
        Assert.True(KnownFolderPath.TryParse(FirefoxPath, out var id, out var relative));

        Assert.Equal(ProgramFilesX86, id);
        Assert.Equal(@"Mozilla Firefox\firefox.exe", relative);
    }

    [Fact]
    public void TryParse_IsCaseInsensitiveForTheGuid()
    {
        Assert.True(KnownFolderPath.TryParse(@"{7c5a40ef-a0fb-4bfc-874a-c0f2e0b9fa8e}\x.exe", out var id, out _));
        Assert.Equal(ProgramFilesX86, id);
    }

    [Fact]
    public void TryParse_AcceptsGuidOnly()
    {
        Assert.True(KnownFolderPath.TryParse("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", out var id, out var relative));
        Assert.Equal(ProgramFilesX86, id);
        Assert.Equal("", relative);
    }

    [Fact]
    public void TryParse_AcceptsForwardSlashSeparator()
    {
        Assert.True(KnownFolderPath.TryParse("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}/App/app.exe", out _, out var relative));
        Assert.Equal("App/app.exe", relative);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\Program Files\app.exe")]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App")]
    [InlineData(@"7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E\app.exe")]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E\app.exe")]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8}\app.exe")]
    [InlineData(@"{ZC5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\app.exe")]
    [InlineData("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}app.exe")]
    [InlineData(@" {7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\app.exe")]
    public void TryParse_RejectsEverythingElse(string path)
    {
        Assert.False(KnownFolderPath.TryParse(path, out var id, out var relative));
        Assert.Equal(Guid.Empty, id);
        Assert.Equal("", relative);
    }

    [Fact]
    public void Resolve_ReplacesTheGuidPrefix()
    {
        var resolved = KnownFolderPath.Resolve(FirefoxPath, id => id == ProgramFilesX86 ? @"C:\Program Files (x86)" : null);

        Assert.Equal(@"C:\Program Files (x86)\Mozilla Firefox\firefox.exe", resolved);
    }

    [Fact]
    public void Resolve_DoesNotDoubleTheSeparator()
    {
        var resolved = KnownFolderPath.Resolve(FirefoxPath, _ => @"C:\Program Files (x86)\");

        Assert.Equal(@"C:\Program Files (x86)\Mozilla Firefox\firefox.exe", resolved);
    }

    [Fact]
    public void Resolve_GuidOnly_ReturnsTheFolder()
    {
        Assert.Equal(@"C:\Users\me\Desktop", KnownFolderPath.Resolve("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", _ => @"C:\Users\me\Desktop\"));
    }

    [Fact]
    public void Resolve_UnknownGuid_ReturnsOriginal()
    {
        Assert.Equal(FirefoxPath, KnownFolderPath.Resolve(FirefoxPath, _ => null));
        Assert.Equal(FirefoxPath, KnownFolderPath.Resolve(FirefoxPath, _ => ""));
        Assert.Equal(FirefoxPath, KnownFolderPath.Resolve(FirefoxPath, _ => "  "));
    }

    [Theory]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("Some.App!Main")]
    [InlineData("")]
    public void Resolve_NonKnownFolderStrings_AreReturnedUnchanged_AndLookupIsNotCalled(string path)
    {
        var called = false;
        var result = KnownFolderPath.Resolve(path, _ => { called = true; return @"X:\"; });

        Assert.Equal(path, result);
        Assert.False(called);
    }

    [Fact]
    public void Resolve_PassesTheParsedGuidToLookup()
    {
        Guid? seen = null;
        KnownFolderPath.Resolve(FirefoxPath, id => { seen = id; return null; });
        Assert.Equal(ProgramFilesX86, seen);
    }

    [Theory]
    [InlineData(@"C:\Program Files\app.exe", true)]
    [InlineData("c:/tools/app", true)]
    [InlineData(@"\\server\share\app.exe", true)]
    [InlineData(@"Tools\App.lnk", true)]
    [InlineData(@"tools/app.EXE", true)]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Mozilla Firefox\firefox.exe", true)]
    [InlineData("  C:\\x\\y.exe  ", true)]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", false)]
    [InlineData("MSEdge", false)]
    [InlineData("notepad.exe", false)]
    [InlineData("Microsoft.Windows.Explorer", false)]
    [InlineData(@"folder\notepad.txt", false)]
    [InlineData("C:", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void LooksLikeFileSystemPath(string text, bool expected)
    {
        Assert.Equal(expected, KnownFolderPath.LooksLikeFileSystemPath(text));
    }
}
