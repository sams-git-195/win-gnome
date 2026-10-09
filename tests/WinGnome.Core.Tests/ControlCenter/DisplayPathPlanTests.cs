using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class DisplayPathPlanTests
{
    private static readonly RefreshRate Ntsc60 = new(60000, 1001);
    private static readonly RefreshRate Exact60 = new(60, 1);
    private static readonly DisplaySetting Current = new(@"\\.\DISPLAY1", 1920, 1080, 59, 0, 0, IsPrimary: true);

    [Theory]
    [InlineData(60000u, 1001u, 59)]
    [InlineData(60u, 1u, 60)]
    [InlineData(240000u, 1000u, 240)]
    [InlineData(24000u, 1001u, 23)]
    [InlineData(144u, 1u, 144)]
    [InlineData(60u, 0u, 0)]
    public void GdiRate_IsTheFloorOfTheRational(uint numerator, uint denominator, int expected)
    {
        Assert.Equal(expected, DisplayPathPlan.GdiRate(new RefreshRate(numerator, denominator)));
    }

    [Theory]
    [InlineData(59, 60000u, 1001u)]
    [InlineData(29, 30000u, 1001u)]
    [InlineData(23, 24000u, 1001u)]
    [InlineData(119, 120000u, 1001u)]
    [InlineData(47, 48000u, 1001u)]
    [InlineData(60, 60u, 1u)]
    [InlineData(144, 144u, 1u)]
    [InlineData(75, 75u, 1u)]
    public void RationalFor_MapsGdiNtscRatesToTheirFractions(int hz, uint numerator, uint denominator)
    {
        Assert.Equal(new RefreshRate(numerator, denominator), DisplayPathPlan.RationalFor(hz));
    }

    [Theory]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(144)]
    public void RationalFor_RoundTripsThroughGdiRate(int hz)
    {
        Assert.Equal(hz, DisplayPathPlan.GdiRate(DisplayPathPlan.RationalFor(hz)));
    }

    [Fact]
    public void Plan_SameModeAndRate_ChangesNothingButTheSourceMode()
    {
        var plan = DisplayPathPlan.Plan(Ntsc60, Current, Current with { X = -1920 });

        Assert.Equal(new PathEdit(WriteRefresh: false, Ntsc60, InvalidateTargetMode: false), plan);
    }

    [Fact]
    public void Plan_From5994To60_WritesTheExactRate()
    {
        // GDI reports 59.94 Hz as 59; picking 60 must reach Windows even though the two differ by less than 1 Hz.
        var plan = DisplayPathPlan.Plan(Ntsc60, Current, Current with { RefreshHz = 60 });

        Assert.Equal(new PathEdit(WriteRefresh: true, Exact60, InvalidateTargetMode: true), plan);
    }

    [Fact]
    public void Plan_From60To5994_WritesTheNtscFraction()
    {
        var current = Current with { RefreshHz = 60 };

        var plan = DisplayPathPlan.Plan(Exact60, current, current with { RefreshHz = 59 });

        Assert.Equal(new PathEdit(WriteRefresh: true, Ntsc60, InvalidateTargetMode: true), plan);
    }

    [Fact]
    public void Plan_NewResolutionSameRate_InvalidatesTheTargetModeOnly()
    {
        var plan = DisplayPathPlan.Plan(Ntsc60, Current, Current with { Width = 1280, Height = 720 });

        Assert.Equal(new PathEdit(WriteRefresh: false, Ntsc60, InvalidateTargetMode: true), plan);
    }

    [Fact]
    public void ExpectedAfterRevert_KeepsTheDisplaysAttachedBeforeTheRevert()
    {
        var b = Current with { DeviceName = @"\\.\DISPLAY2", X = 1920 };

        var expected = DisplayPathPlan.ExpectedAfterRevert([Current, b], [@"\\.\display1", @"\\.\DISPLAY2"]);

        Assert.Equal([Current, b], expected);
    }

    [Fact]
    public void ExpectedAfterRevert_DropsDisplaysAlreadyGoneBeforeTheRevert()
    {
        var b = Current with { DeviceName = @"\\.\DISPLAY2", X = 1920 };

        Assert.Equal([Current], DisplayPathPlan.ExpectedAfterRevert([Current, b], [@"\\.\DISPLAY1"]));
    }
}
