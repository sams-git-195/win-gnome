using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using WinGnome.Core.Settings;
using static WinGnome.Core.Tests.Monitors.MonitorLayoutTests;

namespace WinGnome.Core.Tests.Monitors;

public class SurfacePlanTests
{
    private static readonly MonitorInfo A = Mon("A", 0, 0, 1920, 1080, 96, primary: true);
    private static readonly MonitorInfo B = Mon("B", -2560, -400, 0, 1040, 144);
    private static readonly MonitorLayout Both = MonitorLayout.Create([A, B]);

    private static SurfaceState On(MonitorInfo monitor, bool detached = false) => new(monitor.Key, monitor.Bounds, monitor.Dpi, detached);

    [Fact]
    public void Compute_All_IsEveryMonitor()
    {
        Assert.Equal([A, B], SurfacePlan.Compute(Both, BarMonitors.All));
    }

    [Fact]
    public void Compute_Primary_IsOnlyThePrimary()
    {
        Assert.Equal([A], SurfacePlan.Compute(Both, BarMonitors.Primary));
    }

    [Fact]
    public void Compute_EmptyLayout_IsEmpty()
    {
        Assert.Empty(SurfacePlan.Compute(MonitorLayout.Empty, BarMonitors.Primary));
        Assert.Empty(SurfacePlan.Compute(MonitorLayout.Empty, BarMonitors.All));
    }

