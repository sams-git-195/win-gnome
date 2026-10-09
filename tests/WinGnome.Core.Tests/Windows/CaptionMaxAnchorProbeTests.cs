using WinGnome.Core.Windows;
using Xunit;

namespace WinGnome.Core.Tests.Windows;

public sealed class CaptionMaxAnchorProbeTests
{
    private const int Client = 1;   // HTCLIENT
    private const int Caption = 2;  // HTCAPTION
    private const int Right = 11;   // HTRIGHT
    private const int Top = 12;     // HTTOP
    private const int Max = CaptionHitTestProbe.HtMaxButton;
    private const int Min = CaptionHitTestProbe.HtMinButton;
    private const int Close = CaptionHitTestProbe.HtClose;

    /// <summary>A row sampled every 2 px leftwards from x = 1000 (frame right 1001): runs of codes with their sample counts.</summary>
    private static (int[] Xs, int[] Codes) Row(params (int Code, int Count)[] runs)
    {
        var codes = runs.SelectMany(run => Enumerable.Repeat(run.Code, run.Count)).ToArray();
        var xs = Enumerable.Range(0, codes.Length).Select(i => 1000 - (i * 2)).ToArray();
        return (xs, codes);
    }

    [Fact]
    public void FindGroup_DiaRestored_ReturnsTheGroupAndTheMaximiseMiddle()
    {
        // Measured on Dia at 125 %: 5 px HTRIGHT, 54 px HTCLIENT over close, 50 px HTMAXBUTTON, then HTCLIENT.
        // Every 2 px: 3 border samples, 27 close samples (994..942), 25 maximise samples (940..892), client.
        var (xs, codes) = Row((Right, 3), (Client, 27), (Max, 25), (Client, 80));

        // Close is 59 px with the border, maximise 49 px; the maximise zone's left edge is 891, and minimise is
        // taken to be as wide as maximise.
        Assert.Equal((842, 916), CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_Maximised_TouchesTheEdge()
    {
        var (xs, codes) = Row((Client, 27), (Max, 25), (Client, 80));

        Assert.Equal((848, 922), CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Theory]
    [InlineData(34)]  // close 67 px vs maximise 49 px
    [InlineData(18)]  // close 35 px
    public void FindGroup_CloseZoneNotAboutAsWideAsMaximise_IsNull(int closeSamples)
    {
        var (xs, codes) = Row((Client, closeSamples), (Max, 25), (Client, 80));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_MaximiseNarrowerThanAButton_IsNull()
    {
        // 29 px is less than 30 DIPs at 125 %, even with a matching close zone.
        var (xs, codes) = Row((Client, 15), (Max, 15), (Client, 80));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_NoRoomForMinimise_IsNull()
    {
        // Only 39 px of client area left of maximise before the drag region.
        var (xs, codes) = Row((Client, 27), (Max, 25), (Client, 20), (Caption, 40));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Theory]
    [InlineData(Caption, 3)]  // drag region between the edge and the close zone
    [InlineData(Right, 6)]    // 11 px of border: more than 8 DIPs
    public void FindGroup_CloseZoneAwayFromTheEdge_IsNull(int leadingCode, int leadingSamples)
    {
        var (xs, codes) = Row((leadingCode, leadingSamples), (Client, 24), (Max, 25), (Client, 80));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Theory]
    [InlineData(Caption)]
    [InlineData(Right)]
    public void FindGroup_ZonesNotImmediatelyAdjacent_IsNull(int gapCode)
    {
        var (xs, codes) = Row((Client, 27), (gapCode, 1), (Max, 25), (Client, 80));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_MinimiseZoneNotClient_IsNull()
    {
        var (xs, codes) = Row((Client, 27), (Max, 25), (Caption, 80));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Theory]
    [InlineData(Min)]
    [InlineData(Close)]
    public void FindGroup_OtherButtonCodesInTheRow_IsNull(int otherCode)
    {
        // An app reporting another button too is not this pattern (and the button-code probe handles it).
        var (xs, codes) = Row((Client, 27), (Max, 25), (Client, 30), (otherCode, 25), (Client, 10));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_SecondMaximiseZoneFurtherLeft_IsNull()
    {
        var (xs, codes) = Row((Client, 27), (Max, 25), (Client, 30), (Max, 25), (Client, 10));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_MaximiseRunsOffTheEndOfTheRow_IsNull()
    {
        var (xs, codes) = Row((Client, 27), (Max, 25));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_NoMaximise_IsNull()
    {
        var (xs, codes) = Row((Client, 130));

        Assert.Null(CaptionMaxAnchorProbe.FindGroup(xs, codes, 1.25));
    }

    [Fact]
    public void FindGroup_MismatchedLengths_IsNull()
    {
        Assert.Null(CaptionMaxAnchorProbe.FindGroup([1000, 998], [Client], 1.25));
    }

    [Fact]
    public void FindZoneExtent_DiaMaximiseColumn_ReturnsTheMaximiseRows()
    {
        // Through Dia's maximise button: 4 rows of HTTOP, 52 of HTMAXBUTTON, then client.
        int[] codes = [.. Enumerable.Repeat(Top, 4), .. Enumerable.Repeat(Max, 52), .. Enumerable.Repeat(Client, 24)];

        Assert.Equal((4, 56), CaptionHitTestProbe.FindZoneExtent(codes, Max, maxTopGap: 10));
    }

    [Fact]
    public void FindZoneExtent_ZoneReachesTheEnd_IsNull()
    {
        int[] codes = [.. Enumerable.Repeat(Top, 4), .. Enumerable.Repeat(Max, 20)];

        Assert.Null(CaptionHitTestProbe.FindZoneExtent(codes, Max, maxTopGap: 10));
    }
}
