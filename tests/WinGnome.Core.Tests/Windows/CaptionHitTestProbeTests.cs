using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using Xunit;

namespace WinGnome.Core.Tests.Windows;

public sealed class CaptionHitTestProbeTests
{
    private const int Border = 11; // HTRIGHT
    private const int Caption = 2; // HTCAPTION
    private const int Client = 1;  // HTCLIENT
    private const int Min = CaptionHitTestProbe.HtMinButton;
    private const int Max = CaptionHitTestProbe.HtMaxButton;
    private const int Close = CaptionHitTestProbe.HtClose;

    /// <summary>A row sampled every 2 px leftwards from x = 1000: the runs of codes, with how many samples each covers.</summary>
    private static (int[] Xs, int[] Codes) Row(params (int Code, int Count)[] runs)
    {
        var codes = runs.SelectMany(run => Enumerable.Repeat(run.Code, run.Count)).ToArray();
        var xs = Enumerable.Range(0, codes.Length).Select(i => 1000 - (i * 2)).ToArray();
        return (xs, codes);
    }

    [Fact]
    public void FindGroupLeft_ClaudeDesktopRow_ReturnsTheMinimiseButtonsLeftEdge()
    {
        // Measured on Claude desktop at 125 %: a 2 px border, then 56-58 px zones, then the caption.
        var (xs, codes) = Row((Border, 1), (Close, 29), (Max, 28), (Min, 28), (Caption, 10));

        // The last minimise sample is at 1000 - 85 * 2 = 830, the first caption sample at 828.
        Assert.Equal(829, CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroupLeft_NoBorderSamples_StillFindsTheGroup()
    {
        var (xs, codes) = Row((Close, 23), (Max, 23), (Min, 23), (Client, 5));

        Assert.Equal(863, CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.0));
    }

    [Fact]
    public void FindGroupLeft_AllClient_IsNull()
    {
        // GitHub Desktop and Dia answer HTCLIENT over their own buttons.
        var (xs, codes) = Row((Client, 100));

        Assert.Null(CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroupLeft_WrongOrder_IsNull()
    {
        var (xs, codes) = Row((Close, 23), (Min, 23), (Max, 23), (Caption, 5));

        Assert.Null(CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.0));
    }

    [Fact]
    public void FindGroupLeft_GapBetweenZones_IsNull()
    {
        var (xs, codes) = Row((Close, 23), (Caption, 2), (Max, 23), (Min, 23), (Caption, 5));

        Assert.Null(CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.0));
    }

    [Fact]
    public void FindGroupLeft_OnlyCloseButton_IsNull()
    {
        var (xs, codes) = Row((Close, 23), (Caption, 60));

        Assert.Null(CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.0));
    }

    [Fact]
    public void FindGroupLeft_ButtonCodeAfterTheTrio_IsNull()
    {
        var (xs, codes) = Row((Close, 23), (Max, 23), (Min, 23), (Caption, 5), (Close, 3), (Caption, 5));

        Assert.Null(CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.0));
    }

    [Fact]
    public void FindGroupLeft_TrioRunsOffTheEndOfTheRow_IsNull()
    {
        var (xs, codes) = Row((Close, 23), (Max, 23), (Min, 23));

        Assert.Null(CaptionHitTestProbe.FindGroupLeft(xs, codes, 1.0));
    }

    [Theory]
    [InlineData(1.0, 11, null)]  // 22 px buttons are too narrow at 100 % (minimum 24 DIPs, 2 px sampling slack)
    [InlineData(1.0, 12, 929)]
    [InlineData(2.0, 23, null)]  // 46 px is too narrow at 200 %
    [InlineData(2.0, 24, 857)]
    public void FindGroupLeft_RejectsZonesNarrowerThanAButton(double scale, int samplesPerZone, int? expected)
    {
        var (xs, codes) = Row((Close, samplesPerZone), (Max, samplesPerZone), (Min, samplesPerZone), (Caption, 5));

        Assert.Equal(expected, CaptionHitTestProbe.FindGroupLeft(xs, codes, scale));
    }

    [Fact]
    public void FindGroupLeft_MismatchedLengths_IsNull()
    {
        Assert.Null(CaptionHitTestProbe.FindGroupLeft([1000, 998], [Close], 1.0));
    }

    [Fact]
    public void FindCloseExtent_ClaudeDesktopColumn_ReturnsTheCloseRows()
    {
        // HTTOPRIGHT (14) on the top pixel, close for 36 rows, then client.
        int[] codes = [14, .. Enumerable.Repeat(Close, 36), .. Enumerable.Repeat(Client, 20)];

        Assert.Equal((1, 37), CaptionHitTestProbe.FindCloseExtent(codes, maxTopGap: 4));
    }

    [Fact]
    public void FindCloseExtent_CloseStartsTooLow_IsNull()
    {
        int[] codes = [.. Enumerable.Repeat(Caption, 6), .. Enumerable.Repeat(Close, 30), Client];

        Assert.Null(CaptionHitTestProbe.FindCloseExtent(codes, maxTopGap: 4));
    }

    [Fact]
    public void FindCloseExtent_CloseReachesTheEnd_IsNull()
    {
        int[] codes = [.. Enumerable.Repeat(Close, 10)];

        Assert.Null(CaptionHitTestProbe.FindCloseExtent(codes, maxTopGap: 4));
    }

    [Fact]
    public void FindCloseExtent_NoClose_IsNull()
    {
        Assert.Null(CaptionHitTestProbe.FindCloseExtent([Client, Client, Client], maxTopGap: 4));
    }

    [Theory]
    [InlineData(1.0, 100, 10)]
    [InlineData(1.25, 125, 13)]
    [InlineData(2.0, 200, 20)]
    [InlineData(double.NaN, 100, 10)]
    public void Samples_ScaleWithDpi(double scale, int expectedRowSamples, int expectedRowOffset)
    {
        var frame = new PixelRect(-1920, -40, -100, 900);

        var xs = CaptionHitTestProbe.RowSamples(frame, scale);

        Assert.Equal(expectedRowSamples, xs.Length);
        Assert.Equal(-101, xs[0]);
        Assert.Equal(-103, xs[1]);
        Assert.Equal(-40 + expectedRowOffset, CaptionHitTestProbe.RowY(frame, scale));
    }

    [Theory]
    [InlineData(1.0, 64)]
    [InlineData(1.25, 80)]
    [InlineData(1.5, 96)]
    public void ColumnSampleCount_ScalesWithDpi(double scale, int expected)
    {
        Assert.Equal(expected, CaptionHitTestProbe.ColumnSampleCount(scale));
    }

    [Fact]
    public void ButtonsRect_SpansFromTheGroupToTheFrameEdge()
    {
        var frame = new PixelRect(336, 274, 2420, 1345);

        Assert.Equal(new PixelRect(2247, 275, 2420, 311), CaptionHitTestProbe.ButtonsRect(frame, 2247, (1, 37)));
    }
}
