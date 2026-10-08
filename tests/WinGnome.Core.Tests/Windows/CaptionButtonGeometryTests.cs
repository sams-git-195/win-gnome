using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class CaptionButtonGeometryTests
{
    [Fact]
    public void ToScreen_OffsetsByWindowOrigin()
    {
        // Values measured on Windows 11 at 125 %: a normal WinForms window.
        var window = new PixelRect(200, 200, 1000, 700);
        var frame = new PixelRect(208, 200, 992, 692);
        var relative = new PixelRect(607, 0, 792, 37);

        var screen = CaptionButtonGeometry.ToScreen(relative, window, frame);

        Assert.Equal(new PixelRect(807, 200, 992, 237), screen);
    }

    [Fact]
    public void ToScreen_ClipsTheOffscreenStripOfMaximisedWindows()
    {
        // Values measured on Windows 11 at 125 %: the same window maximised on a 2560 x 1600 monitor.
        var window = new PixelRect(-9, -9, 2569, 1549);
        var frame = new PixelRect(0, 0, 2560, 1540);
        var relative = new PixelRect(2383, 0, 2568, 37);

        var screen = CaptionButtonGeometry.ToScreen(relative, window, frame);

        Assert.Equal(new PixelRect(2374, 0, 2559, 28), screen);
    }

    [Fact]
    public void ToScreen_WithoutFrame_DoesNotClip()
    {
        var screen = CaptionButtonGeometry.ToScreen(new PixelRect(10, 0, 50, 30), new PixelRect(-5, -5, 100, 100), default);

        Assert.Equal(new PixelRect(5, -5, 45, 25), screen);
    }

    [Fact]
    public void ToScreen_EmptyButtons_IsEmpty()
    {
        // Chrome reports a zero-width rectangle.
        var screen = CaptionButtonGeometry.ToScreen(new PixelRect(2092, 0, 2092, 37), new PixelRect(0, 0, 2100, 1200), new PixelRect(8, 0, 2092, 1192));

        Assert.True(screen.IsEmpty);
    }

    [Fact]
    public void ToScreen_ButtonsOutsideFrame_IsEmpty()
    {
        var screen = CaptionButtonGeometry.ToScreen(new PixelRect(0, 0, 10, 10), new PixelRect(0, 0, 100, 100), new PixelRect(50, 50, 100, 100));

        Assert.True(screen.IsEmpty);
    }

    [Theory]
    [InlineData(238, false)] // classic caption: client starts below the buttons
    [InlineData(237, false)]
    [InlineData(236, true)]
    [InlineData(200, true)]  // custom title bar (Notepad, Explorer): client starts at the frame top
    [InlineData(191, true)]  // maximised custom title bar: client starts above the monitor
    public void ClientCoversCaption_ComparesClientTopWithButtonsBottom(int clientTop, bool expected)
    {
        var buttons = new PixelRect(807, 200, 992, 237);

        Assert.Equal(expected, CaptionButtonGeometry.ClientCoversCaption(clientTop, buttons));
    }

    [Theory]
    [InlineData(1.0, 797)]
    [InlineData(1.25, 796)]
    [InlineData(2.0, 794)]
    [InlineData(0.1, 799)]
    [InlineData(double.NaN, 797)]
    public void TitleBarSamplePoint_IsJustLeftOfTheButtonsAndCentred(double scale, int expectedX)
    {
        var (x, y) = CaptionButtonGeometry.TitleBarSamplePoint(new PixelRect(800, 200, 990, 240), scale);

        Assert.Equal(expectedX, x);
        Assert.Equal(220, y);
    }

    [Theory]
    [InlineData(1.0, 8)]
    [InlineData(1.25, 10)]
    [InlineData(1.5, 12)]
    [InlineData(1.75, 14)]
    [InlineData(2.0, 16)]
    [InlineData(0.01, 1)]
    [InlineData(0.0, 8)]
    [InlineData(-1.0, 8)]
    [InlineData(double.NaN, 8)]
    [InlineData(double.PositiveInfinity, 8)]
    public void WindowCornerRadiusPixels_ScalesTheEightDipRadius(double scale, int expected)
    {
        Assert.Equal(expected, CaptionButtonGeometry.WindowCornerRadiusPixels(scale));
    }
}
