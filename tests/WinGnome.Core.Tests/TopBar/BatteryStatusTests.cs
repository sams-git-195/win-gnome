using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class BatteryStatusTests
{
    private const uint UnknownTime = uint.MaxValue;

    [Theory]
    [InlineData((byte)128, false)] // no system battery
    [InlineData((byte)(128 | 8), false)] // no system battery, whatever else is set
    [InlineData((byte)255, true)] // unknown: may become known without a broadcast
    [InlineData((byte)8, true)] // charging
    [InlineData((byte)0, true)] // discharging, no level flags
    public void NeedsPolling_StopsOnlyWhenWindowsReportsNoBattery(byte flag, bool expected)
    {
        Assert.Equal(expected, BatteryStatus.NeedsPolling(flag));
    }

    [Fact]
    public void NoSystemBattery_IsNone()
    {
        Assert.Equal(BatteryStatus.None, BatteryStatus.FromPowerStatus(1, 128, 255, UnknownTime));
    }

    [Fact]
    public void UnknownFlagAndPercent_IsNone()
    {
        Assert.False(BatteryStatus.FromPowerStatus(255, 255, 255, UnknownTime).HasBattery);
    }

    [Fact]
    public void UnknownFlagWithKnownPercent_StillHasBattery()
    {
        var status = BatteryStatus.FromPowerStatus(0, 255, 40, UnknownTime);

        Assert.True(status.HasBattery);
        Assert.Equal(40, status.Percent);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void Discharging_ReportsRemainingTime()
    {
        var status = BatteryStatus.FromPowerStatus(0, 1, 73, 7800);

        Assert.Equal(new BatteryStatus(true, 73, false, false, TimeSpan.FromMinutes(130)), status);
        Assert.Equal("73% · 2 h 10 min left", status.Summary);
    }

    [Fact]
    public void Charging_IgnoresRemainingTime()
    {
        var status = BatteryStatus.FromPowerStatus(1, 8 | 1, 55, 1234);

        Assert.True(status.IsCharging);
        Assert.True(status.IsPluggedIn);
        Assert.Null(status.Remaining);
        Assert.Equal("55% · Charging", status.Summary);
    }

    [Fact]
    public void PluggedInAndFull_IsFullyCharged()
    {
        Assert.Equal("100% · Fully charged", BatteryStatus.FromPowerStatus(1, 1, 100, UnknownTime).Summary);
    }

    [Fact]
    public void PluggedInButNotCharging()
    {
        Assert.Equal("80% · Plugged in, not charging", BatteryStatus.FromPowerStatus(1, 1, 80, UnknownTime).Summary);
    }

    [Fact]
    public void OnBatteryWithoutEstimate()
    {
        Assert.Equal("30% · On battery", BatteryStatus.FromPowerStatus(0, 2, 30, UnknownTime).Summary);
    }

    [Fact]
    public void UnknownPercent_HasNoPercentText()
    {
        var status = BatteryStatus.FromPowerStatus(0, 1, 255, UnknownTime);

        Assert.True(status.HasBattery);
        Assert.Null(status.Percent);
        Assert.Null(status.GlyphLevel);
        Assert.Equal("", status.PercentText);
        Assert.Equal("On battery", status.Summary);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(49, 5)]
    [InlineData(94, 9)]
    [InlineData(95, 10)]
    [InlineData(100, 10)]
    public void GlyphLevel_RoundsToTenths(byte percent, int expected)
    {
        Assert.Equal(expected, BatteryStatus.FromPowerStatus(0, 1, percent, UnknownTime).GlyphLevel);
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h 00 min")]
    [InlineData(125, "2 h 05 min")]
    [InlineData(0, "0 min")]
    public void FormatRemaining(int minutes, string expected)
    {
        Assert.Equal(expected, BatteryStatus.FormatRemaining(TimeSpan.FromMinutes(minutes)));
    }
}
