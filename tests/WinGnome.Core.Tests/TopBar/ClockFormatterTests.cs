using System.Globalization;
using WinGnome.Core.Settings;
using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class ClockFormatterTests
{
    // Wednesday 8 October 2025, 14:05:09.250
    private static readonly DateTime Now = new DateTime(2025, 10, 8, 14, 5, 9, 250);

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static TopBarSettings Settings(bool weekday = true, bool date = true, bool seconds = false, ClockStyle style = ClockStyle.TwentyFourHour) =>
        new() { ShowWeekday = weekday, ShowDate = date, ShowSeconds = seconds, ClockStyle = style };

    [Fact]
    public void Default_IsGnomeStyle()
    {
        Assert.Equal("Wed 8 Oct  14:05", ClockFormatter.Format(Now, Settings(), Invariant));
    }

    [Fact]
    public void DefaultSettingsObject_FormatsTheSame()
    {
        Assert.Equal("Wed 8 Oct  14:05", ClockFormatter.Format(Now, new TopBarSettings(), Invariant));
    }

    [Fact]
    public void WeekdayOnly()
    {
        Assert.Equal("Wed  14:05", ClockFormatter.Format(Now, Settings(date: false), Invariant));
    }

    [Fact]
    public void DateOnly()
    {
        Assert.Equal("8 Oct  14:05", ClockFormatter.Format(Now, Settings(weekday: false), Invariant));
    }

    [Fact]
    public void TimeOnly()
    {
        Assert.Equal("14:05", ClockFormatter.Format(Now, Settings(weekday: false, date: false), Invariant));
    }

    [Fact]
    public void Seconds_AreShownWhenEnabled()
    {
        Assert.Equal("Wed 8 Oct  14:05:09", ClockFormatter.Format(Now, Settings(seconds: true), Invariant));
        Assert.Equal("14:05:09", ClockFormatter.Format(Now, Settings(false, false, true), Invariant));
    }

    [Fact]
    public void TwentyFourHour_PadsTheHour()
    {
        var early = new DateTime(2025, 1, 2, 3, 4, 5);
        Assert.Equal("Thu 2 Jan  03:04", ClockFormatter.Format(early, Settings(), Invariant));
    }

    [Fact]
    public void TwelveHour_Afternoon()
    {
        var text = ClockFormatter.Format(Now, Settings(style: ClockStyle.TwelveHour), new CultureInfo("en-US"));
        Assert.Equal("Wed 8 Oct  2:05 PM", text);
    }

    [Fact]
    public void TwelveHour_WithSeconds()
    {
        var text = ClockFormatter.Format(Now, Settings(seconds: true, style: ClockStyle.TwelveHour), new CultureInfo("en-US"));
        Assert.Equal("Wed 8 Oct  2:05:09 PM", text);
    }

    [Theory]
    [InlineData(0, 30, "12:30 AM")]
    [InlineData(9, 0, "9:00 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(23, 59, "11:59 PM")]
    public void TwelveHour_Boundaries(int hour, int minute, string expected)
    {
        var time = new DateTime(2025, 10, 8, hour, minute, 0);
        var text = ClockFormatter.Format(time, Settings(false, false, style: ClockStyle.TwelveHour), new CultureInfo("en-US"));
        Assert.Equal(expected, text);
    }

    [Fact]
    public void UsesTheCulturesAbbreviatedNames()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.AbbreviatedDayNames = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"];
        culture.DateTimeFormat.AbbreviatedMonthNames = ["Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez", ""];

        Assert.Equal("Mi 8 Okt  14:05", ClockFormatter.Format(Now, Settings(), culture));
    }

    [Fact]
    public void DayOfMonth_HasNoLeadingZero_AndTwoDigitsWhenNeeded()
    {
        Assert.Equal("Sun 5 Jan  00:00", ClockFormatter.Format(new DateTime(2025, 1, 5), Settings(), Invariant));
        Assert.Equal("Fri 31 Oct  00:00", ClockFormatter.Format(new DateTime(2025, 10, 31), Settings(), Invariant));
    }

    [Fact]
    public void NextTick_Minutes_IsTimeUntilNextMinuteBoundary()
    {
        Assert.Equal(TimeSpan.FromSeconds(50.75), ClockFormatter.NextTickDelay(Now, showSeconds: false));
    }

    [Fact]
    public void NextTick_Seconds_IsTimeUntilNextSecondBoundary()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(750), ClockFormatter.NextTickDelay(Now, showSeconds: true));
    }

    [Fact]
    public void NextTick_ExactlyOnBoundary_WaitsAFullInterval()
    {
        var onMinute = new DateTime(2025, 10, 8, 14, 5, 0);

        Assert.Equal(TimeSpan.FromMinutes(1), ClockFormatter.NextTickDelay(onMinute, false));
        Assert.Equal(TimeSpan.FromSeconds(1), ClockFormatter.NextTickDelay(onMinute, true));
    }

    [Fact]
    public void NextTick_NeverLessThanFiftyMilliseconds()
    {
        var almostMinute = new DateTime(2025, 10, 8, 14, 5, 59, 990);
        var almostSecond = new DateTime(2025, 10, 8, 14, 5, 9, 990);

        Assert.Equal(TimeSpan.FromMilliseconds(50), ClockFormatter.NextTickDelay(almostMinute, false));
        Assert.Equal(TimeSpan.FromMilliseconds(50), ClockFormatter.NextTickDelay(almostSecond, true));
    }

    [Fact]
    public void NextTick_AtFiftyMillisecondsExactly_IsKept()
    {
        var time = new DateTime(2025, 10, 8, 14, 5, 9, 950);
        Assert.Equal(TimeSpan.FromMilliseconds(50), ClockFormatter.NextTickDelay(time, true));
    }

    [Fact]
    public void NextTick_ResultAlwaysLandsOnOrPastTheBoundary()
    {
        for (var ms = 0; ms < 1000; ms += 37)
        {
            var time = new DateTime(2025, 10, 8, 14, 5, 30, ms);
            var next = time + ClockFormatter.NextTickDelay(time, true);
            Assert.True(next.Second != time.Second || next.Minute != time.Minute);
        }
    }
}
