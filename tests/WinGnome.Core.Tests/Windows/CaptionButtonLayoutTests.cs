using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class CaptionButtonLayoutTests
{
    // Default settings: diameter 14, spacing 8, right side, minimise-maximise-close.
    private static readonly PixelRect Window = PixelRect.FromSize(100, 50, 1138, 600);
    private static readonly PixelRect Native = new(1100, 50, 1238, 82); // 138 x 32 at 100 %

    private static WindowButtonSettings Settings() => new();

    [Fact]
    public void ReturnsNull_ForEmptyNativeRect()
    {
        Assert.Null(CaptionButtonLayout.Compute(default, Window, 1, Settings()));
        Assert.Null(CaptionButtonLayout.Compute(new PixelRect(10, 10, 10, 40), Window, 1, Settings()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ReturnsNull_ForInvalidDpiScale(double dpi)
    {
        Assert.Null(CaptionButtonLayout.Compute(Native, Window, dpi, Settings()));
    }

    [Fact]
    public void Right_BoundsAreTheNativeButtons()
    {
        var layout = CaptionButtonLayout.Compute(Native, Window, 1, Settings());

        Assert.NotNull(layout);
        Assert.Equal(Native, layout.Bounds);
        Assert.Equal(14, layout.Diameter);
    }

    [Fact]
    public void Right_DefaultOrder_IsMinimiseMaximiseClose_RightAlignedWithTwelveDipPadding()
    {
        var layout = CaptionButtonLayout.Compute(Native, Window, 1, Settings())!;

        Assert.Equal(
            [CaptionButtonKind.Minimize, CaptionButtonKind.Maximize, CaptionButtonKind.Close],
            layout.Buttons.Select(b => b.Kind));
        Assert.Equal([75.0, 97.0, 119.0], layout.Buttons.Select(b => b.CenterX));
        Assert.All(layout.Buttons, b => Assert.Equal(16, b.CenterY));

        var last = layout.Buttons[^1];
        Assert.Equal(138 - 12, last.CenterX + (layout.Diameter / 2));
    }

    [Fact]
    public void Right_MacOrder_IsCloseMinimiseMaximise()
    {
        var settings = Settings();
        settings.Order = ButtonOrder.CloseMinimizeMaximize;

        var layout = CaptionButtonLayout.Compute(Native, Window, 1, settings)!;

        Assert.Equal(
            [CaptionButtonKind.Close, CaptionButtonKind.Minimize, CaptionButtonKind.Maximize],
            layout.Buttons.Select(b => b.Kind));
        Assert.Equal([75.0, 97.0, 119.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Fact]
    public void Right_HighDpi_ReportsDipsNotPixels()
    {
        var native = new PixelRect(1000, 0, 1207, 48); // 207 x 48 px at 150 % == 138 x 32 DIP

        var layout = CaptionButtonLayout.Compute(native, Window, 1.5, Settings())!;

        Assert.Equal(native, layout.Bounds);
        Assert.Equal([75.0, 97.0, 119.0], layout.Buttons.Select(b => Math.Round(b.CenterX, 6)));
        Assert.All(layout.Buttons, b => Assert.Equal(16, b.CenterY, 6));
    }

    [Fact]
    public void Left_BoundsStartEightDipInsideTheWindow_AndKeepTheNativeVerticalSpan()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;

        var layout = CaptionButtonLayout.Compute(Native, Window, 1, settings)!;

        // width = ceil((12 * 2 + 3 * 14 + 2 * 8) * 1) = 82
        Assert.Equal(new PixelRect(108, 50, 190, 82), layout.Bounds);
    }

    [Fact]
    public void Left_CirclesAreLeftAlignedWithTwelveDipPadding()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;

        var layout = CaptionButtonLayout.Compute(Native, Window, 1, settings)!;

        Assert.Equal([19.0, 41.0, 63.0], layout.Buttons.Select(b => b.CenterX));
        Assert.All(layout.Buttons, b => Assert.Equal(16, b.CenterY));
        Assert.Equal(12, layout.Buttons[0].CenterX - (layout.Diameter / 2));
    }

    [Fact]
    public void Left_MacOrder()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        settings.Order = ButtonOrder.CloseMinimizeMaximize;

        var layout = CaptionButtonLayout.Compute(Native, Window, 1, settings)!;

        Assert.Equal(
            [CaptionButtonKind.Close, CaptionButtonKind.Minimize, CaptionButtonKind.Maximize],
            layout.Buttons.Select(b => b.Kind));
    }

    [Fact]
    public void Left_At125Percent_RoundsInsetAndCeilsWidth()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        var native = new PixelRect(1100, 50, 1273, 90); // 40 px tall

        var layout = CaptionButtonLayout.Compute(native, Window, 1.25, settings)!;

        // inset = round(8 * 1.25) = 10, width = ceil(82 * 1.25) = ceil(102.5) = 103
        Assert.Equal(new PixelRect(110, 50, 213, 90), layout.Bounds);
        Assert.Equal([19.0, 41.0, 63.0], layout.Buttons.Select(b => Math.Round(b.CenterX, 6)));
        Assert.All(layout.Buttons, b => Assert.Equal(16, b.CenterY, 6));
    }

    [Fact]
    public void Left_BiggerCirclesWidenTheOverlay()
    {
        var settings = Settings();
        settings.Side = ButtonSide.Left;
        settings.Diameter = 20;
        settings.Spacing = 10;

        var layout = CaptionButtonLayout.Compute(Native, Window, 1, settings)!;

        Assert.Equal(24 + 60 + 20, layout.Bounds.Width);
        Assert.Equal(20, layout.Diameter);
        Assert.Equal([22.0, 52.0, 82.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Fact]
    public void ShrinksSpacingFirst_WhenGroupDoesNotFit()
    {
        var native = new PixelRect(0, 0, 70, 32); // available = 70 - 24 = 46, natural = 58

        var layout = CaptionButtonLayout.Compute(native, Window, 1, Settings())!;

        Assert.Equal(14, layout.Diameter);
        // spacing shrinks to (46 - 42) / 2 = 2: centres 70 - 12 - 46 + 7 = 19, 35, 51
        Assert.Equal([19.0, 35.0, 51.0], layout.Buttons.Select(b => b.CenterX));
    }

    [Fact]
    public void ShrinksDiameterNext_WhenEvenZeroSpacingDoesNotFit()
    {
        var native = new PixelRect(0, 0, 60, 32); // available = 36

        var layout = CaptionButtonLayout.Compute(native, Window, 1, Settings())!;

        Assert.Equal(12, layout.Diameter);
        Assert.Equal([18.0, 30.0, 42.0], layout.Buttons.Select(b => b.CenterX));
        Assert.Equal(60 - 12, layout.Buttons[^1].CenterX + (layout.Diameter / 2));
    }

    [Fact]
    public void DiameterNeverDropsBelowEight()
    {
        var native = new PixelRect(0, 0, 40, 32); // available = 16

        var layout = CaptionButtonLayout.Compute(native, Window, 1, Settings())!;

        Assert.Equal(8, layout.Diameter);
    }

    [Fact]
    public void UnnormalisedSmallDiameterIsClampedToEight()
    {
        var settings = Settings();
        settings.Diameter = 2;

        var layout = CaptionButtonLayout.Compute(Native, Window, 1, settings)!;

        Assert.Equal(8, layout.Diameter);
    }

    [Fact]
    public void Circles_NeverOverlap_WhenTheyFit()
    {
        var layout = CaptionButtonLayout.Compute(Native, Window, 1, Settings())!;

        for (var i = 1; i < layout.Buttons.Count; i++)
        {
            Assert.True(layout.Buttons[i].CenterX - layout.Buttons[i - 1].CenterX >= layout.Diameter);
        }
    }
}
