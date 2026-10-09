using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using static WinGnome.Core.Tests.Monitors.MonitorLayoutTests;

namespace WinGnome.Core.Tests.Monitors;

public class MonitorLayoutDiffTests
{
    private static readonly MonitorInfo A = Mon("A", 0, 0, 1920, 1080, 96, primary: true);
    private static readonly MonitorInfo B = Mon("B", 1920, 0, 3840, 1080, 144);

    [Fact]
    public void Compute_SameLayout_IsEmpty()
    {
        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([A, B]));

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Compute_WorkAreaOnly_IsEmpty()
    {
        // Our own AppBars change work areas; that must not look like a display change.
        var reserved = A with { WorkArea = new PixelRect(0, 32, 1920, 1080) };

        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([reserved, B]));

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Compute_Added()
    {
        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A]), MonitorLayout.Create([A, B]));

        Assert.Equal([B], diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void Compute_Removed()
    {
        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([A]));

        Assert.Equal([B], diff.Removed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void Compute_Moved_ReportsBounds()
    {
        var moved = B with { Bounds = new PixelRect(-1920, 0, 0, 1080) };

        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([A, moved]));

        Assert.Equal([new ChangedMonitor(B, moved, MonitorChange.Bounds)], diff.Changed);
    }

    [Fact]
    public void Compute_DpiOnly_ReportsDpi()
    {
        var scaled = B with { Dpi = 120 };

        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([A, scaled]));

        Assert.Equal([new ChangedMonitor(B, scaled, MonitorChange.Dpi)], diff.Changed);
    }

    [Fact]
    public void Compute_PrimarySwap_ReportsPrimaryAndBoundsOnBoth()
    {
        // Windows keeps the primary at the origin, so a swap moves both monitors.
        var newA = A with { Bounds = new PixelRect(-1920, 0, 0, 1080), IsPrimary = false };
        var newB = B with { Bounds = new PixelRect(0, 0, 1920, 1080), IsPrimary = true };

        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([newA, newB]));

        Assert.Equal(
            [
                new ChangedMonitor(A, newA, MonitorChange.Bounds | MonitorChange.Primary),
                new ChangedMonitor(B, newB, MonitorChange.Bounds | MonitorChange.Primary),
            ],
            diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Compute_HotPlugReplacingAMonitor_ReportsRemovedAndAdded()
    {
        var c = Mon("C", 1920, 0, 3840, 1080);

        var diff = MonitorLayoutDiff.Compute(MonitorLayout.Create([A, B]), MonitorLayout.Create([A, c]));

        Assert.Equal([B], diff.Removed);
        Assert.Equal([c], diff.Added);
        Assert.Empty(diff.Changed);
    }
}
