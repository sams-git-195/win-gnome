using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class TaskbarRegionBreakerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static bool ApplyTimes(TaskbarRegionBreaker breaker, long handle, int count, DateTime at)
    {
        var tripped = false;
        for (var i = 0; i < count; i++)
        {
            tripped = breaker.RecordApply(handle, at);
        }

        return tripped;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(20, false)]
    [InlineData(21, true)]
    [InlineData(60, true)]
    public void RecordApply_AppliesWithinTheWindow_TripsPastTwenty(int applies, bool expected)
    {
        Assert.Equal(expected, ApplyTimes(new TaskbarRegionBreaker(), 1, applies, T0));
    }

    [Fact]
    public void RecordApply_SlowApplies_NeverTrip()
    {
        var breaker = new TaskbarRegionBreaker();
        var tripped = false;
        for (var i = 0; i < 100; i++)
        {
            tripped |= breaker.RecordApply(1, T0.AddSeconds(i * 1.0 + (i * 0.2)));
        }

        Assert.False(tripped);
    }

    [Fact]
    public void RecordApply_AppliesExactlyAWindowApart_StillCountTogether()
    {
        var breaker = new TaskbarRegionBreaker();
        ApplyTimes(breaker, 1, 20, T0);

        Assert.True(breaker.RecordApply(1, T0 + TaskbarRegionBreaker.Window));
    }

    [Fact]
    public void RecordApply_OldAppliesLeaveTheWindow()
    {
        var breaker = new TaskbarRegionBreaker();
        ApplyTimes(breaker, 1, 20, T0);

        Assert.False(breaker.RecordApply(1, T0 + TaskbarRegionBreaker.Window + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void RecordApply_WindowsAreCountedSeparately()
    {
        var breaker = new TaskbarRegionBreaker();
        ApplyTimes(breaker, 1, 20, T0);

        Assert.False(ApplyTimes(breaker, 2, 20, T0));
    }

    [Fact]
    public void Reset_ForgetsTheHistory()
    {
        var breaker = new TaskbarRegionBreaker();
        ApplyTimes(breaker, 1, 20, T0);
        breaker.Reset();

        Assert.False(breaker.RecordApply(1, T0));
    }
}
