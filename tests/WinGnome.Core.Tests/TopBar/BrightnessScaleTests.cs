using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class BrightnessScaleTests
{
    private static readonly int[] Full = Enumerable.Range(0, 101).ToArray();
    private static readonly int[] Coarse = [0, 25, 50, 75, 100];
    private static readonly int[] Raised = [10, 20, 30];

    [Fact]
    public void NormalizeLevels_SortsDeduplicatesAndDropsOutOfRange()
    {
        Assert.Equal([0, 20, 50, 100], BrightnessScale.NormalizeLevels([50, 100, 20, 20, -5, 0, 101, 300]));
    }

    [Fact]
    public void NormalizeLevels_Empty_StaysEmpty()
    {
        Assert.Empty(BrightnessScale.NormalizeLevels([]));
    }

    [Theory]
    [InlineData(33.4, 33)]
    [InlineData(33.5, 34)]
    [InlineData(0.0, 0)]
    [InlineData(100.0, 100)]
    [InlineData(-20.0, 0)]
    [InlineData(250.0, 100)]
    [InlineData(double.NaN, 0)]
    public void Snap_FullRange_RoundsToWholePercent(double percent, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Snap(percent, Full));
    }

    [Theory]
    [InlineData(10.0, 0)]
    [InlineData(12.4, 0)]
    [InlineData(12.5, 25)]
    [InlineData(60.0, 50)]
    [InlineData(63.0, 75)]
    [InlineData(99.0, 100)]
    public void Snap_CoarseLevels_PicksNearestAndTiesGoBrighter(double percent, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Snap(percent, Coarse));
    }

    [Theory]
    [InlineData(0.0, 10)]
    [InlineData(100.0, 30)]
    public void Snap_PanelWithRaisedMinimum_StaysWithinSupportedLevels(double percent, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Snap(percent, Raised));
    }

    [Fact]
    public void Nudge_OneNotchIsFivePercent()
    {
        Assert.Equal(55, BrightnessScale.Nudge(50, 120, Full));
        Assert.Equal(45, BrightnessScale.Nudge(50, -120, Full));
    }

    [Fact]
    public void Nudge_IsProportionalForHighResolutionWheels()
    {
        Assert.Equal(53, BrightnessScale.Nudge(50, 60, Full));
        Assert.Equal(65, BrightnessScale.Nudge(50, 360, Full));
    }

    [Theory]
    [InlineData(98, 240, 100)]
    [InlineData(2, -240, 0)]
    public void Nudge_ClampsAtTheEnds(int current, int delta, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Nudge(current, delta, Full));
    }

    [Theory]
    [InlineData(0, 120, 25)]
    [InlineData(25, 120, 50)]
    [InlineData(100, 120, 100)]
    [InlineData(50, -120, 25)]
    [InlineData(0, -120, 0)]
    [InlineData(50, 30, 75)]
    public void Nudge_CoarseLevels_AlwaysReachesTheNeighbour(int current, int delta, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Nudge(current, delta, Coarse));
    }

    [Fact]
    public void Nudge_ZeroDelta_KeepsTheLevel()
    {
        Assert.Equal(50, BrightnessScale.Nudge(50, 0, Coarse));
    }
}
