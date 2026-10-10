using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class TaskbarRegionPolicyTests
{
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    public void ShouldManage_OnlyInDockModeOutsideSafeModeWithTheSettingOn(bool safe, bool hidden, bool enabled, bool expected)
    {
        Assert.Equal(expected, TaskbarRegionPolicy.ShouldManage(safe, hidden, enabled));
    }

    [Theory]
    [InlineData(TaskbarRegionPolicy.ErrorRegion, true)]
    [InlineData(TaskbarRegionPolicy.NullRegion, false)]
    [InlineData(TaskbarRegionPolicy.SimpleRegion, true)]
    [InlineData(TaskbarRegionPolicy.ComplexRegion, true)]
    public void NeedsEmptying_OnlyAnEmptyRegionIsLeftAlone(int regionType, bool expected)
    {
        Assert.Equal(expected, TaskbarRegionPolicy.NeedsEmptying(regionType));
    }

    [Fact]
    public void RegionsToClear_EmptyRegionsOnPresentWindows_AreCleared()
    {
        var present = new Dictionary<long, int> { [10] = TaskbarRegionPolicy.NullRegion, [20] = TaskbarRegionPolicy.NullRegion };

        Assert.Equal([10L, 20L], TaskbarRegionPolicy.RegionsToClear([10, 20], present));
    }

    [Fact]
    public void RegionsToClear_ARegionExplorerSetItself_IsNeverCleared()
    {
        var present = new Dictionary<long, int>
        {
            [10] = TaskbarRegionPolicy.SimpleRegion,
            [20] = TaskbarRegionPolicy.ComplexRegion,
            [30] = TaskbarRegionPolicy.ErrorRegion,
            [40] = TaskbarRegionPolicy.NullRegion,
        };

        Assert.Equal([40L], TaskbarRegionPolicy.RegionsToClear([10, 20, 30, 40], present));
    }

    [Fact]
    public void RegionsToClear_RecordedWindowThatIsGone_IsSkipped()
    {
        var present = new Dictionary<long, int> { [20] = TaskbarRegionPolicy.NullRegion };

        Assert.Equal([20L], TaskbarRegionPolicy.RegionsToClear([10, 20], present));
    }

    [Fact]
    public void RegionsToClear_PresentWindowThatWasNeverRecorded_IsSkipped()
    {
        var present = new Dictionary<long, int> { [10] = TaskbarRegionPolicy.NullRegion, [99] = TaskbarRegionPolicy.NullRegion };

        Assert.Equal([10L], TaskbarRegionPolicy.RegionsToClear([10], present));
    }

    [Fact]
    public void RegionsToClear_DuplicateRecords_ClearOnce()
    {
        var present = new Dictionary<long, int> { [10] = TaskbarRegionPolicy.NullRegion };

        Assert.Equal([10L], TaskbarRegionPolicy.RegionsToClear([10, 10], present));
    }

    [Fact]
    public void RegionsToClear_NothingRecorded_ClearsNothing()
    {
        var present = new Dictionary<long, int> { [10] = TaskbarRegionPolicy.NullRegion };

        Assert.Empty(TaskbarRegionPolicy.RegionsToClear([], present));
    }
}
