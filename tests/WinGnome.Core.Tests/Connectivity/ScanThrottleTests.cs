using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class ScanThrottleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryBegin_FirstCall_IsAllowed() => Assert.True(new ScanThrottle().TryBegin(T0));

    [Fact]
    public void TryBegin_WithinFiveSeconds_IsIgnored()
    {
        var throttle = new ScanThrottle();
        throttle.TryBegin(T0);

        Assert.False(throttle.TryBegin(T0.AddSeconds(4.999)));
    }

    [Fact]
    public void TryBegin_AtFiveSeconds_IsAllowed()
    {
        var throttle = new ScanThrottle();
        throttle.TryBegin(T0);

        Assert.True(throttle.TryBegin(T0.AddSeconds(5)));
    }

    [Fact]
    public void TryBegin_AnIgnoredCall_DoesNotRestartTheWindow()
    {
        var throttle = new ScanThrottle();
        throttle.TryBegin(T0);
        throttle.TryBegin(T0.AddSeconds(4));

        Assert.True(throttle.TryBegin(T0.AddSeconds(5)));
    }
}
