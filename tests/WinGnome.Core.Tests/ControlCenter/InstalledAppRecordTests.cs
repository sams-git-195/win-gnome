using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class InstalledAppRecordTests
{
    private static InstalledAppRecord From(params (string Name, object? Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value);
        return InstalledAppRecord.FromValues("Key", InstalledAppScope.User, name => map.GetValueOrDefault(name));
    }

    [Fact]
    public void FromValues_ReadsEveryField()
    {
        var record = InstalledAppRecord.FromValues("{GUID}", InstalledAppScope.Machine32, name => name switch
        {
            "DisplayName" => "App",
            "Publisher" => "Pub",
            "DisplayVersion" => "1.2",
            "DisplayIcon" => @"C:\a.exe,0",
            "InstallDate" => "20240131",
            "EstimatedSize" => 2048,
            "UninstallString" => @"C:\u.exe",
            "WindowsInstaller" => 1,
            "NoRemove" => 1,
            "SystemComponent" => 1,
            "ParentKeyName" => "Parent",
            "ReleaseType" => "Hotfix",
            _ => null,
        });

        Assert.Equal(
            new InstalledAppRecord("{GUID}", InstalledAppScope.Machine32, "App", "Pub", "1.2", @"C:\a.exe,0", "20240131", 2048,
                @"C:\u.exe", WindowsInstaller: true, NoRemove: true, SystemComponent: true, "Parent", "Hotfix"),
            record);
    }

    [Fact]
    public void FromValues_NoValues_GivesNullsAndFalseFlags()
    {
        Assert.Equal(
            new InstalledAppRecord("Key", InstalledAppScope.User, null, null, null, null, null, null, null, false, false, false, null, null),
            From());
    }

    [Theory]
    [InlineData("  App  ", "App")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void FromValues_TrimsTextAndDropsEmpty(string raw, string? expected) =>
        Assert.Equal(expected, From(("DisplayName", raw)).DisplayName);

    [Fact]
    public void FromValues_NumberWhereTextExpected_IsReadAsText() =>
        Assert.Equal("5", From(("DisplayVersion", 5)).DisplayVersion);

    [Fact]
    public void FromValues_BinaryWhereTextExpected_IsIgnored() =>
        Assert.Null(From(("DisplayName", new byte[] { 1, 2 })).DisplayName);

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, true)]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("0", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public void FromValues_Flag_IsSetByNonZeroNumbers(object raw, bool expected) =>
        Assert.Equal(expected, From(("SystemComponent", raw)).SystemComponent);

    [Fact]
    public void FromValues_FlagAsBinary_IsNotSet() =>
        Assert.False(From(("NoRemove", new byte[] { 1 })).NoRemove);

    [Theory]
    [InlineData(1024, 1024L)]
    [InlineData("300", 300L)]
    [InlineData("abc", null)]
    public void FromValues_EstimatedSize_ParsesDwordOrText(object raw, long? expected) =>
        Assert.Equal(expected, From(("EstimatedSize", raw)).EstimatedSizeKb);

    [Fact]
    public void FromValues_EstimatedSizeAboveIntMax_IsReadUnsigned() =>
        // The registry API hands a DWORD of 0x80000000 back as int.MinValue.
        Assert.Equal(2147483648L, From(("EstimatedSize", int.MinValue)).EstimatedSizeKb);

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe,0", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe,-101", @"C:\Program Files\App\app.exe")]
    [InlineData(@"""C:\Program Files\App\app.exe"",2", @"C:\Program Files\App\app.exe")]
    [InlineData(@"""C:\Program Files\App\app.ico""", @"C:\Program Files\App\app.ico")]
    [InlineData(@"\\server\share\app.exe", @"\\server\share\app.exe")]
    [InlineData(@"C:\odd,name\app.exe", @"C:\odd,name\app.exe")]
    [InlineData("app.exe", null)]
    [InlineData(@"App\app.exe", null)]
    [InlineData(@"%ProgramFiles%\App\app.exe", null)]
    [InlineData(@"\App\app.exe", null)]
    [InlineData(@"C:app.exe", null)]
    public void IconPath_StripsQuotesAndIndex_AndNeedsAFullyQualifiedPath(string displayIcon, string? expected) =>
        Assert.Equal(expected, From(("DisplayIcon", displayIcon)).IconPath);

    [Fact]
    public void IconPath_WithoutDisplayIcon_IsNull() =>
        Assert.Null(From().IconPath);
}