    [Fact]
    public void Reconcile_NothingYet_AddsEveryDesiredMonitor()
    {
        var steps = SurfacePlan.Reconcile([], Both, BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Add, "A", A), new SurfaceStep(SurfaceStepKind.Add, "B", B)], steps);
    }

    [Fact]
    public void Reconcile_UpToDate_HasNoSteps()
    {
        Assert.Empty(SurfacePlan.Reconcile([On(A), On(B)], Both, BarMonitors.All));
    }

    [Fact]
    public void Reconcile_MonitorUnplugged_RemovesItsSurface()
    {
        var steps = SurfacePlan.Reconcile([On(A), On(B)], MonitorLayout.Create([A]), BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Remove, "B")], steps);
    }

    [Fact]
    public void Reconcile_SwitchedToPrimaryOnly_RemovesTheSecondary()
    {
        var steps = SurfacePlan.Reconcile([On(A), On(B)], Both, BarMonitors.Primary);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Remove, "B")], steps);
    }

    [Fact]
    public void Reconcile_SwitchedToAll_AddsTheSecondary()
    {
        var steps = SurfacePlan.Reconcile([On(A)], Both, BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Add, "B", B)], steps);
    }

    [Fact]
    public void Reconcile_MonitorMoved_ReleasesThenDocks()
    {
        var moved = B with { Bounds = new PixelRect(1920, 0, 4480, 1440) };

        var steps = SurfacePlan.Reconcile([On(A), On(B)], MonitorLayout.Create([A, moved]), BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Release, "B"), new SurfaceStep(SurfaceStepKind.Dock, "B", moved)], steps);
    }

    [Fact]
    public void Reconcile_DpiChanged_ReleasesThenDocks()
    {
        var scaled = B with { Dpi = 120 };

        var steps = SurfacePlan.Reconcile([On(A), On(B)], MonitorLayout.Create([A, scaled]), BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Release, "B"), new SurfaceStep(SurfaceStepKind.Dock, "B", scaled)], steps);
    }

    [Fact]
    public void Reconcile_DetachedAndKeyPresent_DocksWithoutAnotherRelease()
    {
        var steps = SurfacePlan.Reconcile([On(A), On(B, detached: true)], Both, BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Dock, "B", B)], steps);
    }

    [Fact]
    public void Reconcile_DetachedAndKeyGone_Removes()
    {
        var steps = SurfacePlan.Reconcile([On(A), On(B, detached: true)], MonitorLayout.Create([A]), BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Remove, "B")], steps);
    }

    [Fact]
    public void Reconcile_PrimarySwapWithAll_ReleasesBothBeforeDockingEither()
    {
        var newA = A with { Bounds = new PixelRect(1920, 0, 3840, 1080), IsPrimary = false };
        var newB = B with { Bounds = new PixelRect(0, 0, 2560, 1440), IsPrimary = true };

        var steps = SurfacePlan.Reconcile([On(A), On(B)], MonitorLayout.Create([newA, newB]), BarMonitors.All);

        Assert.Equal(
            [
                new SurfaceStep(SurfaceStepKind.Release, "A"),
                new SurfaceStep(SurfaceStepKind.Release, "B"),
                new SurfaceStep(SurfaceStepKind.Dock, "A", newA),
                new SurfaceStep(SurfaceStepKind.Dock, "B", newB),
            ],
            steps);
    }

    [Fact]
    public void Reconcile_PrimarySwapWithPrimaryOnly_MovesTheOneSurface()
    {
        var newA = A with { Bounds = new PixelRect(1920, 0, 3840, 1080), IsPrimary = false };
        var newB = B with { Bounds = new PixelRect(0, 0, 2560, 1440), IsPrimary = true };

        var steps = SurfacePlan.Reconcile([On(A)], MonitorLayout.Create([newA, newB]), BarMonitors.Primary);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Release, "A"), new SurfaceStep(SurfaceStepKind.Dock, "A", newB)], steps);
    }

    [Fact]
    public void Reconcile_PrimaryUnpluggedWithPrimaryOnly_MovesTheSurfaceToTheNewPrimary()
    {
        var newB = B with { Bounds = new PixelRect(0, 0, 2560, 1440), IsPrimary = true };

        var steps = SurfacePlan.Reconcile([On(A, detached: true)], MonitorLayout.Create([newB]), BarMonitors.Primary);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Dock, "A", newB)], steps);
    }

    [Fact]
    public void Reconcile_OrdersRemovalsBeforeReleasesBeforeDocksBeforeAdditions()
    {
        var c = Mon("C", 1920, 0, 3840, 1080);
        var movedB = B with { Bounds = new PixelRect(-2560, 0, 0, 1440) };

        var steps = SurfacePlan.Reconcile([On(A), On(B), On(Mon("D", 5000, 0, 6000, 1000))], MonitorLayout.Create([A, movedB, c]), BarMonitors.All);

        Assert.Equal(
            [
                new SurfaceStep(SurfaceStepKind.Remove, "D"),
                new SurfaceStep(SurfaceStepKind.Release, "B"),
                new SurfaceStep(SurfaceStepKind.Dock, "B", movedB),
                new SurfaceStep(SurfaceStepKind.Add, "C", c),
            ],
            steps);
    }

    [Fact]
    public void Reconcile_EmptyLayout_RemovesEverything()
    {
        var steps = SurfacePlan.Reconcile([On(A), On(B)], MonitorLayout.Empty, BarMonitors.All);

        Assert.Equal([new SurfaceStep(SurfaceStepKind.Remove, "A"), new SurfaceStep(SurfaceStepKind.Remove, "B")], steps);
    }

    [Theory]
    [InlineData(BarMonitors.All)]
    [InlineData(BarMonitors.Primary)]
    public void Reconcile_PrimarySwap_NeverHasTwoDockedSurfacesOnOneMonitor(BarMonitors mode)
    {
        // Monitors trade places: A's new rectangle is B's old one, so docking A before releasing B would stack two
        // strips there. Replay the steps and check every intermediate state.
        var newA = A with { Bounds = B.Bounds, IsPrimary = false };
        var newB = B with { Bounds = A.Bounds, IsPrimary = true };
        var docked = new Dictionary<string, PixelRect> { ["A"] = A.Bounds, ["B"] = B.Bounds };
        var current = mode == BarMonitors.All ? new[] { On(A), On(B) } : [On(A)];
        if (mode == BarMonitors.Primary)
        {
            docked.Remove("B");
        }

        foreach (var step in SurfacePlan.Reconcile(current, MonitorLayout.Create([newA, newB]), mode))
        {
            switch (step.Kind)
            {
                case SurfaceStepKind.Remove:
                case SurfaceStepKind.Release:
                    docked.Remove(step.Key);
                    break;
                default:
                    docked.Remove(step.Key);
                    docked[step.Monitor!.Key] = step.Monitor.Bounds;
                    break;
            }

            Assert.Equal(docked.Count, docked.Values.Distinct().Count());
        }

        Assert.Equal(mode == BarMonitors.All ? 2 : 1, docked.Count);
    }
}
