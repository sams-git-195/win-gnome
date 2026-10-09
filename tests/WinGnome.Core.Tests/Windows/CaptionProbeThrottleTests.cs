using WinGnome.Core.Windows;
using Xunit;

namespace WinGnome.Core.Tests.Windows;

public sealed class CaptionProbeThrottleTests
{
    private static readonly ProbeSize SizeA = new(1262, 917, 120);
    private static readonly ProbeSize SizeB = new(1400, 917, 120);

    [Fact]
    public void NextStartAt_NewThrottle_IsNow()
    {
        Assert.Equal(1_000, new CaptionProbeThrottle().NextStartAt(1_000));
    }

    [Fact]
    public void NextStartAt_ThreeProbesWithinAMinute_DelaysTheFourthUntilTheFirstIsAMinuteOld()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordStart(0);
        throttle.RecordStart(10_000);
        throttle.RecordStart(20_000);

        Assert.Equal(60_000, throttle.NextStartAt(30_000));
        Assert.Equal(60_000, throttle.NextStartAt(60_000));
    }

    [Fact]
    public void NextStartAt_TwoProbes_AllowsAThirdNow()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordStart(0);
        throttle.RecordStart(10_000);

        Assert.Equal(20_000, throttle.NextStartAt(20_000));
    }

    [Fact]
    public void NextStartAt_OldProbesAgeOut()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordStart(0);
        throttle.RecordStart(10_000);
        throttle.RecordStart(20_000);
        throttle.RecordStart(60_000);

        Assert.Equal(70_000, throttle.NextStartAt(65_000));
    }

    [Fact]
    public void IsBlocked_OneFailure_AllowsARetryAtTheSameSize()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordFailure(SizeA);

        Assert.False(throttle.IsBlocked(SizeA));
    }

    [Fact]
    public void IsBlocked_TwoFailuresAtTheSameSize_BlocksThatSizeOnly()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordFailure(SizeA);
        throttle.RecordFailure(SizeA);

        Assert.True(throttle.IsBlocked(SizeA));
        Assert.False(throttle.IsBlocked(SizeB));
    }

    [Fact]
    public void IsBlocked_FailureAtAnotherSize_StartsCountingAgain()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordFailure(SizeA);
        throttle.RecordFailure(SizeB);

        Assert.False(throttle.IsBlocked(SizeA));
        Assert.False(throttle.IsBlocked(SizeB));

        throttle.RecordFailure(SizeB);

        Assert.True(throttle.IsBlocked(SizeB));
    }

    [Fact]
    public void IsBlocked_AfterSuccess_IsFalse()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordFailure(SizeA);
        throttle.RecordFailure(SizeA);

        throttle.RecordSuccess();

        Assert.False(throttle.IsBlocked(SizeA));
    }

    [Fact]
    public void IsBlocked_AfterForgetFailures_IsFalse()
    {
        var throttle = new CaptionProbeThrottle();
        throttle.RecordFailure(SizeA);
        throttle.RecordFailure(SizeA);

        throttle.ForgetFailures();

        Assert.False(throttle.IsBlocked(SizeA));
    }

    [Fact]
    public void HasFailed_TracksAnyRecordedFailure()
    {
        var throttle = new CaptionProbeThrottle();
        Assert.False(throttle.HasFailed);

        throttle.RecordFailure(SizeA);

        Assert.True(throttle.HasFailed);
    }
}
