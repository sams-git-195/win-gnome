using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using Xunit;

namespace WinGnome.Core.Tests.Windows;

public sealed class ProbedClickCheckTests
{
    [Theory]
    [InlineData(20, 20, true)]   // HTCLOSE still there
    [InlineData(9, 9, true)]
    [InlineData(8, 8, true)]
    [InlineData(1, 1, true)]     // a web button's HTCLIENT hole still there
    [InlineData(20, 9, false)]   // the buttons moved
    [InlineData(20, 1, false)]   // the app no longer reports a button there
    [InlineData(20, 2, false)]
    [InlineData(1, 2, false)]    // the hole became drag region
    [InlineData(1, 20, false)]
    [InlineData(20, null, false)] // no answer (hung, gone, timed out)
    [InlineData(1, null, false)]
    public void Allows_OnlyWhenTheWindowStillAnswersWhatTheProbeSaw(int expected, int? actual, bool allowed)
    {
        Assert.Equal(allowed, ProbedClickCheck.Allows(expected, actual));
    }

    [Theory]
    [InlineData(CaptionButtonKind.Close, false, 20)]
    [InlineData(CaptionButtonKind.Maximize, false, 9)]
    [InlineData(CaptionButtonKind.Minimize, false, 8)]
    [InlineData(CaptionButtonKind.Close, true, 1)]
    [InlineData(CaptionButtonKind.Maximize, true, 1)]
    [InlineData(CaptionButtonKind.Minimize, true, 1)]
    public void ExpectedCode_ButtonCodeOrClientHole(CaptionButtonKind kind, bool clientHoles, int expected)
    {
        Assert.Equal(expected, ProbedClickCheck.ExpectedCode(kind, clientHoles));
    }

    [Theory]
    [InlineData(CaptionButtonKind.Close, 1447)]     // hole 1419..1467
    [InlineData(CaptionButtonKind.Maximize, 1391)]  // hole 1363..1417
    [InlineData(CaptionButtonKind.Minimize, 1335)]  // hole 1306..1361
    public void CheckX_GitHubDesktop_IsInsideTheNativeButton(CaptionButtonKind kind, int expected)
    {
        var buttons = new PixelRect(1306, 321, 1475, 355);

        Assert.Equal(expected, ProbedClickCheck.CheckX(buttons, kind));
    }

    [Theory]
    [InlineData(CaptionButtonKind.Close, 2231)]     // HTCLOSE 2201..2257
    [InlineData(CaptionButtonKind.Maximize, 2173)]  // HTMAXBUTTON 2143..2200
    [InlineData(CaptionButtonKind.Minimize, 2116)]  // HTMINBUTTON 2087..2142
    public void CheckX_ClaudeDesktop_IsInsideTheNativeButton(CaptionButtonKind kind, int expected)
    {
        var buttons = new PixelRect(2087, 395, 2259, 432);

        Assert.Equal(expected, ProbedClickCheck.CheckX(buttons, kind));
    }
}
