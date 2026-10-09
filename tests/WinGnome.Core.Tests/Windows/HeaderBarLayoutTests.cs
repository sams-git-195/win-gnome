using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class HeaderBarLayoutTests
{
    // Default settings: diameter 14, spacing 8, right side, minimise-maximise-close. The settings header bar is 46 DIPs.
    private static WindowButtonSettings Settings() => new();

    [Fact]
    public void ComputeForHeaderBar_Right_GroupFillsTheLast138DipsWithCirclesCentredVertically()
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, Settings());

        Assert.NotNull(layout);
        Assert.Equal(new PixelRect(802, 0, 940, 46), layout.Bounds);
        Assert.Equal(14, layout.Diameter);
        Assert.Equal(
            [
                new CaptionButtonSlot(CaptionButtonKind.Minimize, 75, 23),
                new CaptionButtonSlot(CaptionButtonKind.Maximize, 97, 23),
                new CaptionButtonSlot(CaptionButtonKind.Close, 119, 23),
            ],
            layout.Buttons);
    }

    [Fact]
    public void ComputeForHeaderBar_RightMacOrder_IsCloseMinimiseMaximise()
    {
        var settings = Settings();
        settings.Order = ButtonOrder.CloseMinimizeMaximize;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, settings)!;

        Assert.Equal(
            [
                new CaptionButtonSlot(CaptionButtonKind.Close, 75, 23),
                new CaptionButtonSlot(CaptionButtonKind.Minimize, 97, 23),
                new CaptionButtonSlot(CaptionButtonKind.Maximize, 119, 23),
            ],
            layout.Buttons);
    }

    [Fact]
    public void ComputeForHeaderBar_Left_GroupStartsEightDipsIn()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        settings.Order = ButtonOrder.CloseMinimizeMaximize;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, settings)!;

        Assert.Equal(new PixelRect(8, 0, 90, 46), layout.Bounds);
        Assert.Equal(
            [
                new CaptionButtonSlot(CaptionButtonKind.Close, 19, 23),
                new CaptionButtonSlot(CaptionButtonKind.Minimize, 41, 23),
                new CaptionButtonSlot(CaptionButtonKind.Maximize, 63, 23),
            ],
            layout.Buttons);
    }

    [Fact]
    public void ComputeForHeaderBar_FractionalWidth_RoundsToWholeDips()
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(940.4, 46, Settings())!;

        Assert.Equal(new PixelRect(802, 0, 940, 46), layout.Bounds);
    }

    [Fact]
    public void ComputeForHeaderBar_NarrowBar_ShrinksSpacingFirst()
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(70, 46, Settings())!;

        Assert.Equal(new PixelRect(0, 0, 70, 46), layout.Bounds);
        Assert.Equal(14, layout.Diameter);
        Assert.Equal([19.0, 35.0, 51.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Fact]
    public void ComputeForHeaderBar_VeryNarrowBar_ShrinksTheDiameter()
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(60, 46, Settings())!;

        Assert.Equal(12, layout.Diameter);
        Assert.Equal([18.0, 30.0, 42.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Fact]
    public void ComputeForHeaderBar_DiameterBelowMinimum_IsClampedToEight()
    {
        var settings = Settings();
        settings.Diameter = 4;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, settings)!;

        Assert.Equal(8, layout.Diameter);
        Assert.Equal([90.0, 106.0, 122.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Fact]
    public void ComputeForHeaderBar_NaNSpacing_CountsAsZero()
    {
        var settings = Settings();
        settings.Spacing = double.NaN;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, settings)!;

        Assert.Equal([91.0, 105.0, 119.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Theory]
    [InlineData(ButtonOrder.MinimizeMaximizeClose)]
    [InlineData(ButtonOrder.CloseMinimizeMaximize)]
    public void ComputeForHeaderBar_CloseOnlyOnTheRight_PutsTheCloseCircleAtTheEnd(ButtonOrder order)
    {
        var settings = Settings();
        settings.Order = order;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(460, 46, settings, closeOnly: true)!;

        Assert.Equal(new PixelRect(322, 0, 460, 46), layout.Bounds);
        Assert.Equal([new CaptionButtonSlot(CaptionButtonKind.Close, 119, 23)], layout.Buttons);
    }

    [Fact]
    public void ComputeForHeaderBar_CloseOnlyOnTheLeft_NarrowsTheGroupToOneCircle()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(460, 46, settings, closeOnly: true)!;

        Assert.Equal(new PixelRect(8, 0, 46, 46), layout.Bounds);
        Assert.Equal([new CaptionButtonSlot(CaptionButtonKind.Close, 19, 23)], layout.Buttons);
    }

    [Fact]
    public void ComputeForHeaderBar_CloseOnlyInATinyBar_ShrinksTheCircleToTheMinimum()
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(30, 46, Settings(), closeOnly: true)!;

        Assert.Equal(8, layout.Diameter);
        Assert.Equal([new CaptionButtonSlot(CaptionButtonKind.Close, 14, 23)], layout.Buttons);
    }

    [Theory]
    [InlineData(0, 46)]
    [InlineData(-10, 46)]
    [InlineData(940, 0)]
    [InlineData(double.NaN, 46)]
    [InlineData(940, double.PositiveInfinity)]
    [InlineData(0.4, 46)]
    public void ComputeForHeaderBar_NoArea_ReturnsNull(double width, double height)
    {
        Assert.Null(CaptionButtonLayout.ComputeForHeaderBar(width, height, Settings()));
    }

    // The settings preview's 400 x 36 bar: the geometry it built by hand before ComputeForHeaderBar existed
    // (a 138 DIP native block at the right of a 400 DIP window at 100 %).
    [Fact]
    public void ComputeForHeaderBar_PreviewBarOnTheRight_KeepsThePreviewGeometry()
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(400, 36, Settings())!;

        Assert.Equal(new PixelRect(262, 0, 400, 36), layout.Bounds);
        Assert.Equal(14, layout.Diameter);
        Assert.Equal(
            [
                new CaptionButtonSlot(CaptionButtonKind.Minimize, 75, 18),
                new CaptionButtonSlot(CaptionButtonKind.Maximize, 97, 18),
                new CaptionButtonSlot(CaptionButtonKind.Close, 119, 18),
            ],
            layout.Buttons);
    }

    [Fact]
    public void ComputeForHeaderBar_PreviewBarOnTheLeft_KeepsThePreviewGeometry()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        settings.Order = ButtonOrder.CloseMinimizeMaximize;

        var layout = CaptionButtonLayout.ComputeForHeaderBar(400, 36, settings)!;

        Assert.Equal(new PixelRect(8, 0, 90, 36), layout.Bounds);
        Assert.Equal(
            [
                new CaptionButtonSlot(CaptionButtonKind.Close, 19, 18),
                new CaptionButtonSlot(CaptionButtonKind.Minimize, 41, 18),
                new CaptionButtonSlot(CaptionButtonKind.Maximize, 63, 18),
            ],
            layout.Buttons);
    }

    // Default layout in a 940 x 46 bar: the maximise target spans bar x 888-910 (centre 899, pitch 22).
    private static CaptionOverlayLayout Bar() => CaptionButtonLayout.ComputeForHeaderBar(940, 46, Settings())!;

    [Theory]
    [InlineData(1348, 30, CaptionButtonKind.Maximize)]   // 898.7 DIPs at 150 %
    [InlineData(1332, 30, CaptionButtonKind.Maximize)]   // exactly the target's left edge (888)
    [InlineData(1331, 30, CaptionButtonKind.Minimize)]   // 887.3
    [InlineData(1365, 30, CaptionButtonKind.Close)]      // 910
    [InlineData(1348, 0, CaptionButtonKind.Maximize)]    // top row of the bar
    [InlineData(1348, 68, CaptionButtonKind.Maximize)]   // 45.3, last row
    public void FindInHeaderBar_At150Percent_MapsPixelsToButtons(int x, int y, CaptionButtonKind expected)
    {
        Assert.Equal(expected, CaptionButtonHitTest.FindInHeaderBar(Bar(), x, y, 1.5, 0, 0));
    }

    [Theory]
    [InlineData(1348, 69)]   // 46: below the bar
    [InlineData(1348, -1)]   // above the bar
    [InlineData(1200, 30)]   // 800: left of the group
    [InlineData(1410, 30)]   // 940: past the bar's right edge
    public void FindInHeaderBar_OutsideTheGroup_ReturnsNull(int x, int y)
    {
        Assert.Null(CaptionButtonHitTest.FindInHeaderBar(Bar(), x, y, 1.5, 0, 0));
    }

    [Fact]
    public void FindInHeaderBar_MaximisedMargin_OffsetsTheBar()
    {
        // Maximised, the bar starts 8 DIPs in from the client edge on both axes.
        Assert.Equal(CaptionButtonKind.Maximize, CaptionButtonHitTest.FindInHeaderBar(Bar(), 1361, 13, 1.5, 8, 8));
        Assert.Null(CaptionButtonHitTest.FindInHeaderBar(Bar(), 1361, 11, 1.5, 8, 8));
        Assert.Equal(CaptionButtonKind.Minimize, CaptionButtonHitTest.FindInHeaderBar(Bar(), 1343, 30, 1.5, 8, 8));
    }

    [Theory]
    [InlineData(899, 23, 1.0, CaptionButtonKind.Maximize)]    // 100 %: pixels are DIPs
    [InlineData(887, 23, 1.0, CaptionButtonKind.Minimize)]
    [InlineData(1124, 29, 1.25, CaptionButtonKind.Maximize)]  // 899.2
    [InlineData(1109, 29, 1.25, CaptionButtonKind.Minimize)]  // 887.2
    public void FindInHeaderBar_OtherScales_MapPixelsToButtons(int x, int y, double scale, CaptionButtonKind expected)
    {
        Assert.Equal(expected, CaptionButtonHitTest.FindInHeaderBar(Bar(), x, y, scale, 0, 0));
    }

    [Theory]
    [InlineData(70, CaptionButtonKind.Maximize)]    // centres at bar x 27, 49, 71 (group starts 8 DIPs in)
    [InlineData(60, CaptionButtonKind.Maximize)]    // the maximise target's left edge
    [InlineData(59, CaptionButtonKind.Minimize)]
    [InlineData(16, CaptionButtonKind.Close)]
    public void FindInHeaderBar_LeftSide_MapsDipsToButtons(int x, CaptionButtonKind expected)
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        settings.Order = ButtonOrder.CloseMinimizeMaximize;
        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, settings)!;

        Assert.Equal(expected, CaptionButtonHitTest.FindInHeaderBar(layout, x, 23, 1.0, 0, 0));
    }

    [Theory]
    [InlineData(4)]     // left padding, outside the group
    [InlineData(95)]    // right of the group
    public void FindInHeaderBar_LeftSidePadding_HitsNothing(int x)
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, settings)!;

        Assert.Null(CaptionButtonHitTest.FindInHeaderBar(layout, x, 23, 1.0, 0, 0));
    }

    [Theory]
    [InlineData(921, CaptionButtonKind.Close)]   // the close circle's centre (bar x 802 + 119)
    [InlineData(914, CaptionButtonKind.Close)]   // a lone circle's target is its diameter
    [InlineData(913, null)]
    [InlineData(928, null)]
    public void FindInHeaderBar_CloseOnly_HasOneTarget(int x, CaptionButtonKind? expected)
    {
        var layout = CaptionButtonLayout.ComputeForHeaderBar(940, 46, Settings(), closeOnly: true)!;

        Assert.Equal(expected, CaptionButtonHitTest.FindInHeaderBar(layout, x, 23, 1.0, 0, 0));
    }

    [Theory]
    [InlineData(899, 20, 0)]
    [InlineData(-1348, -30, -1.5)]   // would land on maximise if the sign were ignored
    [InlineData(899, 20, double.NaN)]
    public void FindInHeaderBar_InvalidScale_ReturnsNull(int x, int y, double scale)
    {
        Assert.Null(CaptionButtonHitTest.FindInHeaderBar(Bar(), x, y, scale, 0, 0));
    }
}
