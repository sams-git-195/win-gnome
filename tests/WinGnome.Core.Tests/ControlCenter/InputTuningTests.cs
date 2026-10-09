using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class InputTuningTests
{
    [Theory]
    [InlineData(0, 250)]
    [InlineData(1, 500)]
    [InlineData(2, 750)]
    [InlineData(3, 1000)]
    [InlineData(-1, 250)]
    [InlineData(9, 1000)]
    public void RepeatDelayMs_MapsTheFourWindowsSteps(int index, int expected)
    {
        Assert.Equal(expected, InputTuning.RepeatDelayMs(index));
    }

    [Theory]
    [InlineData(0, 2.5)]
    [InlineData(31, 30.0)]
    [InlineData(40, 30.0)]
    [InlineData(-3, 2.5)]
    public void RepeatsPerSecond_IsLinearFromAboutTwoAndAHalfToThirty(int speed, double expected)
    {
        Assert.Equal(expected, InputTuning.RepeatsPerSecond(speed), 3);
    }

    [Fact]
    public void RepeatsPerSecond_Midpoint()
    {
        // 2.5 + 15.5 * 27.5 / 31 = 16.25 at the exact middle of the range.
        Assert.Equal(16.25, (InputTuning.RepeatsPerSecond(15) + InputTuning.RepeatsPerSecond(16)) / 2, 3);
    }

    [Theory]
    [InlineData(new[] { 6, 10, 1 }, true)]
    [InlineData(new[] { 6, 10, 2 }, true)]
    [InlineData(new[] { 0, 0, 0 }, false)]
    [InlineData(new[] { 6, 10, 0 }, false)]
    [InlineData(new[] { 1, 2 }, false)]
    public void IsAccelerationOn_ReadsTheThirdValue(int[] mouse, bool expected)
    {
        Assert.Equal(expected, InputTuning.IsAccelerationOn(mouse));
    }

    [Fact]
    public void AccelerationParameters_MatchWindowsEnhancePointerPrecision()
    {
        Assert.Equal([6, 10, 1], InputTuning.AccelerationParameters(true));
        Assert.Equal([0, 0, 0], InputTuning.AccelerationParameters(false));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(25, 20)]
    public void ClampMouseSpeed_KeepsTheWindowsRange(int speed, int expected)
    {
        Assert.Equal(expected, InputTuning.ClampMouseSpeed(speed));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void ClampScrollLines_KeepsAUsableRange(int lines, int expected)
    {
        Assert.Equal(expected, InputTuning.ClampScrollLines(lines));
    }
}
