using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class FilterKeysPlanTests
{
    // This machine's read on 25H2: filter keys off, shortcut, confirmation, sounds, indicator and click on.
    private static readonly FilterKeysState Off = new(0x7E, 1000, 1000, 500, 0);

    [Fact]
    public void For_SlowOn_SetsOnlyTheOnBitAndKeepsTheTimings()
    {
        Assert.Equal(new FilterKeysState(0x7F, 1000, 1000, 500, 0), FilterKeysPlan.For(Off, slow: true, bounce: false));
    }

    [Fact]
    public void For_SlowThenOff_RestoresTheOriginalBitForBit()
    {
        var slow = FilterKeysPlan.For(Off, slow: true, bounce: false);

        Assert.Equal(Off, FilterKeysPlan.For(slow, slow: false, bounce: false));
    }

    [Fact]
    public void For_BounceOn_ZeroesTheOtherTimingsAndUsesTheDefaultBounceWhenNoneWasSet()
    {
        Assert.Equal(new FilterKeysState(0x7F, 0, 0, 0, 500), FilterKeysPlan.For(Off, slow: false, bounce: true));
    }

    [Fact]
    public void For_BounceOn_KeepsAPreviousBounceTime()
    {
        var current = new FilterKeysState(0x7E, 0, 0, 0, 1500);

        Assert.Equal(new FilterKeysState(0x7F, 0, 0, 0, 1500), FilterKeysPlan.For(current, slow: false, bounce: true));
    }

    [Fact]
    public void For_SlowAfterBounce_TurnsBounceOffAndUsesTheDefaultWait()
    {
        var bounce = new FilterKeysState(0x7F, 0, 0, 0, 500);

        Assert.Equal(new FilterKeysState(0x7F, 1000, 0, 0, 0), FilterKeysPlan.For(bounce, slow: true, bounce: false));
    }

    [Fact]
    public void For_BounceAfterSlow_TurnsSlowOff()
    {
        var slow = new FilterKeysState(0x7F, 700, 1000, 500, 0);

        Assert.Equal(new FilterKeysState(0x7F, 0, 0, 0, 500), FilterKeysPlan.For(slow, slow: false, bounce: true));
    }

    [Fact]
    public void For_BothOff_ClearsOnlyTheOnBit()
    {
        var bounce = new FilterKeysState(0x7F, 0, 0, 0, 700);

        Assert.Equal(new FilterKeysState(0x7E, 0, 0, 0, 700), FilterKeysPlan.For(bounce, slow: false, bounce: false));
    }

    [Fact]
    public void For_UserTurnedTheShortcutOff_KeepsItOff()
    {
        // A valid read (AVAILABLE set) without HOTKEYACTIVE: WinGnome must not silently re-enable the shortcut.
        var current = new FilterKeysState(0x7A, 1000, 1000, 500, 0);

        Assert.Equal(new FilterKeysState(0x7B, 1000, 1000, 500, 0), FilterKeysPlan.For(current, slow: true, bounce: false));
    }

    [Theory]
    [InlineData(true, false, 0x0Fu, 1000, 0)]
    [InlineData(false, true, 0x0Fu, 0, 500)]
    [InlineData(false, false, 0x0Eu, 0, 0)]
    public void For_ZeroFlagsRead_StillWritesHotkeyActiveConfirmHotkeyAndAvailable(bool slow, bool bounce, uint flags, int wait, int bounceMs)
    {
        var zeroed = new FilterKeysState(0, 0, 0, 0, 0);

        Assert.Equal(new FilterKeysState(flags, wait, 0, 0, bounceMs), FilterKeysPlan.For(zeroed, slow, bounce));
    }

    [Theory]
    [InlineData(100, 300)]
    [InlineData(299, 300)]
    [InlineData(300, 300)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 2000)]
    [InlineData(60000, 2000)]
    public void For_SlowOn_ClampsTheWaitToWindowsRange(int wait, int expected)
    {
        var current = new FilterKeysState(0x7E, wait, 1000, 500, 0);

        Assert.Equal(expected, FilterKeysPlan.For(current, slow: true, bounce: false).WaitMs);
    }

    [Theory]
    [InlineData(100, 500)]
    [InlineData(499, 500)]
    [InlineData(500, 500)]
    [InlineData(2000, 2000)]
    [InlineData(2001, 2000)]
    public void For_BounceOn_ClampsTheBounceTimeToWindowsRange(int bounceMs, int expected)
    {
        var current = new FilterKeysState(0x7E, 0, 0, 0, bounceMs);

        Assert.Equal(expected, FilterKeysPlan.For(current, slow: false, bounce: true).BounceMs);
    }

    [Fact]
    public void ForSlowKeys_On_TurnsBounceOff()
    {
        var bounce = new FilterKeysState(0x7F, 0, 0, 0, 700);

        Assert.Equal(new FilterKeysState(0x7F, 1000, 0, 0, 0), FilterKeysPlan.ForSlowKeys(bounce, on: true));
    }

    [Fact]
    public void ForSlowKeys_OffWhileBounceIsOn_LeavesBounceOn()
    {
        var bounce = new FilterKeysState(0x7F, 0, 0, 0, 700);

        Assert.Equal(bounce, FilterKeysPlan.ForSlowKeys(bounce, on: false));
    }

    [Fact]
    public void ForSlowKeys_Off_TurnsFilterKeysOff()
    {
        var slow = new FilterKeysState(0x7F, 1000, 1000, 500, 0);

        Assert.Equal(Off, FilterKeysPlan.ForSlowKeys(slow, on: false));
    }

    [Fact]
    public void ForBounceKeys_On_TurnsSlowOff()
    {
        var slow = new FilterKeysState(0x7F, 1000, 1000, 500, 0);

        Assert.Equal(new FilterKeysState(0x7F, 0, 0, 0, 500), FilterKeysPlan.ForBounceKeys(slow, on: true));
    }

    [Fact]
    public void ForBounceKeys_OffWhileSlowIsOn_LeavesSlowOn()
    {
        var slow = new FilterKeysState(0x7F, 1000, 1000, 500, 0);

        Assert.Equal(slow, FilterKeysPlan.ForBounceKeys(slow, on: false));
    }

    [Fact]
    public void ForBounceKeys_Off_TurnsFilterKeysOff()
    {
        var bounce = new FilterKeysState(0x7F, 0, 0, 0, 700);

        Assert.Equal(new FilterKeysState(0x7E, 0, 0, 0, 700), FilterKeysPlan.ForBounceKeys(bounce, on: false));
    }

    [Fact]
    public void For_SlowAndBounce_Throws()
    {
        Assert.Throws<ArgumentException>(() => FilterKeysPlan.For(Off, slow: true, bounce: true));
    }

    [Theory]
    [InlineData(0x7Eu, 1000, 0, false, false)]
    [InlineData(0x7Fu, 1000, 0, true, false)]
    [InlineData(0x7Fu, 0, 500, false, true)]
    // On with neither an acceptance delay nor a bounce time (repeat keys only): neither switch is on.
    [InlineData(0x7Fu, 0, 0, false, false)]
    // A leftover bounce time while filter keys are off is not bounce keys.
    [InlineData(0x7Eu, 0, 500, false, false)]
    public void State_ReportsSlowAndBounce(uint flags, int wait, int bounceMs, bool slow, bool bounce)
    {
        var state = new FilterKeysState(flags, wait, 1000, 500, bounceMs);

        Assert.Equal((slow, bounce), (state.IsSlowKeysOn, state.IsBounceKeysOn));
    }
}
