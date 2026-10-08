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
    [InlineData(50, 1, 75)]
    [InlineData(50, -1, 25)]
    [InlineData(0, 1, 25)]
    [InlineData(100, -1, 75)]
    [InlineData(100, 1, 100)]
    [InlineData(0, -1, 0)]
    [InlineData(60, 1, 75)]
    [InlineData(60, -1, 50)]
    [InlineData(50, 0, 50)]
    public void Step_CoarseLevels_MovesToTheNeighbouringLevel(int current, int direction, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Step(current, direction, Coarse));
    }

    [Theory]
    [InlineData(30, 1, 31)]
    [InlineData(30, -1, 29)]
    [InlineData(30, 5, 31)]
    [InlineData(30, -5, 29)]
    public void Step_FullRange_MovesOnePercentWhateverTheMagnitude(int current, int direction, int expected)
    {
        Assert.Equal(expected, BrightnessScale.Step(current, direction, Full));
    }

    [Fact]
    public void Step_NoLevels_KeepsTheLevel()
    {
        Assert.Equal(40, BrightnessScale.Step(40, 1, []));
    }
}
