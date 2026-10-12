using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using Xunit;

namespace WinGnome.Core.Tests.Windows;

public sealed class OwnCaptionButtonsTests
{
    private const int Border = 14; // HTTOPRIGHT
    private const int Caption = 2; // HTCAPTION
    private const int Client = 1;  // HTCLIENT

    // DWM's rectangle for Steam at 125 %: 185 px wide, ending 8 px inside the frame's right edge (1000).
    private static readonly PixelRect SteamNative = new(807, 0, 992, 37);

    /// <summary>A row sampled every 2 px leftwards from x = 999: the runs of codes, with how many samples each covers.</summary>
    private static (int[] Xs, int[] Codes) Row(params (int Code, int Count)[] runs)
    {
        var codes = runs.SelectMany(run => Enumerable.Repeat(run.Code, run.Count)).ToArray();
        var xs = Enumerable.Range(0, codes.Length).Select(i => 999 - (i * 2)).ToArray();
        return (xs, codes);
    }

    [Fact]
    public void FindLeft_SteamRow_ReturnsTheLeftEdgeOfItsOwnButtons()
    {
        // Measured on Steam at 125 %: 4 px border, 116 px of client (its buttons), 10 px of caption, then more
        // client (its friends and notification controls).
        var (xs, codes) = Row((Border, 2), (Client, 58), (Caption, 5), (Client, 60));

        // The last client sample is at 999 - 59 * 2 = 881, the first caption sample at 879.
        Assert.Equal(880, OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }

    [Fact]
    public void FindLeft_AllClient_IsNull()
    {
        // File Explorer answers HTCLIENT along the whole row: its buttons fill DWM's rectangle.
        var (xs, codes) = Row((Border, 4), (Client, 121));

        Assert.Null(OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }

    [Fact]
    public void FindLeft_WindowsDrawnButtonCodes_IsNull()
    {
        var (xs, codes) = Row((CaptionHitTestProbe.HtClose, 29), (CaptionHitTestProbe.HtMaxButton, 28), (Caption, 40));

        Assert.Null(OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }

    [Theory]
    [InlineData(2, null)] // 4 px of caption is under 4 DIPs at 125 %
    [InlineData(3, 880)]  // 6 px is enough
    public void FindLeft_CaptionRunMustBeWideEnough(int captionSamples, int? expected)
    {
        var (xs, codes) = Row((Border, 2), (Client, 58), (Caption, captionSamples), (Client, 60));

        Assert.Equal(expected, OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }

    [Theory]
    [InlineData(29, null)] // 58 px is under 48 DIPs at 125 %
    [InlineData(30, 940)]  // 60 px is enough
    public void FindLeft_ClientRunMustBeAButtonGroupWide(int clientSamples, int? expected)
    {
        var (xs, codes) = Row((Client, clientSamples), (Caption, 10));

        Assert.Equal(expected, OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }

    [Fact]
    public void FindLeft_EdgeAtOrLeftOfTheNativeRectangle_IsNull()
    {
        // The client run reaches past DWM's left edge (807): trimming would not narrow anything.
        var (xs, codes) = Row((Client, 97), (Caption, 10));

        Assert.Null(OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }

    [Fact]
    public void FindLeft_ClientRunReachingTheEndOfTheRow_IsNull()
    {
        var (xs, codes) = Row((Border, 2), (Client, 40));

        Assert.Null(OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.0));
    }

    [Fact]
    public void FindLeft_CaptionAtTheFrameEdge_IsNull()
    {
        // A caption strip wider than the resize border before any client: no buttons at the edge.
        var (xs, codes) = Row((Caption, 10), (Client, 40), (Caption, 10));

        Assert.Null(OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.0));
    }

    [Fact]
    public void FindLeft_NegativeCoordinates_Works()
    {
        var native = new PixelRect(-1193, 0, -1008, 37);
        var codes = new[] { Border, Border }.Concat(Enumerable.Repeat(Client, 58)).Concat(Enumerable.Repeat(Caption, 5)).ToArray();
        var xs = Enumerable.Range(0, codes.Length).Select(i => -1001 - (i * 2)).ToArray();

        Assert.Equal(-1120, OwnCaptionButtons.FindLeft(xs, codes, native, 1.25));
    }

    [Fact]
    public void FindLeft_MismatchedOrEmptyRows_AreNull()
    {
        Assert.Null(OwnCaptionButtons.FindLeft([], [], SteamNative, 1.25));
        Assert.Null(OwnCaptionButtons.FindLeft([999, 997], [Client], SteamNative, 1.25));
    }

    [Theory]
    [InlineData(5, 880)]  // 10 px of border codes: within 8 DIPs at 125 %
    [InlineData(6, null)] // 12 px: the client run is not at the frame's edge
    public void FindLeft_GapToTheFrameEdgeIsLimited(int borderSamples, int? expected)
    {
        var (xs, codes) = Row((Border, borderSamples), (Client, 60 - borderSamples), (Caption, 5));

        Assert.Equal(expected, OwnCaptionButtons.FindLeft(xs, codes, SteamNative, 1.25));
    }
}
