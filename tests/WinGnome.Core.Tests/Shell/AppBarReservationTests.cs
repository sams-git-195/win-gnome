using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class AppBarReservationTests
{
    // The user's primary (2560x1600 physical) with a 40 px top bar, and the 3440x1440 monitor above it.
    private static readonly PixelRect PrimaryBar = new(0, 0, 2560, 40);
    private static readonly PixelRect UpperBar = new(-447, -1440, 2993, -1408);

    [Fact]
    public void Top_WorkAreaBelowTheStrip_IsReserved()
    {
        Assert.True(AppBarReservation.IsReserved(AppBarEdge.Top, PrimaryBar, new PixelRect(0, 40, 2560, 1600)));
    }

    [Fact]
    public void Top_WorkAreaResetToTheWholeMonitor_IsNotReserved()
    {
        // What Explorer left after the upper monitor was unplugged.
        Assert.False(AppBarReservation.IsReserved(AppBarEdge.Top, PrimaryBar, new PixelRect(0, 0, 2560, 1600)));
    }

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(80, true)] // Stacked below another top bar: the work area ends beyond both.
    public void Top_Boundary(int workTop, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Top, PrimaryBar, new PixelRect(0, workTop, 2560, 1600)));
    }

    [Theory]
    [InlineData(-1408, true)]
    [InlineData(-1409, false)]
    [InlineData(-1440, false)]
    public void Top_NegativeCoordinates(int workTop, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Top, UpperBar, new PixelRect(-447, workTop, 2993, 0)));
    }

    [Theory]
    [InlineData(1504, true)]
    [InlineData(1505, false)]
    [InlineData(1600, false)]
    public void Bottom(int workBottom, bool expected)
    {
        var dock = new PixelRect(0, 1504, 2560, 1600);
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Bottom, dock, new PixelRect(0, 40, 2560, workBottom)));
    }

    [Theory]
    [InlineData(96, true)]
    [InlineData(95, false)]
    public void Left(int workLeft, bool expected)
    {
        var dock = new PixelRect(0, 40, 96, 1600);
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Left, dock, new PixelRect(workLeft, 40, 2560, 1600)));
    }

    [Theory]
    [InlineData(2464, true)]
    [InlineData(2465, false)]
    public void Right(int workRight, bool expected)
    {
        var dock = new PixelRect(2464, 40, 2560, 1600);
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Right, dock, new PixelRect(0, 40, workRight, 1600)));
    }

    [Fact]
    public void EmptyStrip_IsAlwaysReserved()
    {
        // Zero height, but its bottom edge lies below the work area's top.
        Assert.True(AppBarReservation.IsReserved(AppBarEdge.Top, new PixelRect(0, 40, 2560, 40), new PixelRect(0, 0, 2560, 1600)));
    }
}
