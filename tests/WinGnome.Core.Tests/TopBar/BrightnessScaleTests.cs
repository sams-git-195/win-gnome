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
    [InlineData(50, 240, 75)]
    public void Nudge_CoarseLevels_AWholeNotchAlwaysReachesTheNeighbour(int current, int delta, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Nudge(current, delta, Coarse));
    }

    [Theory]
    [InlineData(50, 30)]
    [InlineData(50, -30)]
    [InlineData(50, 119)]
    [InlineData(50, -119)]
    [InlineData(50, 1)]
    public void Nudge_CoarseLevels_ASubNotchDeltaDoesNotJumpALevel(int current, int delta)
    {
        Assert.Equal(current, BrightnessScale.Nudge(current, delta, Coarse));
    }

    [Theory]
    [InlineData(50, 10, 50)]
    [InlineData(50, 24, 51)]
    [InlineData(50, -24, 49)]
    [InlineData(50, 90, 54)]
    public void Nudge_FullRange_SubNotchDeltasStayProportional(int current, int delta, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Nudge(current, delta, Full));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.NaN, 0)]
    public void Snap_NonFiniteInput_ClampsToTheEnds(double percent, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Snap(percent, Coarse));
    }

    [Theory]
    [InlineData(42.4, 42)]
    [InlineData(250.0, 100)]
    [InlineData(double.NaN, 0)]
    public void Snap_NoLevels_ReturnsTheClampedPercent(double percent, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Snap(percent, []));
    }

    [Theory]
    [InlineData(52.0, 75)]
    [InlineData(48.0, 25)]
    [InlineData(60.0, 75)]
    [InlineData(40.0, 25)]
    [InlineData(50.0, 50)]
    [InlineData(80.0, 75)]
    [InlineData(10.0, 0)]
    public void Resolve_CoarseLevels_ASwallowedRequestStepsOneLevel(double requested, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Resolve(requested, 50, Coarse));
    }

    [Theory]
    [InlineData(102.0, 100)]
    [InlineData(100.0, 100)]
    public void Resolve_AtTheTop_StaysPut(double requested, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Resolve(requested, 100, Coarse));
    }

    [Theory]
    [InlineData(30.4, 30)]
    [InlineData(29.6, 30)]
    [InlineData(30.9, 31)]
    [InlineData(32.0, 32)]
    public void Resolve_FullRange_SubPercentJitterDoesNotStep(double requested, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Resolve(requested, 30, Full));
    }

    [Fact]
    public void Nudge_ZeroDelta_KeepsTheLevel()
    {
        Assert.Equal(50, BrightnessScale.Nudge(50, 0, Coarse));
    }
}
