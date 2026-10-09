using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;

namespace WinGnome.Core.Tests.Monitors;

public class MonitorLayoutTests
{
    internal static MonitorInfo Mon(string key, int left, int top, int right, int bottom, int dpi = 96, bool primary = false) =>
        new(key, new PixelRect(left, top, right, bottom), new PixelRect(left, top, right, bottom), dpi, primary);

    // The QA layout: 100 % primary at the origin, a 150 % secondary above and to the left (negative coordinates).
    private static readonly MonitorInfo Primary = Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1600, 96, primary: true);
    private static readonly MonitorInfo Upper = Mon(@"\\.\DISPLAY2", -447, -1440, 2993, 0, 144);

    [Fact]
    public void Create_KeepsMonitorsInReadOrder_WithTheFlaggedPrimary()
    {
        var layout = MonitorLayout.Create([Upper, Primary]);

        Assert.Equal([Upper, Primary], layout.Monitors);
        Assert.Equal(Primary, layout.Primary);
    }

    [Fact]
    public void Create_Empty_HasNoPrimary()
    {
        var layout = MonitorLayout.Create([]);

        Assert.Empty(layout.Monitors);
        Assert.Null(layout.Primary);
        Assert.Null(layout.At(0, 0));
    }

    [Fact]
    public void Create_DropsEmptyRectangles()
    {
        var layout = MonitorLayout.Create([Primary, Mon(@"\\.\DISPLAY9", 100, 100, 100, 500)]);

        Assert.Equal([Primary], layout.Monitors);
    }

    [Fact]
    public void Create_DuplicateKey_KeepsTheFirst()
    {
        var second = Mon(@"\\.\DISPLAY2", 2560, 0, 4480, 1080);

        var layout = MonitorLayout.Create([Primary, Upper, second]);

        Assert.Equal([Primary, Upper], layout.Monitors);
    }

    [Fact]
    public void Create_DuplicateKeyDifferingInCase_KeepsTheFirst()
    {
        var layout = MonitorLayout.Create([Primary, Mon(@"\\.\display1", 2560, 0, 4480, 1080)]);

        Assert.Equal([Primary], layout.Monitors);
    }

    [Fact]
    public void Create_IdenticalBounds_KeepsOne_PreferringThePrimary()
    {
        // Clone mode, or a glitchy read: two device names for one rectangle.
        var clone = Mon(@"\\.\DISPLAY5", 0, 0, 2560, 1600);

        var layout = MonitorLayout.Create([clone, Primary]);

        Assert.Equal([Primary], layout.Monitors);
    }

    [Fact]
    public void Create_NoPrimaryFlag_MakesTheMonitorAtTheOriginPrimary()
    {
        var layout = MonitorLayout.Create([Upper, Primary with { IsPrimary = false }]);

        Assert.Equal(Primary, layout.Primary);
        Assert.Equal([Upper, Primary], layout.Monitors);
    }

    [Fact]
    public void Create_NoPrimaryFlagAndNothingAtTheOrigin_MakesTheFirstPrimary()
    {
        var right = Mon(@"\\.\DISPLAY3", 2560, 0, 4480, 1080);

        var layout = MonitorLayout.Create([right, Upper]);

        Assert.Equal(right with { IsPrimary = true }, layout.Primary);
    }

    [Fact]
    public void Create_TwoPrimaryFlags_KeepsOnlyTheFirst()
    {
        var layout = MonitorLayout.Create([Primary, Upper with { IsPrimary = true }]);

        Assert.Equal([Primary, Upper], layout.Monitors);
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndNullForUnknownKeys()
    {
        var layout = MonitorLayout.Create([Primary, Upper]);

        Assert.Equal(Upper, layout.Find(@"\\.\display2"));
        Assert.Null(layout.Find(@"\\.\DISPLAY7"));
        Assert.Null(layout.Find(null));
    }

    [Theory]
    [InlineData(0, 0, @"\\.\DISPLAY1")]
    [InlineData(-447, -1440, @"\\.\DISPLAY2")]
    [InlineData(-1, -1, @"\\.\DISPLAY2")]
    [InlineData(2992, -1, @"\\.\DISPLAY2")]
    [InlineData(2559, 1599, @"\\.\DISPLAY1")]
    // The shared horizontal edge (y = 0) belongs to the lower monitor.
    [InlineData(100, 0, @"\\.\DISPLAY1")]
    [InlineData(100, -1, @"\\.\DISPLAY2")]
    public void At_FindsTheMonitorUnderThePoint(int x, int y, string expected)
    {
        var layout = MonitorLayout.Create([Primary, Upper]);

        Assert.Equal(expected, layout.At(x, y)?.Key);
    }

    [Theory]
    [InlineData(2560, 0)] // Right is exclusive.
    [InlineData(0, 1600)] // Bottom is exclusive.
    [InlineData(2993, -1)]
    [InlineData(-448, -1)]
    [InlineData(-1, 0)] // Left of the primary, below the upper monitor.
    public void At_OutsideEveryMonitor_IsNull(int x, int y)
    {
        var layout = MonitorLayout.Create([Primary, Upper]);

        Assert.Null(layout.At(x, y));
    }

    [Fact]
    public void At_SharedVerticalEdge_BelongsToTheRightMonitor()
    {
        var right = Mon(@"\\.\DISPLAY3", 2560, 0, 4480, 1080);
        var layout = MonitorLayout.Create([Primary, right]);

        Assert.Equal(right.Key, layout.At(2560, 10)?.Key);
        Assert.Equal(Primary.Key, layout.At(2559, 10)?.Key);
    }

    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(0, 1.0)]
    public void Scale_FollowsTheDpi(int dpi, double expected)
    {
        Assert.Equal(expected, (Primary with { Dpi = dpi }).Scale);
    }
}
