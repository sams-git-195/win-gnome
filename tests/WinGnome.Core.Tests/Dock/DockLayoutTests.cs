using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Dock;

public class DockLayoutTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    // At 100 %: icon 48, padding 6 -> cell 60; thickness 60 + 2*4 = 68; length 5 * 60 + 2*8 = 316; gap 8.
    [Fact]
    public void Bottom_Floating()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Bottom, 5, 48, 1, extendToEdges: false);

        Assert.Equal(new PixelRect(802, 1004, 1118, 1072), g.Bounds);
        Assert.Equal(new PixelRect(802, 1080, 1118, 1148), g.HiddenBounds);
        Assert.Equal(new PixelRect(802, 1078, 1118, 1080), g.TriggerZone);
    }

    [Fact]
    public void Bottom_ExtendToEdges()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Bottom, 5, 48, 1, extendToEdges: true);

        Assert.Equal(new PixelRect(0, 1012, 1920, 1080), g.Bounds);
        Assert.Equal(new PixelRect(0, 1080, 1920, 1148), g.HiddenBounds);
        Assert.Equal(new PixelRect(0, 1078, 1920, 1080), g.TriggerZone);
    }

    [Fact]
    public void Left_Floating()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Left, 5, 48, 1, extendToEdges: false);

        Assert.Equal(new PixelRect(8, 382, 76, 698), g.Bounds);
        Assert.Equal(new PixelRect(-68, 382, 0, 698), g.HiddenBounds);
        Assert.Equal(new PixelRect(0, 382, 2, 698), g.TriggerZone);
    }

    [Fact]
    public void Right_Floating()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Right, 5, 48, 1, extendToEdges: false);

        Assert.Equal(new PixelRect(1844, 382, 1912, 698), g.Bounds);
        Assert.Equal(new PixelRect(1920, 382, 1988, 698), g.HiddenBounds);
        Assert.Equal(new PixelRect(1918, 382, 1920, 698), g.TriggerZone);
    }

    [Fact]
    public void Left_ExtendToEdges_SpansTheFullHeight_WithNoGap()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Left, 5, 48, 1, extendToEdges: true);

        Assert.Equal(new PixelRect(0, 0, 68, 1080), g.Bounds);
        Assert.Equal(new PixelRect(0, 0, 2, 1080), g.TriggerZone);
    }

    [Fact]
    public void HighDpi_ScalesPaddingMarginsAndGap()
    {
        // icon 72 px at 150 %: padding 9, cell 90, thickness 90 + 2*6 = 102, inset 12, length 5*90 + 24 = 474.
        var g = DockLayout.Compute(Monitor, DockPosition.Bottom, 5, 72, 1.5, extendToEdges: false);

        Assert.Equal(474, g.Bounds.Width);
        Assert.Equal(102, g.Bounds.Height);
        Assert.Equal(1080 - 12, g.Bounds.Bottom);
        Assert.Equal((1920 - 474) / 2, g.Bounds.Left);
    }

    [Fact]
    public void LengthGrowsWithItemCount()
    {
        var few = DockLayout.Compute(Monitor, DockPosition.Bottom, 3, 48, 1, false);
        var many = DockLayout.Compute(Monitor, DockPosition.Bottom, 10, 48, 1, false);

        Assert.Equal(60 * 7, many.Bounds.Width - few.Bounds.Width);
    }

    [Fact]
    public void ZeroItems_ReservesOneCell()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Bottom, 0, 48, 1, false);

        Assert.Equal(60 + 16, g.Bounds.Width);
    }

    [Fact]
    public void ManyItems_AreClampedToMonitorLengthMinusMargins()
    {
        var g = DockLayout.Compute(Monitor, DockPosition.Bottom, 100, 48, 1, false);

        Assert.Equal(1920 - 16, g.Bounds.Width);
        Assert.Equal(8, g.Bounds.Left);
        Assert.Equal(1912, g.Bounds.Right);
    }

    [Fact]
    public void SecondMonitor_OffsetsEverything()
    {
        var second = new PixelRect(1920, 100, 3840, 1180);
        var first = DockLayout.Compute(Monitor, DockPosition.Bottom, 5, 48, 1, false);

        var g = DockLayout.Compute(second, DockPosition.Bottom, 5, 48, 1, false);

        Assert.Equal(first.Bounds.Offset(1920, 100), g.Bounds);
        Assert.Equal(first.HiddenBounds.Offset(1920, 100), g.HiddenBounds);
        Assert.Equal(first.TriggerZone.Offset(1920, 100), g.TriggerZone);
    }

    [Fact]
    public void NegativeCoordinateMonitor_WorksForLeftEdge()
    {
        var left = new PixelRect(-1920, 0, 0, 1080);

        var g = DockLayout.Compute(left, DockPosition.Left, 5, 48, 1, false);

        Assert.Equal(-1920 + 8, g.Bounds.Left);
        Assert.Equal(-1920, g.TriggerZone.Left);
        Assert.Equal(-1920, g.HiddenBounds.Right);
    }

    [Theory]
    [InlineData(DockPosition.Bottom)]
    [InlineData(DockPosition.Left)]
    [InlineData(DockPosition.Right)]
    public void HiddenBounds_AreFullyOffTheMonitor_AndBoundsAreInside(DockPosition position)
    {
        var g = DockLayout.Compute(Monitor, position, 6, 48, 1.25, false);

        Assert.False(g.HiddenBounds.Intersects(Monitor));
        Assert.Equal(g.Bounds.Width, g.HiddenBounds.Width);
        Assert.Equal(g.Bounds.Height, g.HiddenBounds.Height);
        Assert.Equal(g.Bounds, Monitor.Intersect(g.Bounds));
    }

    [Theory]
    [InlineData(DockPosition.Bottom)]
    [InlineData(DockPosition.Left)]
    [InlineData(DockPosition.Right)]
    public void TriggerZone_IsAThinStripOnTheMonitorEdge(DockPosition position)
    {
        var g = DockLayout.Compute(Monitor, position, 6, 48, 1, false);

        Assert.True(g.TriggerZone.Intersects(Monitor));
        var thin = position == DockPosition.Bottom ? g.TriggerZone.Height : g.TriggerZone.Width;
        Assert.Equal(2, thin);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidDpi_IsTreatedAsOne(double dpi)
    {
        var expected = DockLayout.Compute(Monitor, DockPosition.Bottom, 5, 48, 1, false);
        Assert.Equal(expected, DockLayout.Compute(Monitor, DockPosition.Bottom, 5, 48, dpi, false));
    }

    [Fact]
    public void Magnification_FullAtZeroDistance()
    {
        Assert.Equal(1.6, DockLayout.MagnificationScale(0, 48, 1.6), 9);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(121)]
    [InlineData(5000)]
    public void Magnification_IsOneBeyondTheRange(double distance)
    {
        // range = 2.5 * 48 = 120
        Assert.Equal(1, DockLayout.MagnificationScale(distance, 48, 2));
    }

    [Fact]
    public void Magnification_HalfwayFollowsCosineSquared()
    {
        // d = range / 2 -> cos^2(pi/4) = 0.5
        Assert.Equal(1 + (0.5 * 1.0), DockLayout.MagnificationScale(60, 48, 2), 9);
    }

    [Fact]
    public void Magnification_IsSymmetricAndMonotonic()
    {
        Assert.Equal(DockLayout.MagnificationScale(30, 48, 1.8), DockLayout.MagnificationScale(-30, 48, 1.8), 12);

        var previous = double.MaxValue;
        for (var d = 0; d <= 130; d += 10)
        {
            var scale = DockLayout.MagnificationScale(d, 48, 1.8);
            Assert.InRange(scale, 1, 1.8);
            Assert.True(scale <= previous);
            previous = scale;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    public void Magnification_DisabledWhenMaxIsOneOrLess(double max)
    {
        Assert.Equal(1, DockLayout.MagnificationScale(0, 48, max));
    }

    [Fact]
    public void Magnification_ZeroIconSize_IsOne()
    {
        Assert.Equal(1, DockLayout.MagnificationScale(0, 0, 2));
    }
}
