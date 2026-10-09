using WinGnome.Core.Monitors;
using static WinGnome.Core.Tests.Monitors.MonitorLayoutTests;

namespace WinGnome.Core.Tests.Monitors;

public class HotCornerRulesTests
{
    private static readonly MonitorInfo Primary = Mon("P", 0, 0, 1920, 1080, primary: true);

    private static bool PrimaryCorner(params MonitorInfo[] others) =>
        HotCornerRules.IsTrueCorner(Primary, MonitorLayout.Create([Primary, .. others]));

    [Fact]
    public void SingleMonitor_IsATrueCorner()
    {
        Assert.True(PrimaryCorner());
    }

    [Fact]
    public void NeighbourDirectlyLeft_IsNotACorner()
    {
        Assert.False(PrimaryCorner(Mon("L", -1920, 0, 0, 1080)));
    }

    [Fact]
    public void NeighbourDirectlyAbove_IsNotACorner()
    {
        Assert.False(PrimaryCorner(Mon("U", 0, -1080, 1920, 0)));
    }

    [Fact]
    public void WideNeighbourAboveAndToTheLeft_IsNotACorner()
    {
        // The QA layout: an ultrawide above, starting left of the primary.
        Assert.False(PrimaryCorner(Mon("U", -447, -1440, 2993, 0)));
    }

    [Fact]
    public void DiagonalOnlyNeighbour_KeepsTheCorner()
    {
        Assert.True(PrimaryCorner(Mon("D", -1920, -1080, 0, 0)));
    }

    [Fact]
    public void NeighbourAboveStartingRightOfTheCorner_KeepsTheCorner()
    {
        Assert.True(PrimaryCorner(Mon("U", 1, -1080, 1921, 0)));
    }

    [Fact]
    public void NeighbourLeftStartingBelowTheCorner_KeepsTheCorner()
    {
        Assert.True(PrimaryCorner(Mon("L", -1920, 1, 0, 1081)));
    }

    [Fact]
    public void NeighbourLeftEndingAtTheCornersRow_KeepsTheCorner()
    {
        // Its bottom edge is exclusive, so (−1, 0) is not on it.
        Assert.True(PrimaryCorner(Mon("L", -1920, -1080, 0, 0)));
    }

    [Fact]
    public void NeighbourAboveWithAGap_KeepsTheCorner()
    {
        Assert.True(PrimaryCorner(Mon("U", 0, -1081, 1920, -1)));
    }

    [Fact]
    public void SecondaryAtNegativeCoordinates_WithThePrimaryBelowAndRight_IsATrueCorner()
    {
        var upper = Mon("U", -447, -1440, 2993, 0);

        Assert.True(HotCornerRules.IsTrueCorner(upper, MonitorLayout.Create([Primary, upper])));
    }

    [Fact]
    public void SecondaryRightOfThePrimary_IsNotACorner()
    {
        var right = Mon("R", 1920, 0, 3840, 1080);

        Assert.False(HotCornerRules.IsTrueCorner(right, MonitorLayout.Create([Primary, right])));
        Assert.True(HotCornerRules.IsTrueCorner(Primary, MonitorLayout.Create([Primary, right])));
    }
}
