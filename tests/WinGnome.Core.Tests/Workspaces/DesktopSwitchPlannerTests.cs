using WinGnome.Core.Workspaces;

namespace WinGnome.Core.Tests.Workspaces;

public class DesktopSwitchPlannerTests
{
    private static readonly DateTime T0 = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstSwitch_CountsFromTheCurrentDesktop()
    {
        var planner = new DesktopSwitchPlanner();

        Assert.Equal(2, planner.PlanSwitchTo(new VirtualDesktopState(4, 0), 2, T0));
    }

    [Fact]
    public void SecondClickBeforeExplorerCatchesUp_DoesNotOvershoot()
    {
        var planner = new DesktopSwitchPlanner();
        var stale = new VirtualDesktopState(4, 0);
        planner.PlanSwitchTo(stale, 2, T0);

        // Double-click on the same dot: nothing more to do.
        Assert.Equal(0, planner.PlanSwitchTo(stale, 2, T0.AddMilliseconds(150)));

        // Another dot: counted from the desktop already being switched to.
        Assert.Equal(-1, planner.PlanSwitchTo(stale, 1, T0.AddMilliseconds(300)));
    }

    [Fact]
    public void IntermediateRegistryStates_DoNotResetThePendingTarget()
    {
        var planner = new DesktopSwitchPlanner();
        planner.PlanSwitchTo(new VirtualDesktopState(4, 0), 3, T0);

        var halfway = new VirtualDesktopState(4, 1);
        planner.Observe(halfway);

        Assert.Equal(3, planner.EffectiveIndex(halfway, T0.AddMilliseconds(200)));
    }

    [Fact]
    public void ArrivalClearsThePendingTarget()
    {
        var planner = new DesktopSwitchPlanner();
        planner.PlanSwitchTo(new VirtualDesktopState(3, 0), 2, T0);
        planner.Observe(new VirtualDesktopState(3, 2));

        // Later the user switches elsewhere with the keyboard; the registry is now the truth.
        Assert.Equal(1, planner.EffectiveIndex(new VirtualDesktopState(3, 1), T0.AddMilliseconds(100)));
    }

    [Fact]
    public void RefusedSwitch_FallsBackToTheRegistryAfterTheTimeout()
    {
        var planner = new DesktopSwitchPlanner();
        var state = new VirtualDesktopState(3, 0);
        planner.PlanSwitchTo(state, 2, T0);

        Assert.Equal(0, planner.EffectiveIndex(state, T0 + DesktopSwitchPlanner.PendingTimeout));
        Assert.Equal(2, planner.PlanSwitchTo(state, 2, T0 + DesktopSwitchPlanner.PendingTimeout));
    }

    [Fact]
    public void RemovedDesktop_InvalidatesThePendingTarget()
    {
        var planner = new DesktopSwitchPlanner();
        planner.PlanSwitchTo(new VirtualDesktopState(4, 0), 3, T0);
        var shrunk = new VirtualDesktopState(2, 0);
        planner.Observe(shrunk);

        Assert.Equal(0, planner.EffectiveIndex(shrunk, T0.AddMilliseconds(100)));
    }

    [Theory]
    [InlineData(-5, -1)]
    [InlineData(9, 2)]
    public void TargetIsClampedToExistingDesktops(int index, int expectedSteps)
    {
        var planner = new DesktopSwitchPlanner();

        Assert.Equal(expectedSteps, planner.PlanSwitchTo(new VirtualDesktopState(4, 1), index, T0));
    }

    [Fact]
    public void SwitchBy_StopsAtTheEnds_AndChainsFromThePendingTarget()
    {
        var planner = new DesktopSwitchPlanner();
        var state = new VirtualDesktopState(3, 0);

        Assert.Equal(0, planner.PlanSwitchBy(state, -1, T0));
        Assert.Equal(1, planner.PlanSwitchBy(state, 1, T0));
        Assert.Equal(1, planner.PlanSwitchBy(state, 1, T0.AddMilliseconds(400)));
        Assert.Equal(0, planner.PlanSwitchBy(state, 1, T0.AddMilliseconds(800)));
    }
}
