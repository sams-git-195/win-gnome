using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class CrashHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_HasNoCrashes()
    {
        Assert.Empty(CrashHistory.Empty.Crashes);
        Assert.False(CrashHistory.Empty.IsCrashLoop(Now));
    }

    [Fact]
    public void Record_AppendsInTimeOrder()
    {
        var history = CrashHistory.Empty.Record(Now.AddSeconds(-5), Now).Record(Now.AddSeconds(-20), Now);

        Assert.Equal([Now.AddSeconds(-20), Now.AddSeconds(-5)], history.Crashes);
    }

    [Fact]
    public void Record_DoesNotMutateTheOriginal()
    {
        var original = CrashHistory.Empty;

        original.Record(Now, Now);

        Assert.Empty(original.Crashes);
    }

    [Fact]
    public void Prune_DropsEntriesOlderThanThirtyMinutes()
    {
        var history = CrashHistory.From([Now.AddMinutes(-31), Now.AddMinutes(-30), Now.AddMinutes(-1)]);

        Assert.Equal([Now.AddMinutes(-30), Now.AddMinutes(-1)], history.Prune(Now).Crashes);
    }

    [Fact]
    public void Record_PrunesOldEntries()
    {
        var history = CrashHistory.From([Now.AddHours(-3)]).Record(Now, Now);

        Assert.Equal([Now], history.Crashes);
    }

    [Theory]
    [InlineData(120, 1)]
    [InlineData(121, 0)]
    [InlineData(0, 1)]
    public void CountWithin_TwoMinuteWindowIsInclusive(int secondsAgo, int expected)
    {
        var history = CrashHistory.From([Now.AddSeconds(-secondsAgo)]);

        Assert.Equal(expected, history.CountWithin(CrashHistory.FastWindow, Now));
    }

    [Fact]
    public void CountWithin_IgnoresTimestampsInTheFuture()
    {
        var history = CrashHistory.From([Now.AddMinutes(10), Now.AddSeconds(-5)]);

        Assert.Equal(1, history.CountWithin(CrashHistory.FastWindow, Now));
    }

    [Fact]
    public void IsCrashLoop_ThreeInTwoMinutes_IsTrue()
    {
        var history = CrashHistory.From([Now.AddSeconds(-100), Now.AddSeconds(-50), Now]);

        Assert.True(history.IsCrashLoop(Now));
    }

    [Fact]
    public void IsCrashLoop_FiveInThirtyMinutesButNeverThreeInTwo_IsTrue()
    {
        var history = CrashHistory.From([Now.AddMinutes(-29), Now.AddMinutes(-22), Now.AddMinutes(-15), Now.AddMinutes(-8), Now.AddMinutes(-1)]);

        Assert.True(history.IsCrashLoop(Now));
    }

    [Fact]
    public void IsCrashLoop_FourSpreadOut_IsFalse()
    {
        var history = CrashHistory.From([Now.AddMinutes(-29), Now.AddMinutes(-22), Now.AddMinutes(-15), Now.AddMinutes(-1)]);

        Assert.False(history.IsCrashLoop(Now));
    }

    [Fact]
    public void IsCrashLoop_ClockSetBack_DoesNotCountFutureCrashes()
    {
        var history = CrashHistory.From([Now.AddMinutes(1), Now.AddMinutes(2), Now.AddMinutes(3)]);

        Assert.False(history.IsCrashLoop(Now));
    }
}

public class RestartBackoffTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 10)]
    public void TryGetDelay_FollowsTheSchedule(int restarts, int expectedSeconds)
    {
        Assert.True(RestartBackoff.TryGetDelay(restarts, out var delay));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(-1)]
    public void TryGetDelay_OutsideTheSchedule_GivesUp(int restarts)
    {
        Assert.False(RestartBackoff.TryGetDelay(restarts, out var delay));
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void MaxRestarts_IsThree()
    {
        Assert.Equal(3, RestartBackoff.MaxRestarts);
    }
}
