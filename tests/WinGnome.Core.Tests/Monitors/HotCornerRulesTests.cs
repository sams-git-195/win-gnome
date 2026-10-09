using WinGnome.Core.Input;
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

    // The user's layout in physical pixels: a 2048x1280 (logical) primary at 125 % is 2560x1600, and a 3440x1440
    // secondary at 100 % above it, starting 447 px further left.
    private static readonly MonitorInfo UserPrimary = Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1600, 120, primary: true);
    private static readonly MonitorInfo UserUpper = Mon(@"\\.\DISPLAY2", -447, -1440, 2993, 0, 96);
    private static readonly MonitorLayout UserLayout = MonitorLayout.Create([UserPrimary, UserUpper]);

    [Fact]
    public void KindOf_UserLayout_PrimaryIsGuarded_UpperIsACorner()
    {
        Assert.Equal(HotCornerKind.Guarded, HotCornerRules.KindOf(UserPrimary, UserLayout));
        Assert.Equal(HotCornerKind.Corner, HotCornerRules.KindOf(UserUpper, UserLayout));
    }

    [Fact]
    public void KindOf_SinglePrimary_IsACorner()
    {
        Assert.Equal(HotCornerKind.Corner, HotCornerRules.KindOf(Primary, MonitorLayout.Create([Primary])));
    }

    [Fact]
    public void KindOf_SecondaryWithANeighbourAtItsCorner_IsNone()
    {
        var right = Mon("R", 1920, 0, 3840, 1080);

        Assert.Equal(HotCornerKind.None, HotCornerRules.KindOf(right, MonitorLayout.Create([Primary, right])));
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(150, 300)]
    [InlineData(300, 300)]
    [InlineData(301, 301)]
    [InlineData(2000, 2000)]
    public void GuardedDwellMs_IsTheDelayButAtLeastTheMinimum(int delayMs, int expected)
    {
        Assert.Equal(expected, HotCornerRules.GuardedDwellMs(delayMs));
    }

    /// <summary>
    /// The primary's guarded corner on the user's layout, sampled every 50 ms as HotCornerWatcher does: samples on the
    /// upper monitor reset the detector, as the watcher does for any monitor without that corner.
    /// </summary>
    private static bool Guarded(int delayMs, params (int X, int Y)[] samples)
    {
        var detector = new HotCornerDetector(HotCornerRules.GuardedDwellMs(delayMs), HotCornerRules.GuardedSizePx);
        var fired = false;
        for (var i = 0; i < samples.Length; i++)
        {
            if (UserLayout.At(samples[i].X, samples[i].Y)?.IsPrimary == true)
            {
                fired |= detector.Update(samples[i].X, samples[i].Y, UserPrimary.Bounds, i * 50L);
            }
            else
            {
                detector.Reset();
            }
        }

        return fired;
    }

    [Theory]
    // Resting at the corner (the left edge clamps x; y rests near the top): fires after 300 ms even with no delay.
    [InlineData(0, 0, 0, 7, true)]
    [InlineData(150, 3, 2, 7, true)]
    [InlineData(0, 7, 7, 7, true)]
    [InlineData(500, 0, 0, 11, true)]
    // Not long enough yet: 250 ms, or 450 ms with a 500 ms delay.
    [InlineData(0, 0, 0, 6, false)]
    [InlineData(500, 0, 0, 10, false)]
    // Just outside the 8 px box.
    [InlineData(0, 8, 0, 20, false)]
    [InlineData(0, 0, 8, 20, false)]
    public void GuardedCorner_FiresOnlyAfterRestingInTheBox(int delayMs, int x, int y, int sampleCount, bool expected)
    {
        Assert.Equal(expected, Guarded(delayMs, Enumerable.Repeat((x, y), sampleCount).ToArray()));
    }

    [Fact]
    public void GuardedCorner_PassingThroughToTheUpperMonitor_NeverFires()
    {
        // Up the left edge of the primary and on into the upper monitor, one sample in the box on the way.
        Assert.False(Guarded(0, (0, 40), (0, 4), (0, -30), (0, -60), (2, -200), (2, -200), (2, -200), (2, -200)));
    }

    [Fact]
    public void GuardedCorner_RestingAgainAfterADipIntoTheUpperMonitor_StartsTheDwellAfresh()
    {
        // 250 ms in the box, a dip into the upper monitor, then 250 ms more: neither stay is long enough.
        Assert.False(Guarded(0, (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, -5), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)));
    }
}
