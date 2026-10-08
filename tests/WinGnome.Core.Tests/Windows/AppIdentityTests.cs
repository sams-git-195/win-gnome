using WinGnome.Core.Shell;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class AppIdentityTests
{
    [Fact]
    public void ForWindow_PrefersAumid_Lowercased()
    {
        Assert.Equal(
            "aumid:microsoft.windowsterminal_8wekyb3d8bbwe!app",
            AppIdentity.ForWindow("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", @"C:\x\wt.exe", 7));
    }

    [Fact]
    public void ForWindow_FallsBackToLowercasedPath()
    {
        Assert.Equal(@"path:c:\program files\foo\foo.exe", AppIdentity.ForWindow(null, @"C:\Program Files\Foo\Foo.exe", 7));
        Assert.Equal(@"path:c:\program files\foo\foo.exe", AppIdentity.ForWindow("", @"C:\Program Files\Foo\Foo.exe", 7));
        Assert.Equal(@"path:c:\program files\foo\foo.exe", AppIdentity.ForWindow("   ", @"C:\Program Files\Foo\Foo.exe", 7));
    }

    [Fact]
    public void ForWindow_FallsBackToPid()
    {
        Assert.Equal("pid:7", AppIdentity.ForWindow(null, null, 7));
        Assert.Equal("pid:7", AppIdentity.ForWindow("", "  ", 7));
    }

    [Fact]
    public void ForLaunchId_AumidIsLowercased()
    {
        Assert.Equal(
            "aumid:microsoft.windowsterminal_8wekyb3d8bbwe!app",
            AppIdentity.ForLaunchId("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        Assert.Equal("aumid:msedge", AppIdentity.ForLaunchId("MSEdge"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Foo\Foo.exe", @"path:c:\program files\foo\foo.exe")]
    [InlineData("c:/program files/foo/foo.exe", @"path:c:\program files\foo\foo.exe")]
    [InlineData(@"\\server\share\tool.exe", @"path:\\server\share\tool.exe")]
    [InlineData(@"Tools\Thing.lnk", @"path:tools\thing.lnk")]
    [InlineData(@"D:\Apps\Thing", @"path:d:\apps\thing")]
    public void ForLaunchId_FileSystemPaths(string launchId, string expected)
    {
        Assert.Equal(expected, AppIdentity.ForLaunchId(launchId));
    }

    [Fact]
    public void ForLaunchId_BareExecutableNameIsNotAPath()
    {
        Assert.Equal("aumid:notepad.exe", AppIdentity.ForLaunchId("notepad.exe"));
    }

    [Fact]
    public void ForLaunchId_KnownFolderPath_UsesResolver()
    {
        var firefoxFolder = new Guid("6D809377-6AF0-444B-8957-A3773F02200E");
        string Resolve(string path) => KnownFolderPath.Resolve(path, id => id == firefoxFolder ? @"C:\Program Files" : null);

        var identity = AppIdentity.ForLaunchId(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Mozilla Firefox\firefox.exe", Resolve);

        Assert.Equal(@"path:c:\program files\mozilla firefox\firefox.exe", identity);
    }

    [Fact]
    public void ForLaunchId_KnownFolderPath_WithoutResolver_KeepsGuidForm()
    {
        var identity = AppIdentity.ForLaunchId(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Mozilla Firefox\firefox.exe");

        Assert.Equal(@"path:{6d809377-6af0-444b-8957-a3773f02200e}\mozilla firefox\firefox.exe", identity);
    }

    [Fact]
    public void ForLaunchId_ResolverIsNotCalledForAumids()
    {
        var called = false;
        AppIdentity.ForLaunchId("Some.App!Main", p => { called = true; return p; });
        Assert.False(called);
    }

    [Fact]
    public void ForLaunchId_ResolverReturningBlank_FallsBackToOriginal()
    {
        Assert.Equal(@"path:c:\a\b.exe", AppIdentity.ForLaunchId(@"C:\A\B.exe", _ => ""));
    }

    [Fact]
    public void WindowAndLaunchIdentitiesAgreeForTheSameExecutable()
    {
        var window = AppIdentity.ForWindow(null, @"C:\Program Files\Foo\Foo.exe", 1);
        var launch = AppIdentity.ForLaunchId("c:/program files/foo/FOO.EXE");

        Assert.True(AppIdentity.IsSameApp(window, launch));
    }

    [Fact]
    public void IsSameApp_IgnoresCase_ButNotContent()
    {
        Assert.True(AppIdentity.IsSameApp("aumid:abc", "AUMID:ABC"));
        Assert.False(AppIdentity.IsSameApp("aumid:abc", "aumid:abd"));
        Assert.False(AppIdentity.IsSameApp("aumid:abc", "path:abc"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe", "Firefox")]
    [InlineData("C:/x/notepad.exe", "Notepad")]
    [InlineData("chrome.exe", "Chrome")]
    [InlineData("Code", "Code")]
    [InlineData(@"C:\x\Visual Studio.exe", "Visual Studio")]
    [InlineData(@"C:\x\7zFM.exe", "7zFM")]
    [InlineData(@"C:\x\archive.tar.gz", "Archive.tar")]
    public void DisplayNameFromPath(string path, string expected)
    {
        Assert.Equal(expected, AppIdentity.DisplayNameFromPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\folder\")]
    public void DisplayNameFromPath_Unknown(string? path)
    {
        Assert.Equal("Unknown", AppIdentity.DisplayNameFromPath(path));
    }
}
