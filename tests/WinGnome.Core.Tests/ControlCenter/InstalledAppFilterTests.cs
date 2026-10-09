using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class InstalledAppFilterTests
{
    private static InstalledAppRecord App(
        string? name,
        string key = "Key",
        InstalledAppScope scope = InstalledAppScope.Machine,
        string? version = "1.0",
        string? publisher = "Pub",
        bool systemComponent = false,
        string? parent = null,
        string? releaseType = null) =>
        new(key, scope, name, publisher, version, null, null, null, null, false, false, systemComponent, parent, releaseType);

    private static string?[] Names(IEnumerable<InstalledAppRecord> records) => [.. records.Select(r => r.DisplayName)];

    [Fact]
    public void Apply_Empty_GivesEmpty() =>
        Assert.Empty(InstalledAppFilter.Apply([]));

    [Fact]
    public void Apply_SortsByNameIgnoringCase() =>
        Assert.Equal(["apple", "Banana", "cherry"], Names(InstalledAppFilter.Apply([App("cherry"), App("apple"), App("Banana")])));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsShown_WithoutDisplayName_IsFalse(string? name) =>
        Assert.False(InstalledAppFilter.IsShown(App(name)));

    [Fact]
    public void IsShown_SystemComponent_IsFalse() =>
        Assert.False(InstalledAppFilter.IsShown(App("App", systemComponent: true)));

    [Fact]
    public void IsShown_WithParentKey_IsFalse() =>
        Assert.False(InstalledAppFilter.IsShown(App("KB123 for Office", parent: "Office")));

    [Theory]
    [InlineData("Update")]
    [InlineData("hotfix")]
    [InlineData("Security Update")]
    [InlineData("Update Rollup")]
    [InlineData("Service Pack")]
    public void IsShown_UpdateReleaseType_IsFalse(string releaseType) =>
        Assert.False(InstalledAppFilter.IsShown(App("KB1", releaseType: releaseType)));

    [Fact]
    public void IsShown_OtherReleaseType_IsTrue() =>
        Assert.True(InstalledAppFilter.IsShown(App("App", releaseType: "Product")));

    [Fact]
    public void IsShown_PlainApp_IsTrue() =>
        Assert.True(InstalledAppFilter.IsShown(App("App")));

    [Fact]
    public void Apply_DropsHiddenRecords() =>
        Assert.Equal(["Shown"], Names(InstalledAppFilter.Apply([App("Shown"), App(null), App("Sys", systemComponent: true)])));

    [Fact]
    public void Apply_SameNameVersionAndPublisherInTwoViews_KeepsTheFirst()
    {
        var machine = App("App", key: "A", scope: InstalledAppScope.Machine);
        var user = App("APP", key: "B", scope: InstalledAppScope.User, publisher: "PUB");

        Assert.Equal([machine], InstalledAppFilter.Apply([machine, user]));
    }

    [Fact]
    public void Apply_SameNameDifferentVersion_KeepsBoth() =>
        Assert.Equal(2, InstalledAppFilter.Apply([App("App", version: "1"), App("App", version: "2")]).Count);

    [Fact]
    public void Apply_SameNameDifferentPublisher_KeepsBoth() =>
        Assert.Equal(2, InstalledAppFilter.Apply([App("App", publisher: "A"), App("App", publisher: "B")]).Count);

    [Fact]
    public void Apply_MissingVersionAndPublisher_StillDeduplicates() =>
        Assert.Single(InstalledAppFilter.Apply([App("App", version: null, publisher: null), App("App", version: null, publisher: null)]));
}
