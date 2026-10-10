using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class TaskbarRehidePolicyTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TaskbarRehideDecision ShowTimes(TaskbarRehidePolicy policy, int count, DateTime at)
    {
        var last = default(TaskbarRehideDecision);
        for (var i = 0; i < count; i++)
        {
            last = policy.OnShow(at);
        }

        return last;
    }

    [Theory]
    [InlineData(1, 50, TaskbarRehideLevel.Normal)]
    [InlineData(4, 50, TaskbarRehideLevel.Normal)]
    [InlineData(5, 750, TaskbarRehideLevel.Burst)]
    [InlineData(11, 750, TaskbarRehideLevel.Burst)]
    [InlineData(12, 3000, TaskbarRehideLevel.Runaway)]
    [InlineData(40, 3000, TaskbarRehideLevel.Runaway)]
    public void OnShow_ShowsInWindow_PicksTheRampedDelay(int shows, int expectedMs, TaskbarRehideLevel expectedLevel)
    {
        var decision = ShowTimes(new TaskbarRehidePolicy(), shows, T0);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), decision.Delay);
        Assert.Equal(expectedLevel, decision.Level);
    }

    [Fact]
    public void OnShow_EmptyHistory_IsNormalAndNotEscalated()
    {
        var decision = new TaskbarRehidePolicy().OnShow(T0);

        Assert.Equal(new TaskbarRehideDecision(TimeSpan.FromMilliseconds(50), TaskbarRehideLevel.Normal, TaskbarRehideLevel.Normal), decision);
        Assert.False(decision.Escalated);
        Assert.False(decision.Recovered);
    }

    [Theory]
    [InlineData(-1, 750)]
    [InlineData(0, 750)]
    [InlineData(1, 50)]
    public void OnShow_OldestShowAroundTheWindowEdge_CountsOnlyWhenNotStrictlyOlder(int ticksPastWindow, int expectedMs)
    {
        var policy = new TaskbarRehidePolicy();
        ShowTimes(policy, 4, T0);

        var decision = policy.OnShow(T0 + TaskbarRehidePolicy.Window + TimeSpan.FromTicks(ticksPastWindow));

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), decision.Delay);
    }

    [Fact]
    public void OnShow_FirstShowOfABurst_EscalatesFromNormalToBurst()
    {
        var policy = new TaskbarRehidePolicy();
        ShowTimes(policy, 4, T0);

        var decision = policy.OnShow(T0);

        Assert.Equal(TaskbarRehideLevel.Normal, decision.PreviousLevel);
        Assert.Equal(TaskbarRehideLevel.Burst, decision.Level);
        Assert.True(decision.Escalated);
        Assert.False(decision.Recovered);
    }

    [Fact]
    public void OnShow_StayingInBurst_IsNeitherEscalatedNorRecovered()
    {
        var policy = new TaskbarRehidePolicy();
        ShowTimes(policy, 5, T0);

        var decision = policy.OnShow(T0);

        Assert.Equal(TaskbarRehideLevel.Burst, decision.Level);
        Assert.False(decision.Escalated);
        Assert.False(decision.Recovered);
    }

    [Fact]
    public void OnShow_BurstGrowsToRunaway_EscalatesAgain()
    {
        var policy = new TaskbarRehidePolicy();
        ShowTimes(policy, 11, T0);

        var decision = policy.OnShow(T0);

        Assert.Equal(TaskbarRehideLevel.Burst, decision.PreviousLevel);
        Assert.Equal(TaskbarRehideLevel.Runaway, decision.Level);
        Assert.True(decision.Escalated);
    }

    [Fact]
    public void OnShow_QuietAfterBurst_RecoversToNormal()
    {
        var policy = new TaskbarRehidePolicy();
        ShowTimes(policy, 6, T0);

        var decision = policy.OnShow(T0 + TimeSpan.FromSeconds(30));

        Assert.Equal(TaskbarRehideLevel.Burst, decision.PreviousLevel);
        Assert.Equal(TaskbarRehideLevel.Normal, decision.Level);
        Assert.Equal(TimeSpan.FromMilliseconds(50), decision.Delay);
        Assert.True(decision.Recovered);
        Assert.False(decision.Escalated);
    }

    [Fact]
    public void OnShow_RunawayDrainsGradually_StepsDownThroughBurst()
    {
        var policy = new TaskbarRehidePolicy();
        ShowTimes(policy, 12, T0);

        // Past the window the 12 old shows are gone; 5 fresh ones put it at Burst, not straight back to Normal.
        var decision = ShowTimes(policy, 5, T0 + TimeSpan.FromSeconds(11));

        Assert.Equal(TaskbarRehideLevel.Burst, decision.Level);
        Assert.Equal(TimeSpan.FromMilliseconds(750), decision.Delay);
    }
}
