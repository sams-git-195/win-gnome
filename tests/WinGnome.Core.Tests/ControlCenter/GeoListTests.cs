using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class GeoListTests
{
    private static readonly GeoEntry Gb = new("GB", "United Kingdom");
    private static readonly GeoEntry Us = new("US", "United States");
    private static readonly GeoEntry Fr = new("FR", "France");
    private static readonly GeoEntry Ax = new("AX", "Åland Islands");
    private static readonly GeoEntry Al = new("AL", "Albania");
    private static readonly GeoEntry Ci = new("CI", "Côte d’Ivoire");

    [Fact]
    public void Sort_OrdersByDisplayName_IgnoringCase()
    {
        var sorted = GeoList.Sort([Us, new GeoEntry("DE", "germany"), Gb, Fr]);

        Assert.Equal(["France", "germany", "United Kingdom", "United States"], sorted.Select(e => e.DisplayName));
    }

    [Fact]
    public void Sort_AccentedInitial_SortsWithItsLetter()
    {
        var sorted = GeoList.Sort([Us, Al, Ax]);

        Assert.Equal(["AX", "AL", "US"], sorted.Select(e => e.Name));
    }

    [Fact]
    public void Sort_Empty_ReturnsEmpty()
    {
        Assert.Empty(GeoList.Sort([]));
    }

    [Theory]
    [InlineData("united")]
    [InlineData("UNITED")]
    [InlineData("kingdom")]
    public void Filter_MatchesDisplayNameSubstring(string query)
    {
        Assert.Equal([Gb], GeoList.Filter([Fr, Gb], query));
    }

    [Fact]
    public void Filter_MatchesCode()
    {
        Assert.Equal([Us], GeoList.Filter([Fr, Gb, Us], "us"));
    }

    [Fact]
    public void Filter_IgnoresAccents()
    {
        Assert.Equal([Ci], GeoList.Filter([Fr, Ci], "cote"));
    }

    [Fact]
    public void Filter_KeepsListOrder()
    {
        Assert.Equal([Gb, Us], GeoList.Filter([Fr, Gb, Us], "united"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Filter_BlankQuery_ReturnsEverything(string query)
    {
        GeoEntry[] all = [Fr, Gb];

        Assert.Equal(all, GeoList.Filter(all, query));
    }

    [Fact]
    public void Filter_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(GeoList.Filter([Fr, Gb], "zzz"));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Equal(Gb, GeoList.Find([Fr, Gb], "gb"));
    }

    [Theory]
    [InlineData("XX")]
    [InlineData(null)]
    public void Find_Unknown_ReturnsNull(string? name)
    {
        Assert.Null(GeoList.Find([Fr, Gb], name));
    }
}
