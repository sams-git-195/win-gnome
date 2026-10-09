using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class UninstallPlanTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string ProductCode = "{8B1A2C3D-1111-2222-3333-444455556666}";

    private static InstalledAppRecord App(string? uninstall, string key = "App", bool msi = false, bool noRemove = false) =>
        new(key, InstalledAppScope.Machine, "App", null, null, null, null, null, uninstall, msi, noRemove, false, null, null);

    private static PlannedCommand? Plan(InstalledAppRecord record) => UninstallPlan.For(record, System32);

    [Fact]
    public void For_NoRemove_GivesNone() =>
        Assert.Null(Plan(App(@"C:\App\uninstall.exe", noRemove: true)));

    [Fact]
    public void For_NoRemoveOnAnMsiKey_GivesNone() =>
        Assert.Null(Plan(App("MsiExec.exe /I" + ProductCode, key: ProductCode, msi: true, noRemove: true)));

    [Fact]
    public void For_MsiWithGuidKey_RemovesTheProductWithSystemMsiexec() =>
        Assert.Equal(new PlannedCommand(@"C:\Windows\System32\msiexec.exe", "/X " + ProductCode),
            Plan(App("MsiExec.exe /I" + ProductCode, key: ProductCode, msi: true)));

    [Fact]
    public void For_MsiWithLowerCaseGuidKey_WritesTheProductCodeInUpperCase() =>
        Assert.Equal(new PlannedCommand(@"C:\Windows\System32\msiexec.exe", "/X " + ProductCode),
            Plan(App(null, key: ProductCode.ToLowerInvariant(), msi: true)));

    [Fact]
    public void For_MsiWithNonGuidKey_FallsThroughToTheUninstallString() =>
        Assert.Equal(new PlannedCommand(@"C:\Program Files\App\remove.exe", "/quiet"),
            Plan(App(@"""C:\Program Files\App\remove.exe"" /quiet", key: "SomeProduct", msi: true)));

    [Fact]
    public void For_MsiWithNonGuidKeyAndNoUninstallString_GivesNone() =>
        Assert.Null(Plan(App(null, key: "SomeProduct", msi: true)));

    [Fact]
    public void For_GuidKeyWithoutWindowsInstaller_UsesTheUninstallString() =>
        Assert.Equal(new PlannedCommand(@"C:\App\unins000.exe", ""),
            Plan(App(@"C:\App\unins000.exe", key: ProductCode)));

    [Theory]
    [InlineData("msiexec /x " + ProductCode, @"C:\Windows\System32\msiexec.exe", "/x " + ProductCode)]
    [InlineData("MsiExec.exe /X" + ProductCode, @"C:\Windows\System32\msiexec.exe", "/X" + ProductCode)]
    [InlineData(@"rundll32.exe C:\App\app.dll,Uninstall", @"C:\Windows\System32\rundll32.exe", @"C:\App\app.dll,Uninstall")]
    [InlineData(@"RunDll32 ""C:\App Dir\app.dll"",Remove", @"C:\Windows\System32\rundll32.exe", @"""C:\App Dir\app.dll"",Remove")]
    [InlineData(@"""msiexec.exe"" /x " + ProductCode, @"C:\Windows\System32\msiexec.exe", "/x " + ProductCode)]
    public void For_BareSystemProgram_IsRootedAtSystem32(string uninstall, string exe, string args) =>
        Assert.Equal(new PlannedCommand(exe, args), Plan(App(uninstall)));

    [Theory]
    [InlineData("rundll32 app.dll,Uninstall")]
    [InlineData("rundll32.exe app.dll Uninstall")]
    [InlineData(@"rundll32 ""App Dir\app.dll"",Remove")]
    [InlineData(@"rundll32 %ProgramFiles%\App\app.dll,Remove")]
    [InlineData("rundll32")]
    [InlineData(@"rundll32 ""C:\App\app.dll")]
    public void For_Rundll32WithoutAFullyQualifiedDll_GivesNone(string uninstall) =>
        Assert.Null(Plan(App(uninstall)));

    [Fact]
    public void For_SystemDirectoryWithTrailingSlash_GivesOneSeparator() =>
        Assert.Equal(new PlannedCommand(@"C:\Windows\System32\msiexec.exe", "/x {A}"),
            UninstallPlan.For(App("msiexec /x {A}"), @"C:\Windows\System32\"));

    [Theory]
    [InlineData(@"""C:\Program Files\App\uninstall.exe""", @"C:\Program Files\App\uninstall.exe", "")]
    [InlineData(@"""C:\Program Files\App\uninstall.exe"" /S", @"C:\Program Files\App\uninstall.exe", "/S")]
    [InlineData(@"C:\Program Files\App\uninstall.exe", @"C:\Program Files\App\uninstall.exe", "")]
    [InlineData(@"C:\Program Files (x86)\My App\Uninstall My App.exe /S /x", @"C:\Program Files (x86)\My App\Uninstall My App.exe", "/S /x")]
    [InlineData(@"\\server\share\remove.exe --all", @"\\server\share\remove.exe", "--all")]
    [InlineData(@"C:\WINDOWS\system32\msiexec.exe /x {A}", @"C:\WINDOWS\system32\msiexec.exe", "/x {A}")]
    public void For_FullyQualifiedExe_RunsIt(string uninstall, string exe, string args) =>
        Assert.Equal(new PlannedCommand(exe, args), Plan(App(uninstall)));

    [Theory]
    [InlineData("setup.exe /uninstall")]
    [InlineData("uninstall.exe")]
    [InlineData(@"App\uninstall.exe")]
    [InlineData(@".\uninstall.exe")]
    [InlineData(@"..\App\uninstall.exe")]
    [InlineData(@"\App\uninstall.exe")]
    [InlineData(@"C:uninstall.exe")]
    [InlineData(@"%ProgramFiles%\App\uninstall.exe")]
    [InlineData(@"""%ProgramFiles%\App\uninstall.exe"" /S")]
    [InlineData(@"C:\App\uninstall.exe %TEMP%\log.txt")]
    [InlineData("cmd /c del stuff")]
    [InlineData(@"powershell.exe -File C:\App\remove.ps1")]
    public void For_CommandThatCouldResolveThroughTheSearchPath_GivesNone(string uninstall) =>
        Assert.Null(Plan(App(uninstall)));

    [Theory]
    [InlineData(@"C:\Program Files\App\uninst /S")]
    [InlineData(@"C:\App\remove.bat")]
    [InlineData(@"""C:\App\remove.cmd"" /q")]
    [InlineData(@"C:\App\readme.txt")]
    public void For_FullyQualifiedButNotAnExe_GivesNone(string uninstall) =>
        Assert.Null(Plan(App(uninstall)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\" /S")]
    [InlineData("\"C:\\App\\uninstall.exe")]
    public void For_MissingOrMalformedUninstallString_GivesNone(string? uninstall) =>
        Assert.Null(Plan(App(uninstall)));

    [Theory]
    [InlineData("System32")]
    [InlineData("")]
    [InlineData(@"\Windows\System32")]
    public void For_SystemDirectoryNotFullyQualified_Throws(string systemDirectory) =>
        Assert.Throws<ArgumentException>(() => UninstallPlan.For(App("msiexec /x {A}"), systemDirectory));
}
