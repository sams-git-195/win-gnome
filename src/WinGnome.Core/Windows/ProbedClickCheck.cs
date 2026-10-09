using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>
/// Guards clicks on circles over a window whose buttons were found by probing (spec 0009). Before the command is
/// sent, the native button's position is hit-tested again; the click goes through only when the window still
/// answers there what the probe saw, so a stale or wrong decoration never sends a command the app no longer
/// offers at that place.
/// </summary>
public static class ProbedClickCheck
{
    private const int HtClient = 1;

    /// <summary>
    /// What the window must answer over the native <paramref name="kind"/> button: its button code, or HTCLIENT
    /// for an app whose buttons are HTML holes (<see cref="CaptionHoleProbe"/>).
    /// </summary>
    public static int ExpectedCode(CaptionButtonKind kind, bool clientHoles) =>
        clientHoles
            ? HtClient
            : kind switch
            {
                CaptionButtonKind.Close => CaptionHitTestProbe.HtClose,
                CaptionButtonKind.Maximize => CaptionHitTestProbe.HtMaxButton,
                _ => CaptionHitTestProbe.HtMinButton,
            };

    /// <summary>
    /// The screen x to hit-test for <paramref name="kind"/>: the middle of its third of the probed group
    /// (<paramref name="buttons"/>), which is minimise, maximise, close from left to right. The thirds are only
    /// approximately the buttons (gaps, the resize border), but their middles are well inside each one.
    /// </summary>
    public static int CheckX(PixelRect buttons, CaptionButtonKind kind)
    {
        var fromRight = kind switch
        {
            CaptionButtonKind.Close => 1,
            CaptionButtonKind.Maximize => 3,
            _ => 5,
        };

        return buttons.Right - (buttons.Width * fromRight / 6);
    }

    /// <summary>True when the window answered (<paramref name="actualCode"/> is not null) what was expected.</summary>
    public static bool Allows(int expectedCode, int? actualCode) => actualCode == expectedCode;
}
