using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class BarMetricsTests
{
    [Theory]
    [InlineData(1.0, 14.0)]
    [InlineData(1.25, 13.6)]
    [InlineData(1.5, 13.333333333)]
    [InlineData(1.75, 13.714285714)]
    [InlineData(2.0, 13.5)]
    public void SnapToDevice_DefaultFontSize_LandsOnWholeDevicePixels(double scale, double expectedDip)
    {
        Assert.Equal(expectedDip, BarMetrics.SnapToDevice(13.5, scale), precision: 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SnapToDevice_InvalidScale_UsesOne(double scale)
    {
        Assert.Equal(14.0, BarMetrics.SnapToDevice(13.5, scale));
    }

    [Theory]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void SnapToDevice_InvalidLength_IsZero(double dip)
    {
        Assert.Equal(0.0, BarMetrics.SnapToDevice(dip, 1.25));
    }

    [Theory]
    [InlineData(1.0, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.5, 24)]
    [InlineData(1.75, 28)]
    [InlineData(2.0, 32)]
    public void SymbolicIconPx_DefaultFontSize_IsGnomes16PxScaled(double scale, int expected)
    {
        Assert.Equal(expected, BarMetrics.SymbolicIconPx(13.5, scale));
    }

    [Theory]
    [InlineData(10.0, 1.25, 15)]
    [InlineData(20.0, 1.0, 24)]
    [InlineData(20.0, 1.5, 36)]
    public void SymbolicIconPx_OtherFontSizes_KeepTheGnomeRatio(double fontDip, double scale, int expected)
    {
        Assert.Equal(expected, BarMetrics.SymbolicIconPx(fontDip, scale));
    }

    [Theory]
    [InlineData(0.0, 1.25, 20)]
    [InlineData(-13.5, 1.0, 16)]
    [InlineData(double.NaN, 2.0, 32)]
    [InlineData(13.5, 0.0, 16)]
    [InlineData(13.5, double.NaN, 16)]
    public void SymbolicIconPx_InvalidInput_FallsBackToDefaults(double fontDip, double scale, int expected)
    {
        Assert.Equal(expected, BarMetrics.SymbolicIconPx(fontDip, scale));
    }

    [Fact]
    public void SymbolicIconPx_TinyFont_IsAtLeastEightPixels()
    {
        Assert.Equal(8, BarMetrics.SymbolicIconPx(1, 1));
    }

    [Theory]
    [InlineData(0.0, 20, 0.0)]
    [InlineData(2.0, 20, 3.0)]
    [InlineData(5.0, 20, 6.0)]
    [InlineData(16.0, 20, 20.0)]
    [InlineData(8.0, 16, 8.0)]
    [InlineData(3.0, 24, 5.0)]
    [InlineData(1.0, 32, 2.0)]
    [InlineData(7.0, 28, 12.0)]
    [InlineData(-1.0, 16, -1.0)]
    public void SnapIconUnit_RoundsGridUnitsToWholeDevicePixels(double unit, int iconPx, double expectedPx)
    {
        Assert.Equal(expectedPx, BarMetrics.SnapIconUnit(unit, iconPx));
    }

    [Theory]
    [InlineData(8.0, 0)]
    [InlineData(8.0, -16)]
    [InlineData(double.NaN, 16)]
    public void SnapIconUnit_InvalidInput_IsZero(double unit, int iconPx)
    {
        Assert.Equal(0.0, BarMetrics.SnapIconUnit(unit, iconPx));
    }

    [Theory]
    [InlineData(32.0, 1.0, 3)]
    [InlineData(32.0, 1.25, 4)]
    [InlineData(32.0, 1.5, 5)]
    [InlineData(32.0, 1.75, 6)]
    [InlineData(32.0, 2.0, 6)]
    [InlineData(24.0, 1.0, 2)]
    [InlineData(24.0, 1.25, 3)]
    [InlineData(48.0, 1.0, 5)]
    public void PillInsetPx_IsAWholePixelGapAboveAndBelow(double barHeightDip, double scale, int expected)
    {
        Assert.Equal(expected, BarMetrics.PillInsetPx(barHeightDip, scale));
    }

    [Theory]
    [InlineData(32.0, 0.0, 3)]
    [InlineData(double.NaN, 1.0, 2)]
    [InlineData(-10.0, 2.0, 4)]
    public void PillInsetPx_InvalidInput_KeepsTheMinimumGap(double barHeightDip, double scale, int expected)
    {
        Assert.Equal(expected, BarMetrics.PillInsetPx(barHeightDip, scale));
    }
}
