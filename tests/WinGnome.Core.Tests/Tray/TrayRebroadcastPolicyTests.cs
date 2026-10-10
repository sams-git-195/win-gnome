using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class TrayRebroadcastPolicyTests
{
    [Theory]
    [InlineData(1999, false)]
    [InlineData(2000, true)]
    public void ShouldSendHeal_AllowedOnlyAtOrAfterTheGracePoint(long nowMs, bool expected)
    {
        var policy = new TrayRebroadcastPolicy();
        policy.OnHostStarted(0);

        Assert.Equal(expected, policy.ShouldSendHeal(nowMs));
    }

    [Fact]
    public void ShouldSendHeal_SecondCall_RefusedEvenAfterTheCooldownEnds()
    {
        var policy = new TrayRebroadcastPolicy();
        policy.OnHostStarted(0);
        Assert.True(policy.ShouldSendHeal(2000));
        policy.OnBroadcastSent(2000);

        // Long after the heal's own cooldown: the one heal per host start is spent.
        Assert.False(policy.ShouldSendHeal(50_000));
    }

    [Fact]
    public void ShouldSendHeal_BroadcastInsideTheGraceWindow_SkipsTheHealForThisHostStart()
    {
        var policy = new TrayRebroadcastPolicy();
        policy.OnHostStarted(0);

        // Explorer restarted just after start-up; its rebroadcast went out before the heal was due and already
        // made every app re-register, so the heal is skipped instead of doubling the storm.
        policy.OnBroadcastSent(1500);

        Assert.False(policy.ShouldSendHeal(2000));
        Assert.False(policy.ShouldSendHeal(50_000));
    }

    [Fact]
    public void OnHostStarted_AfterTheStartupBroadcast_StillAllowsTheHeal()
    {
        var policy = new TrayRebroadcastPolicy();

        // The host reports its start-up broadcast before arming the schedule; that broadcast is what the heal
        // repairs (it may have reached apps while Explorer was in front), not a reason to skip it.
        policy.OnBroadcastSent(1000);
        policy.OnHostStarted(1000);

        Assert.True(policy.ShouldSendHeal(3000));
    }

    [Fact]
    public void OnHostStarted_Twice_RearmsTheHeal()
    {
        var policy = new TrayRebroadcastPolicy();
        policy.OnHostStarted(0);
        Assert.True(policy.ShouldSendHeal(2000));

        policy.OnHostStarted(30_000);

        Assert.False(policy.ShouldSendHeal(31_999));
        Assert.True(policy.ShouldSendHeal(32_000));
    }

    [Fact]
    public void ShouldSendHeal_BeforeAnyHostStart_Refused()
    {
        Assert.False(new TrayRebroadcastPolicy().ShouldSendHeal(100_000));
    }
}
