using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class PinnedAppsEditorTests
{
    private static List<PinnedApp> Apps(params string[] ids) =>
        ids.Select(id => new PinnedApp { Name = id, LaunchId = id }).ToList();

    private static string[] Ids(List<PinnedApp> apps) => apps.Select(a => a.LaunchId).ToArray();

    [Theory]
    [InlineData(3, 0, -1, false)]
    [InlineData(3, 0, 1, true)]
    [InlineData(3, 2, 1, false)]
    [InlineData(3, 2, -2, true)]
    [InlineData(3, 1, 0, false)]
    [InlineData(3, 5, -1, false)]
    [InlineData(3, -1, 1, false)]
    public void CanMove_RespectsBounds(int count, int index, int offset, bool expected) =>
        Assert.Equal(expected, PinnedAppsEditor.CanMove(count, index, offset));

    [Fact]
    public void Move_SwapsWithNeighbour()
    {
        var apps = Apps("a", "b", "c");
        Assert.True(PinnedAppsEditor.Move(apps, 2, -1));
        Assert.Equal(["a", "c", "b"], Ids(apps));
        Assert.True(PinnedAppsEditor.Move(apps, 0, 1));
        Assert.Equal(["c", "a", "b"], Ids(apps));
    }

    [Fact]
    public void Move_OutOfRange_LeavesListUntouched()
    {
        var apps = Apps("a", "b");
        Assert.False(PinnedAppsEditor.Move(apps, 0, -1));
        Assert.False(PinnedAppsEditor.Move(apps, 1, 1));
        Assert.Equal(["a", "b"], Ids(apps));
    }

    [Fact]
    public void TryAdd_AppendsNewApp()
    {
        var apps = Apps("a");
        Assert.True(PinnedAppsEditor.TryAdd(apps, new PinnedApp { Name = "B", LaunchId = "b" }));
        Assert.Equal(["a", "b"], Ids(apps));
    }

    [Fact]
    public void TryAdd_RejectsDuplicatesIgnoringCase()
    {
        var apps = Apps("Calc");
        Assert.False(PinnedAppsEditor.TryAdd(apps, new PinnedApp { Name = "Calculator", LaunchId = "calc" }));
        Assert.Single(apps);
    }

    [Fact]
    public void TryAdd_RejectsBlankLaunchId()
    {
        var apps = Apps();
        Assert.False(PinnedAppsEditor.TryAdd(apps, new PinnedApp { Name = "x", LaunchId = " " }));
        Assert.Empty(apps);
    }

    [Fact]
    public void Contains_IgnoresCase() =>
        Assert.True(PinnedAppsEditor.Contains(Apps("MSEdge"), "msedge"));
}
