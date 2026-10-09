using WinGnome.Core.Overview;

namespace WinGnome.Core.Tests.Overview;

public class OverviewTransitionStateTests
{
    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void New_IsClosedAndStill()
    {
        var state = new OverviewTransitionState();

        Assert.Equal((0.0, false), (state.Position, state.IsMoving));
    }

    [Fact]
    public void Begin_OpeningFromClosed_TakesTheFullOpenDuration()
    {
        var state = new OverviewTransitionState();

        state.Begin(opening: true, animationsEnabled: true);

        Assert.Equal((Ms(250), true, true), (state.Duration, state.IsOpening, state.IsMoving));
    }

    [Fact]
    public void Advance_ClockStartsAtTheFirstFrame()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);

        Assert.Equal(new TransitionFrame(0, 0, false), state.Advance(Ms(5000)));
        Assert.Equal(new TransitionFrame(0.75, 0.75, false), state.Advance(Ms(5125)));
    }

    [Fact]
    public void Advance_AtTheDuration_FinishesInTheGrid()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.Advance(Ms(100));

        var frame = state.Advance(Ms(350));

        Assert.Equal((new TransitionFrame(1, 1, true), false), (frame, state.IsMoving));
    }

    [Fact]
    public void Advance_ClockGoingBackwards_StaysAtTheStart()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.Advance(Ms(100));

        Assert.Equal(new TransitionFrame(0, 0, false), state.Advance(Ms(90)));
    }

    [Fact]
    public void Advance_AnimationsOff_FinishesOnTheFirstFrame()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: false);

        Assert.Equal((TimeSpan.Zero, new TransitionFrame(1, 1, true)), (state.Duration, state.Advance(Ms(7))));
    }

    [Fact]
    public void Closing_FromTheGrid_TakesTheFullCloseDurationBackToTheWindows()
    {
        var state = Opened();

        state.Begin(opening: false, animationsEnabled: true);
        var start = state.Advance(Ms(1000));
        var middle = state.Advance(Ms(1100));

        Assert.Equal(
            (Ms(200), new TransitionFrame(0, 1, false), new TransitionFrame(0.75, 0.25, false)),
            (state.Duration, start, middle));
    }

    [Fact]
    public void Reversing_MidOpen_TakesTheShareOfTheCloseDurationTravelled()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.Advance(Ms(0));
        state.Advance(Ms(125));

        state.Begin(opening: false, animationsEnabled: true);
        state.Advance(Ms(130));
        var frame = state.Advance(Ms(205));

        // 0.75 of the way open, so 0.75 x 200 ms back; 75 ms into that is 0.75 eased, leaving 0.1875.
        Assert.Equal((Ms(150), new TransitionFrame(0.75, 0.1875, false)), (state.Duration, frame));
    }

    [Fact]
    public void Reversing_Twice_UsesThePositionOnTheWholePath()
    {
        // Open to 0.75, close to 0.1875, then open again: 0.8125 of the way is left, so 0.8125 x 250 ms.
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.Advance(Ms(0));
        state.Advance(Ms(125));
        state.Begin(opening: false, animationsEnabled: true);
        state.Advance(Ms(130));
        state.Advance(Ms(205));

        state.Begin(opening: true, animationsEnabled: true);

        Assert.Equal((Ms(203.125), 0.1875), (state.Duration, state.Position));
    }

    [Fact]
    public void Begin_AlreadyAtTheTarget_TakesNoTime()
    {
        var state = Opened();

        state.Begin(opening: true, animationsEnabled: true);

        Assert.Equal((TimeSpan.Zero, new TransitionFrame(1, 1, true)), (state.Duration, state.Advance(Ms(3))));
    }

    [Fact]
    public void WatchdogDelay_IsTheDurationPlusAMargin()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: false, animationsEnabled: true);
        var closing = state.WatchdogDelay;
        state.Reset();
        state.Begin(opening: true, animationsEnabled: true);

        Assert.Equal((Ms(300), Ms(550)), (closing, state.WatchdogDelay));
    }

    [Fact]
    public void Finish_MidGlide_JumpsToTheTargetAndStops()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.Advance(Ms(0));
        state.Advance(Ms(60));

        var frame = state.Finish();

        Assert.Equal((new TransitionFrame(1, 1, true), false), (frame, state.IsMoving));
    }

    [Fact]
    public void Finish_Closing_EndsAtTheWindows()
    {
        var state = Opened();
        state.Begin(opening: false, animationsEnabled: true);

        Assert.Equal(new TransitionFrame(1, 0, true), state.Finish());
    }

    [Fact]
    public void NoteWindowsChanged_WhileSettled_AppliesNow()
    {
        var state = Opened();

        Assert.Equal((true, false), (state.NoteWindowsChanged(), state.TakeWindowChanges()));
    }

    [Fact]
    public void NoteWindowsChanged_WhileMoving_IsDeferredUntilTaken()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);

        var applyNow = state.NoteWindowsChanged();
        var first = state.TakeWindowChanges();
        var second = state.TakeWindowChanges();

        Assert.Equal((false, true, false), (applyNow, first, second));
    }

    [Fact]
    public void Reopening_MidClose_RefreshesTheWindowsAfterwards()
    {
        // Window events aren't watched while closing, so anything may have changed.
        var state = Opened();
        state.Begin(opening: false, animationsEnabled: true);
        state.Advance(Ms(0));
        state.Advance(Ms(50));

        state.Begin(opening: true, animationsEnabled: true);

        Assert.True(state.TakeWindowChanges());
    }

    [Fact]
    public void Opening_FromClosed_HasNoPendingWindowChanges()
    {
        var state = new OverviewTransitionState();

        state.Begin(opening: true, animationsEnabled: true);

        Assert.False(state.TakeWindowChanges());
    }

    [Fact]
    public void Reset_ReturnsToClosedAndForgetsPendingChanges()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.NoteWindowsChanged();

        state.Reset();

        Assert.Equal((0.0, false, false), (state.Position, state.IsMoving, state.TakeWindowChanges()));
    }

    private static OverviewTransitionState Opened()
    {
        var state = new OverviewTransitionState();
        state.Begin(opening: true, animationsEnabled: true);
        state.Advance(Ms(0));
        state.Advance(Ms(250));
        return state;
    }
}
