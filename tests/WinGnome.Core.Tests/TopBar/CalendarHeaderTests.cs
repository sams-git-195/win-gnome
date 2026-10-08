using System.Globalization;
using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class CalendarHeaderTests
{
    private static readonly DateTime Wednesday = new(2026, 10, 7);

    [Theory]
    [InlineData("dddd, MMMM d, yyyy", "MMMM d, yyyy")]
    [InlineData("dddd, d MMMM yyyy", "d MMMM yyyy")]
    [InlineData("dddd d MMMM yyyy", "d MMMM yyyy")]
    [InlineData("dddd, d. MMMM yyyy", "d. MMMM yyyy")]
    [InlineData("yyyy'年'M'月'd'日' dddd", "yyyy'年'M'月'd'日'")]
    [InlineData("yyyy. MMMM d., dddd", "yyyy. MMMM d.")]
    [InlineData("dddd, d' de 'MMMM' de 'yyyy", "d' de 'MMMM' de 'yyyy")]
    [InlineData("ddd dd MMM yyyy", "d MMM yyyy")]
    [InlineData("dd MMMM yyyy", "d MMMM yyyy")]
    public void HeaderDatePattern_RemovesWeekdayAndLeadingZero(string pattern, string expected)
    {
        Assert.Equal(expected, CalendarHeader.HeaderDatePattern(pattern));
    }

    [Fact]
    public void HeaderDatePattern_KeepsQuotedAndEscapedLetters()
    {
        Assert.Equal("'dddd' d MMMM", CalendarHeader.HeaderDatePattern("'dddd' d MMMM"));
        Assert.Equal(@"\d d MMMM", CalendarHeader.HeaderDatePattern(@"\d d MMMM dddd"));
    }

    [Fact]
    public void HeaderDatePattern_FallsBackWhenNothingIsLeft()
    {
        Assert.Equal("d MMMM yyyy", CalendarHeader.HeaderDatePattern("dddd"));
    }

    [Fact]
    public void HeaderDatePattern_SingleLetterStaysCustom()
    {
        var pattern = CalendarHeader.HeaderDatePattern("dddd dd");

        Assert.Equal("%d", pattern);
        Assert.Equal("7", new DateTime(2026, 10, 7).ToString(pattern, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InvariantCulture()
    {
        Assert.Equal("Wednesday", CalendarHeader.Weekday(Wednesday, CultureInfo.InvariantCulture));
        Assert.Equal("7 October 2026", CalendarHeader.LongDate(Wednesday, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Weekday_IsCapitalised()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.DayNames = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];

        Assert.Equal("Mercredi", CalendarHeader.Weekday(Wednesday, culture));
    }
}
