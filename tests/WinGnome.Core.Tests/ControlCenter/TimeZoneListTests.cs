using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class TimeZoneListTests
{
    private static readonly TimeZoneEntry Utc = new("UTC", "(UTC) Coordinated Universal Time", TimeSpan.Zero);
    private static readonly TimeZoneEntry London = new("GMT Standard Time", "(UTC+00:00) Dublin, Edinburgh, Lisbon, London", TimeSpan.Zero);
    private static readonly TimeZoneEntry Berlin = new("W. Europe Standard Time", "(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna", TimeSpan.FromHours(1));
    private static readonly TimeZoneEntry NewYork = new("Eastern Standard Time", "(UTC-05:00) Eastern Time (US & Canada)", TimeSpan.FromHours(-5));
    private static readonly TimeZoneEntry Kolkata = new("India Standard Time", "(UTC+05:30) Chennai, Kolkata, Mumbai, New Delhi", new TimeSpan(5, 30, 0));

    [Fact]
    public void Sort_OrdersByOffsetThenName()
    {
        var sorted = TimeZoneList.Sort([Kolkata, Berlin, Utc, NewYork, London]);

        Assert.Equal([NewYork, Utc, London, Berlin, Kolkata], sorted);
    }

    [Fact]
    public void Sort_Empty_ReturnsEmpty()
    {
        Assert.Empty(TimeZoneList.Sort([]));
    }

    [Theory]
    [InlineData("berlin")]
    [InlineData("BERLIN")]
    [InlineData("europe")]
    [InlineData("w. europe standard")]
    public void Filter_MatchesCitiesAndIds(string query)
    {
        Assert.Equal([Berlin], TimeZoneList.Filter([NewYork, Utc, London, Berlin, Kolkata], query));
    }

    [Fact]
    public void Filter_KeepsTheListOrder()
    {
        // "standard" is in four ids; the result keeps the offset order rather than ranking.
        var result = TimeZoneList.Filter([NewYork, Utc, London, Berlin, Kolkata], "standard");

        Assert.Equal([NewYork, London, Berlin, Kolkata], result);
    }

    [Fact]
    public void Filter_EmptyQuery_ReturnsEverything()
    {
        var all = new[] { NewYork, Utc };

        Assert.Equal(all, TimeZoneList.Filter(all, " "));
    }

    [Fact]
    public void Filter_ScatteredLetters_DoNotMatch()
    {
        // "bln" is a subsequence of "Berlin" but not a word or substring of any entry.
        Assert.Empty(TimeZoneList.Filter([Berlin], "bln"));
    }

    [Fact]
    public void IndexOf_FindsTheCurrentZoneById()
    {
        Assert.Equal(2, TimeZoneList.IndexOf([NewYork, Utc, Berlin], "W. Europe Standard Time"));
        Assert.Equal(-1, TimeZoneList.IndexOf([NewYork, Utc], "W. Europe Standard Time"));
    }
}
