using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class KeepChangesCountdownTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_IsIdle()
    {
        var countdown = new KeepChangesCountdown();

        Assert.Equal(KeepChangesState.Idle, countdown.State);
        Assert.Equal(0, countdown.SecondsLeft(T0));
    }

    [Fact]
    public void DefaultTimeout_IsFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), KeepChangesCountdown.DefaultTimeout);
    }

    [Fact]
    public void Start_WaitsWithTheFullTimeout()
    {
        var countdown = new KeepChangesCountdown();

        countdown.Start(T0);

        Assert.Equal(KeepChangesState.Waiting, countdown.State);
        Assert.Equal(15, countdown.SecondsLeft(T0));
    }

    [Theory]
    [InlineData(1, 14)]
    [InlineData(4.2, 11)]
    [InlineData(14.9, 1)]
    [InlineData(15, 0)]
    [InlineData(20, 0)]
    public void SecondsLeft_RoundsUp(double elapsed, int expected)
    {
        var countdown = new KeepChangesCountdown();
        countdown.Start(T0);

        Assert.Equal(expected, countdown.SecondsLeft(T0.AddSeconds(elapsed)));
    }

    [Fact]
    public void Tick_BeforeTheDeadline_KeepsWaiting()
    {
        var countdown = new KeepChangesCountdown();
        countdown.Start(T0);

        Assert.False(countdown.Tick(T0.AddSeconds(14.999)));
        Assert.Equal(KeepChangesState.Waiting, countdown.State);
    }

    [Fact]
    public void Tick_AtTheDeadline_RevertsOnce()
    {
        var countdown = new KeepChangesCountdown();
        countdown.Start(T0);

        Assert.True(countdown.Tick(T0.AddSeconds(15)));
        Assert.Equal(KeepChangesState.Reverted, countdown.State);
        Assert.False(countdown.Tick(T0.AddSeconds(16)));
    }

    [Fact]
    public void Keep_WhileWaiting_KeepsAndStopsTheTimeout()
    {
        var countdown = new KeepChangesCountdown();
        countdown.Start(T0);

        Assert.True(countdown.Keep());
        Assert.Equal(KeepChangesState.Kept, countdown.State);
        Assert.False(countdown.Tick(T0.AddSeconds(30)));
        Assert.Equal(KeepChangesState.Kept, countdown.State);
    }

    [Fact]
    public void Revert_WhileWaiting_Reverts()
    {
        var countdown = new KeepChangesCountdown();
        countdown.Start(T0);

        Assert.True(countdown.Revert());
        Assert.Equal(KeepChangesState.Reverted, countdown.State);
    }

    [Fact]
    public void KeepAndRevert_WhenNotWaiting_DoNothing()
    {
        var countdown = new KeepChangesCountdown();

        Assert.False(countdown.Keep());
        Assert.False(countdown.Revert());
        Assert.Equal(KeepChangesState.Idle, countdown.State);
    }

    [Fact]
    public void Revert_AfterTimeout_DoesNothing()
    {
        var countdown = new KeepChangesCountdown();
        countdown.Start(T0);
        countdown.Tick(T0.AddSeconds(15));

        Assert.False(countdown.Revert());
        Assert.False(countdown.Keep());
        Assert.Equal(KeepChangesState.Reverted, countdown.State);
    }

    [Fact]
    public void Start_AgainAfterKeep_WaitsAfresh()
    {
        var countdown = new KeepChangesCountdown(TimeSpan.FromSeconds(10));
        countdown.Start(T0);
        countdown.Keep();

        countdown.Start(T0.AddSeconds(60));

        Assert.Equal(KeepChangesState.Waiting, countdown.State);
        Assert.Equal(10, countdown.SecondsLeft(T0.AddSeconds(60)));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeepChangesCountdown(TimeSpan.Zero));
    }
}
