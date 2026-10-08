using WinGnome.Core.Geometry;
using WinGnome.Core.Overview;

namespace WinGnome.Core.Tests.Overview;

public class OverviewLayoutTests
{
    private const double Tolerance = 1e-9;

    private static void AssertRect(double x, double y, double w, double h, LayoutRect actual)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
        Assert.Equal(w, actual.Width, 6);
        Assert.Equal(h, actual.Height, 6);
    }

    [Fact]
    public void Empty_GivesEmpty()
    {
        Assert.Empty(OverviewLayout.Arrange([], new LayoutRect(0, 0, 100, 100), 10));
    }

    [Fact]
    public void SingleSmallWindow_IsNotUpscaled_AndIsCentred()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(400, 300)], new LayoutRect(0, 0, 1000, 700), 0);

        AssertRect(300, 200, 400, 300, Assert.Single(result));
    }

    [Fact]
    public void SingleLargeWindow_IsScaledDownPreservingAspectRatio()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(2000, 1000)], new LayoutRect(0, 0, 1000, 1000), 0);

        // width-limited: scale 0.5 -> 1000 x 500, centred vertically
        AssertRect(0, 250, 1000, 500, Assert.Single(result));
    }

    [Fact]
    public void AreaOrigin_IsRespected()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(100, 100)], new LayoutRect(500, 300, 200, 200), 0);

        AssertRect(550, 350, 100, 100, Assert.Single(result));
    }

    [Fact]
    public void TwoSmallWindows_SitSideBySideInOneRow()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(400, 300), new LayoutSize(400, 300)], new LayoutRect(0, 0, 1000, 700), 0);

        AssertRect(100, 200, 400, 300, result[0]);
        AssertRect(500, 200, 400, 300, result[1]);
    }

    [Fact]
    public void Spacing_IsFixedAndNotScaled()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(400, 300), new LayoutSize(400, 300)], new LayoutRect(0, 0, 800, 300), 20);

        // scale = (800 - 20) / 800 = 0.975
        AssertRect(0, 3.75, 390, 292.5, result[0]);
        AssertRect(410, 3.75, 390, 292.5, result[1]);
        Assert.Equal(20, result[1].X - result[0].Right, 6);
    }

    [Fact]
    public void FourWindows_PreferTwoRowsWhenThatGivesTheBiggestScale()
    {
        var windows = Enumerable.Repeat(new LayoutSize(800, 600), 4).ToArray();

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 1600, 900), 0);

        // 1 row -> 0.5, 2 rows -> 0.75, 3 rows -> 0.5, 4 rows -> 0.375
        AssertRect(200, 0, 600, 450, result[0]);
        AssertRect(800, 0, 600, 450, result[1]);
        AssertRect(200, 450, 600, 450, result[2]);
        AssertRect(800, 450, 600, 450, result[3]);
    }

    [Fact]
    public void TallArea_StacksWindowsInColumnLikeRows()
    {
        var windows = Enumerable.Repeat(new LayoutSize(800, 600), 3).ToArray();

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 400, 1200), 0);

        // 3 rows of one window: scale 0.5 -> 400 x 300 each, block of 900 centred in 1200
        AssertRect(0, 150, 400, 300, result[0]);
        AssertRect(0, 450, 400, 300, result[1]);
        AssertRect(0, 750, 400, 300, result[2]);
    }

    [Fact]
    public void UnevenRowCounts_PutExtraWindowsInEarlierRows()
    {
        var windows = Enumerable.Repeat(new LayoutSize(800, 600), 5).ToArray();

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 1600, 1200), 0);

        // 2 rows: 3 + 2 windows, scale = min(1600/2400, 1200/1200) = 2/3
        var rows = result.GroupBy(r => Math.Round(r.Y, 6)).OrderBy(g => g.Key).Select(g => g.Count()).ToArray();
        Assert.Equal([3, 2], rows);
    }

    [Fact]
    public void ZeroSizedWindows_CountAsOneByOne()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(0, 0), new LayoutSize(-5, double.NaN)], new LayoutRect(0, 0, 100, 100), 0);

        Assert.All(result, r =>
        {
            Assert.Equal(1, r.Width, 6);
            Assert.Equal(1, r.Height, 6);
        });
    }

    [Fact]
    public void OutputIsIndexAligned_WithInputOrderPreservedInReadingOrder()
    {
        var windows = new[]
        {
            new LayoutSize(800, 600),
            new LayoutSize(400, 300),
            new LayoutSize(1200, 700),
            new LayoutSize(300, 900),
            new LayoutSize(640, 480),
            new LayoutSize(1000, 1000),
            new LayoutSize(500, 500),
        };

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 1920, 1080), 16);

        Assert.Equal(windows.Length, result.Count);
        for (var i = 0; i < windows.Length; i++)
        {
            Assert.Equal(windows[i].Width / windows[i].Height, result[i].Width / result[i].Height, 9);
        }

        // reading order: a later window is never above an earlier one's row, and within a row moves right
        for (var i = 1; i < result.Count; i++)
        {
            var sameRow = result[i].Y < result[i - 1].Bottom - Tolerance && result[i].Bottom > result[i - 1].Y + Tolerance;
            if (sameRow)
            {
                Assert.True(result[i].X >= result[i - 1].Right - Tolerance);
            }
            else
            {
                Assert.True(result[i].Y >= result[i - 1].Y - Tolerance);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(30)]
    public void Results_StayInsideTheArea_WithoutOverlapping(int count)
    {
        var random = new Random(count);
        var windows = Enumerable.Range(0, count)
            .Select(_ => new LayoutSize(200 + random.Next(1600), 150 + random.Next(900)))
            .ToArray();
        var area = new LayoutRect(40, 60, 1600, 900);

        var result = OverviewLayout.Arrange(windows, area, 12);

        foreach (var r in result)
        {
            Assert.True(r.X >= area.X - Tolerance);
            Assert.True(r.Y >= area.Y - Tolerance);
            Assert.True(r.Right <= area.Right + Tolerance);
            Assert.True(r.Bottom <= area.Bottom + Tolerance);
        }

        for (var i = 0; i < result.Count; i++)
        {
            for (var j = i + 1; j < result.Count; j++)
            {
                var overlapX = result[i].X < result[j].Right - Tolerance && result[j].X < result[i].Right - Tolerance;
                var overlapY = result[i].Y < result[j].Bottom - Tolerance && result[j].Y < result[i].Bottom - Tolerance;
                Assert.False(overlapX && overlapY, $"windows {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void AllWindowsShareOneUniformScale()
    {
        var windows = new[] { new LayoutSize(1600, 900), new LayoutSize(800, 450), new LayoutSize(400, 400) };

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 1000, 600), 10);

        var scales = result.Select((r, i) => r.Width / windows[i].Width).ToArray();
        Assert.All(scales, s => Assert.Equal(scales[0], s, 9));
        Assert.True(scales[0] <= 1);
    }

    [Fact]
    public void Scale_NeverExceedsOne_EvenInAHugeArea()
    {
        var windows = new[] { new LayoutSize(100, 80), new LayoutSize(60, 60) };

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 10000, 10000), 5);

        Assert.Equal(100, result[0].Width, 9);
        Assert.Equal(80, result[0].Height, 9);
        Assert.Equal(60, result[1].Width, 9);
    }

    [Fact]
    public void DegenerateArea_StillReturnsFiniteRects()
    {
        var result = OverviewLayout.Arrange([new LayoutSize(100, 100), new LayoutSize(100, 100)], new LayoutRect(0, 0, 0, 0), 10);

        Assert.Equal(2, result.Count);
        Assert.All(result, r =>
        {
            Assert.True(double.IsFinite(r.X) && double.IsFinite(r.Y));
            Assert.True(r.Width > 0 && r.Height > 0);
        });
    }

    [Fact]
    public void NegativeOrNanSpacing_IsTreatedAsZero()
    {
        var area = new LayoutRect(0, 0, 1000, 700);
        var windows = new[] { new LayoutSize(400, 300), new LayoutSize(400, 300) };

        var expected = OverviewLayout.Arrange(windows, area, 0);

        Assert.Equal(expected, OverviewLayout.Arrange(windows, area, -10));
        Assert.Equal(expected, OverviewLayout.Arrange(windows, area, double.NaN));
    }

    [Fact]
    public void RowsWithDifferentHeights_AreCentredVerticallyWithinTheirRow()
    {
        var windows = new[] { new LayoutSize(400, 400), new LayoutSize(400, 200) };

        var result = OverviewLayout.Arrange(windows, new LayoutRect(0, 0, 800, 400), 0);

        Assert.Equal(result[0].CenterY, result[1].CenterY, 9);
    }
}
