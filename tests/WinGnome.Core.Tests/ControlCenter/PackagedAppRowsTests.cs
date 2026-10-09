using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class PackagedAppRowsTests
{
    private const string Calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe";

    [Fact]
    public void Build_Empty_GivesEmpty() =>
        Assert.Empty(PackagedAppRows.Build([]));

    [Fact]
    public void Build_OnePackagedApp_GivesItsRow() =>
        Assert.Equal([new PackagedAppRow(Calc, "Calculator", Calc + "!App")],
            PackagedAppRows.Build([new CatalogApp("Calculator", Calc + "!App")]));

    [Fact]
    public void Build_DesktopEntries_AreIgnored() =>
        Assert.Empty(PackagedAppRows.Build(
        [
            new CatalogApp("Notepad++", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Notepad++\notepad++.exe"),
            new CatalogApp("Word", "Microsoft.Office.WINWORD.EXE.15"),
            new CatalogApp("Chrome", "Chrome"),
            new CatalogApp("Odd", "NotAFamily!App"),
            new CatalogApp("Path", @"C:\Tools\tool.exe"),
        ]));

    [Fact]
    public void Build_SeveralAppsInOneFamily_GivesOneRowNamedAfterTheFirstByName() =>
        Assert.Equal([new PackagedAppRow("Microsoft.Office.Desktop_8wekyb3d8bbwe", "Excel", "Microsoft.Office.Desktop_8wekyb3d8bbwe!Excel")],
            PackagedAppRows.Build(
            [
                new CatalogApp("Word", "Microsoft.Office.Desktop_8wekyb3d8bbwe!Word"),
                new CatalogApp("Excel", "Microsoft.Office.Desktop_8wekyb3d8bbwe!Excel"),
            ]));

    [Fact]
    public void Build_SortsRowsByNameIgnoringCase() =>
        Assert.Equal(["alarms", "Calculator", "photos"],
            PackagedAppRows.Build(
            [
                new CatalogApp("photos", "Microsoft.Windows.Photos_8wekyb3d8bbwe!App"),
                new CatalogApp("Calculator", Calc + "!App"),
                new CatalogApp("alarms", "Microsoft.WindowsAlarms_8wekyb3d8bbwe!App"),
            ]).Select(r => r.Name));

    [Fact]
    public void Build_FamilyNamesDifferingOnlyInCase_AreOneFamily() =>
        Assert.Single(PackagedAppRows.Build([new CatalogApp("A", Calc + "!App"), new CatalogApp("B", Calc.ToUpperInvariant()[..^13] + "8wekyb3d8bbwe!Other")]));

    [Theory]
    [InlineData(Calc + "!App", Calc)]
    [InlineData("A1-b.c_0123456789abc!X", "A1-b.c_0123456789abc")]
    [InlineData("Name_8wekyb3d8bbwe!App!Extra", "Name_8wekyb3d8bbwe")]
    public void FamilyNameOf_Aumid_GivesTheFamily(string aumid, string expected) =>
        Assert.Equal(expected, PackagedAppRows.FamilyNameOf(aumid));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!App")]
    [InlineData(Calc + "!")]
    [InlineData(Calc)]
    [InlineData("Ab_8wekyb3d8bbwe!App")]
    [InlineData("Name_8wekyb3d8bbw!App")]
    [InlineData("Name_8wekyb3d8bbwee!App")]
    [InlineData("Name_8WEKYB3D8BBWE!App")]
    [InlineData("Name_8wekyb3d8bbwi!App")]
    [InlineData("Na me_8wekyb3d8bbwe!App")]
    [InlineData(@"{GUID}\a!b.exe")]
    public void FamilyNameOf_NotAnAumid_GivesNull(string? parsingName) =>
        Assert.Null(PackagedAppRows.FamilyNameOf(parsingName));
}
