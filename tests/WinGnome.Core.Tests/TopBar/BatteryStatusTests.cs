using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class BatteryStatusTests
{
    private const uint UnknownTime = uint.MaxValue;

    [Theory]
    [InlineData((byte)1, (byte)128, (byte)255, false)] // no system battery
    [InlineData((byte)255, (byte)255, (byte)255, false)] // nothing known
    [InlineData((byte)1, (byte)8, (byte)40, true)] // charging
    [InlineData((byte)0, (byte)0, (byte)90, true)] // discharging
    public void NeedsPolling_OnlyWithABattery(byte acLine, byte flag, byte percent, bool expected)
    {
        Assert.Equal(expected, BatteryStatus.FromPowerStatus(acLine, flag, percent, UnknownTime).NeedsPolling);
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
