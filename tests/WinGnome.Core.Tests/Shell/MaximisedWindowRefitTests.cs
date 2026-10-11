using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class MaximisedWindowRefitTests
{
    private static readonly PixelRect Full = new(0, 0, 2560, 1600);
    private static readonly PixelRect BelowTopBar = new(0, 40, 2560, 1600);

    [Fact]
    public void Refit_MaximisedOverTheWholeMonitor_MovesTheTopBelowTheBarKeepingTheBorders()
    {
        // Measured on build 26200 at 125 %: a maximised WinForms window overhangs the work area by 10 px.
        Assert.Equal(new PixelRect(-10, 30, 2570, 1610),
            MaximisedWindowRefit.Refit(new PixelRect(-10, -10, 2570, 1610), Full, BelowTopBar));
    }

    [Fact]
    public void Refit_CustomFrameExactlyFillingTheWorkArea_MovesTheTop()
    {
        Assert.Equal(BelowTopBar, MaximisedWindowRefit.Refit(Full, Full, BelowTopBar));
    }

    [Fact]
    public void Refit_AlreadyFittedToTheNewWorkArea_IsNull()
    {
        Assert.Null(MaximisedWindowRefit.Refit(new PixelRect(-10, 30, 2570, 1610), Full, BelowTopBar));
    }

    [Fact]
    public void Refit_WorkAreaUnchanged_IsNull()
    {
        Assert.Null(MaximisedWindowRefit.Refit(new PixelRect(-10, -10, 2570, 1610), Full, Full));
    }

    [Theory]
    [InlineData(1, 1)]     // One pixel inside the old edge: not fitted to it, stays.
    [InlineData(0, 40)]    // Exactly on it.
    [InlineData(-40, 0)]   // At the largest overhang.
    [InlineData(-41, -41)] // One past it: not a maximised fit, stays.
    public void Refit_TopBoundary(int top, int expectedTop)
    {
        var window = new PixelRect(-10, top, 2570, 1610);

        var result = MaximisedWindowRefit.Refit(window, Full, BelowTopBar) ?? window;

        Assert.Equal(expectedTop, result.Top);
    }

    [Fact]
    public void Refit_BottomDock_MovesOnlyTheBottom()
    {
        Assert.Equal(new PixelRect(-10, 30, 2570, 1540),
            MaximisedWindowRefit.Refit(new PixelRect(-10, 30, 2570, 1610), BelowTopBar, new PixelRect(0, 40, 2560, 1530)));
    }

    [Fact]
    public void Refit_LeftAndRightStrips_MoveThoseEdges()
    {
        Assert.Equal(new PixelRect(50, -10, 2510, 1610),
            MaximisedWindowRefit.Refit(new PixelRect(-10, -10, 2570, 1610), Full, new PixelRect(60, 0, 2500, 1600)));
    }

    [Fact]
    public void Refit_MonitorAboveThePrimaryWithNegativeCoordinates()
    {
        Assert.Equal(new PixelRect(-455, -1416, 3001, 8),
            MaximisedWindowRefit.Refit(new PixelRect(-455, -1448, 3001, 8),
                new PixelRect(-447, -1440, 2993, 0), new PixelRect(-447, -1408, 2993, 0)));
    }

    [Fact]
    public void Refit_DoubledBandCorrected_MovesTheTopBackUp()
    {
        // KI-114: a window maximised onto 0,80 goes back to the strip's edge at 40.
        Assert.Equal(new PixelRect(-10, 30, 2570, 1540),
            MaximisedWindowRefit.Refit(new PixelRect(-10, 70, 2570, 1540), new PixelRect(0, 80, 2560, 1530), new PixelRect(0, 40, 2560, 1530)));
    }

    [Fact]
    public void Refit_OnePixelCorrection_LeavesAWindowAlreadyFittedToTheNewEdge()
    {
        // Its top fits both the old edge (41, overhang 11) and the new one (40, overhang 10): it is already right.
        Assert.Null(MaximisedWindowRefit.Refit(new PixelRect(-10, 30, 2570, 1540), new PixelRect(0, 41, 2560, 1530), new PixelRect(0, 40, 2560, 1530)));
    }

    [Fact]
    public void Refit_StripTallerThanTheWindow_IsNull()
    {
        Assert.Null(MaximisedWindowRefit.Refit(Full, Full, new PixelRect(0, 1700, 2560, 1600)));
    }
}
