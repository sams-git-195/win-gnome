using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class CursorSizeTests
{
    [Theory]
    [InlineData(1, 32)]
    [InlineData(2, 48)]
    [InlineData(8, 144)]
    [InlineData(15, 256)]
    [InlineData(0, 32)]
    [InlineData(-4, 32)]
    [InlineData(16, 256)]
    public void ToPixels_Is32Plus16PerStep_Clamped(int size, int expected)
    {
        Assert.Equal(expected, CursorSizeScale.ToPixels(size));
    }

    [Theory]
    [InlineData(32, 1)]
    [InlineData(48, 2)]
    [InlineData(256, 15)]
    [InlineData(39, 1)]
    [InlineData(40, 2)]
    [InlineData(0, 1)]
    [InlineData(-32, 1)]
    [InlineData(512, 15)]
    public void FromPixels_RoundsToTheNearestStep_Clamped(int pixels, int expected)
    {
        Assert.Equal(expected, CursorSizeScale.FromPixels(pixels));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0, null, true)]
    [InlineData(0, 0, true)]
    [InlineData(0, 2, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, false)]
    [InlineData(3, 2, false)]
    [InlineData(null, 1, false)]
    public void Availability_OnlyWhitePointerWithWindowsScheme(int? cursorType, int? schemeSource, bool expected)
    {
        Assert.Equal(expected, CursorSizeAvailability.For(cursorType, schemeSource));
    }
}
