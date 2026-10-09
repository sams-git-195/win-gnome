using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using Xunit;

namespace WinGnome.Core.Tests.Windows;

public sealed class CaptionHoleProbeTests
{
    private const int Nowhere = 0;   // HTNOWHERE
    private const int Client = 1;    // HTCLIENT
    private const int Caption = 2;   // HTCAPTION
    private const int Right = 11;    // HTRIGHT
    private const int Top = 12;      // HTTOP
    private const int TopRight = 14; // HTTOPRIGHT

    /// <summary>GitHub Desktop's buttons are 45 DIPs wide.</summary>
    private const double ButtonWidth = 45;

    /// <summary>A row sampled every pixel leftwards from x = 1000: the runs of codes, with how many pixels each covers.</summary>
    private static (int[] Xs, int[] Codes) Row(params (int Code, int Count)[] runs)
    {
        var codes = runs.SelectMany(run => Enumerable.Repeat(run.Code, run.Count)).ToArray();
        var xs = Enumerable.Range(0, codes.Length).Select(i => 1000 - i).ToArray();
        return (xs, codes);
    }

    private static int[] Column(params (int Code, int Count)[] runs) =>
        runs.SelectMany(run => Enumerable.Repeat(run.Code, run.Count)).ToArray();

    [Fact]
    public void FindClientHoles_GitHubDesktopRestored_ReturnsTheGroupsLeftEdge()
    {
        // Measured on GitHub Desktop at 125 %: a 7 px resize corner over the close button, 1 px drag strips
        // between the holes, then the drag region.
        var (xs, codes) = Row((TopRight, 7), (Client, 49), (Caption, 1), (Client, 55), (Caption, 1), (Client, 56), (Caption, 30));

        Assert.Equal(832, CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_GitHubDesktopMaximised_HolesTouchTheEdge()
    {
        var (xs, codes) = Row((Client, 56), (Caption, 1), (Client, 55), (Caption, 1), (Client, 56), (Caption, 30));

        Assert.Equal(832, CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_TwoHoles_IsNull()
    {
        var (xs, codes) = Row((Client, 56), (Caption, 1), (Client, 55), (Caption, 60));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_FourHoles_IsNull()
    {
        var (xs, codes) = Row((Client, 56), (Caption, 1), (Client, 55), (Caption, 1), (Client, 56), (Caption, 1), (Client, 56), (Caption, 30));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Theory]
    [InlineData(44)]  // too narrow
    [InlineData(70)]  // too wide
    public void FindClientHoles_UnequalHoles_IsNull(int middleWidth)
    {
        var (xs, codes) = Row((Client, 56), (Caption, 1), (Client, middleWidth), (Caption, 1), (Client, 56), (Caption, 30));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_HolesOfAnotherSize_IsNull()
    {
        // Three equal 40 px holes are not GitHub Desktop's 56 px buttons.
        var (xs, codes) = Row((Client, 40), (Caption, 1), (Client, 40), (Caption, 1), (Client, 40), (Caption, 60));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Theory]
    [InlineData(Caption, 20, 56)]  // drag region between the edge and the holes
    [InlineData(Right, 12, 44)]    // more than 8 DIPs of resize border, even though border and hole add up
    public void FindClientHoles_HolesAwayFromTheEdge_IsNull(int leadingCode, int leadingCount, int closeWidth)
    {
        var (xs, codes) = Row((leadingCode, leadingCount), (Client, closeWidth), (Caption, 1), (Client, 55), (Caption, 1), (Client, 56), (Caption, 30));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Theory]
    [InlineData(Nowhere, 30)]
    [InlineData(Caption, 9)]  // less than 8 DIPs of drag region
    public void FindClientHoles_NoCaptionToTheLeft_IsNull(int leftCode, int leftCount)
    {
        var (xs, codes) = Row((Client, 56), (Caption, 1), (Client, 55), (Caption, 1), (Client, 56), (leftCode, leftCount), (Client, 30));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_WideGapBetweenHoles_IsNull()
    {
        var (xs, codes) = Row((Client, 56), (Caption, 4), (Client, 55), (Caption, 1), (Client, 56), (Caption, 30));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_AllClient_IsNull()
    {
        var (xs, codes) = Row((Client, 250));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_HolesRunOffTheEndOfTheRow_IsNull()
    {
        var (xs, codes) = Row((Client, 56), (Caption, 1), (Client, 55), (Caption, 1), (Client, 56));

        Assert.Null(CaptionHoleProbe.FindClientHoles(xs, codes, 1.25, ButtonWidth));
    }

    [Fact]
    public void FindClientHoles_MismatchedLengths_IsNull()
    {
        Assert.Null(CaptionHoleProbe.FindClientHoles([1000, 999], [Client], 1.25, ButtonWidth));
    }

    [Fact]
    public void FindHoleExtent_GitHubDesktopRestored_CoversTheResizeBorderAbove()
    {
        // 7 rows of top resize border over the buttons, 27 rows of hole, a 1 px drag strip, then the page.
        var codes = Column((Top, 7), (Client, 27), (Caption, 1), (Client, 45));

        Assert.Equal((0, 34), CaptionHoleProbe.FindHoleExtent(codes, maxTopGap: 10));
    }

    [Fact]
    public void FindHoleExtent_GitHubDesktopMaximised_StartsAtTheTop()
    {
        var codes = Column((Client, 34), (Caption, 1), (Client, 45));

        Assert.Equal((0, 34), CaptionHoleProbe.FindHoleExtent(codes, maxTopGap: 10));
    }

    [Fact]
    public void FindHoleExtent_ClientAllTheWayDown_IsNull()
    {
        // Without a non-client row under the buttons nothing says where they end.
        Assert.Null(CaptionHoleProbe.FindHoleExtent(Column((Top, 7), (Client, 73)), maxTopGap: 10));
    }

    [Theory]
    [InlineData(Caption, 3)]  // drag region above the hole
    [InlineData(Top, 11)]     // more border than allowed
    public void FindHoleExtent_HoleStartsTooLow_IsNull(int aboveCode, int aboveCount)
    {
        var codes = Column((aboveCode, aboveCount), (Client, 27), (Caption, 1), (Client, 40));

        Assert.Null(CaptionHoleProbe.FindHoleExtent(codes, maxTopGap: 10));
    }

    [Theory]
    [InlineData(1.0, 200)]
    [InlineData(1.25, 250)]
    public void RowSamples_EveryPixelFromTheRightEdge(double scale, int expectedCount)
    {
        var frame = new PixelRect(-1920, -40, -100, 900);

        var xs = CaptionHoleProbe.RowSamples(frame, scale);

        Assert.Equal(expectedCount, xs.Length);
        Assert.Equal(-101, xs[0]);
        Assert.Equal(-102, xs[1]);
    }
}
