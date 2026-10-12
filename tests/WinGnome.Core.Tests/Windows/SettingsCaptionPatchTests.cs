using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class SettingsCaptionPatchTests
{
    // Windows Settings at 120 %: HTTOP for rows 1-5, HTCLOSE for rows 6-40 (row 0 is the border).
    private static readonly int[] SettingsColumn =
        [1, 12, 12, 12, 12, 12, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 1, 1];

    public static TheoryData<int[], int, bool> ResizeBorderRows => new()
    {
        { [1, 12, 12, 20], 3, true },
        { [12, 12, 12, 20], 3, true },
        { [1, 13, 12, 14, 20], 4, true },
        { [1, 12, 1, 20], 3, false },
        { [1, 1, 12, 20], 3, false },
        { [2, 2, 2, 20], 3, false },
        { [20, 20], 0, false },
        { [12, 12], 5, false },
        { [1, 12, 12], 3, true },
        { [12, 12, 12], 3, true },
        { [12, 12, 12], 4, false },
    };

    [Theory]
    [MemberData(nameof(ResizeBorderRows))]
    public void IsResizeBorderAbove_OnlyResizeCodesBelowTheBorderRow(int[] codes, int top, bool expected) =>
        Assert.Equal(expected, CaptionHitTestProbe.IsResizeBorderAbove(codes, top));

    [Fact]
    public void IsResizeBorderAbove_SettingsColumn_IsTrueAboveTheCloseZone()
    {
        var extent = CaptionHitTestProbe.FindCloseExtent(SettingsColumn, 10);

        Assert.Equal((6, 20), extent);
        Assert.True(CaptionHitTestProbe.IsResizeBorderAbove(SettingsColumn, extent!.Value.Top));
    }

    [Theory]
    [InlineData(279, 273, 1, 5)] // 120 %: the zone starts 6 rows down, 1 px border
    [InlineData(6, 0, 1, 5)]     // 100 %
    [InlineData(274, 273, 1, 0)] // zone already at the inside edge
    [InlineData(6, 0, 0, 6)]     // maximised: no border
    [InlineData(2, 5, 1, 0)]     // never negative
    public void PatchTopExtension_ReachesTheInsideEdgeOfTheBorder(int buttonsTop, int frameTop, int border, int expected) =>
        Assert.Equal(expected, CaptionButtonGeometry.PatchTopExtension(buttonsTop, frameTop, border));

    [Theory]
    [InlineData(1.0, 1, 6, 41, 1, 40, 22.5)]
    [InlineData(1.25, 274, 279, 314, 274, 40, 18.0)]
    public void Compute_PatchTopExtension_CoversTheBandAndKeepsTheCirclesWhereTheyWere(
        double scale, int boundsTop, int buttonsTop, int buttonsBottom, int expectedTop, int expectedHeight, double expectedCenterY)
    {
        var settings = new WindowButtonSettings { Side = ButtonSide.Right };
        var buttons = new PixelRect(1700, buttonsTop, 1846, buttonsBottom);
        var window = new PixelRect(1000, boundsTop - 1, 1847, 900);

        var layout = CaptionButtonLayout.Compute(buttons, window, scale, settings, buttonsTop - boundsTop)!;

        Assert.Equal(expectedTop, layout.Bounds.Top);
        Assert.Equal(buttonsBottom, layout.Bounds.Bottom);
        Assert.Equal(expectedHeight, layout.Bounds.Height);
        Assert.All(layout.Buttons, slot => Assert.Equal(expectedCenterY, slot.CenterY, 6));
    }

    [Fact]
    public void Compute_WithoutExtension_CentresOnTheButtons()
    {
        var layout = CaptionButtonLayout.Compute(new PixelRect(1700, 6, 1846, 41), new PixelRect(1000, 0, 1847, 900), 1, new WindowButtonSettings())!;

        Assert.Equal(new PixelRect(1700, 6, 1846, 41), layout.Bounds);
        Assert.All(layout.Buttons, slot => Assert.Equal(17.5, slot.CenterY, 6));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 3000)]
    [InlineData(2, 10000)]
    [InlineData(3, 30000)]
    [InlineData(4, null)]
    [InlineData(-1, null)]
    public void OwnButtonsRetry_DelayMs_BacksOffThenStops(int retry, int? expected) =>
        Assert.Equal(expected, OwnButtonsRetry.DelayMs(retry));
}
