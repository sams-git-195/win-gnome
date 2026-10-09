using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class CaretWidthTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(7, 7)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(21, 20)]
    public void Clamp_KeepsOneToTwenty(int width, int expected)
    {
        Assert.Equal(expected, CaretWidth.Clamp(width));
    }
}
