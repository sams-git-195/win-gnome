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
    /// What the window must answer over the native <paramref name="kind"/> button: its button code, HTCLIENT
    /// for an app whose buttons are HTML holes (<see cref="CaptionHoleProbe"/>), or for a maximise-anchored
    /// group HTMAXBUTTON over maximise and HTCLIENT beside it (<see cref="CaptionMaxAnchorProbe"/>).
    /// </summary>
    public static int ExpectedCode(CaptionButtonKind kind, ProbeLayout layout) => (layout, kind) switch
    {
        (ProbeLayout.ClientHoles, _) => HtClient,
        (ProbeLayout.MaximiseAnchored, CaptionButtonKind.Maximize) => CaptionHitTestProbe.HtMaxButton,
        (ProbeLayout.MaximiseAnchored, _) => HtClient,
        (_, CaptionButtonKind.Close) => CaptionHitTestProbe.HtClose,
        (_, CaptionButtonKind.Maximize) => CaptionHitTestProbe.HtMaxButton,
        _ => CaptionHitTestProbe.HtMinButton,
    };

    /// <summary>
    /// True when a click on <paramref name="kind"/> must also find HTMAXBUTTON still over the maximise button:
    /// in a maximise-anchored group the close and minimise zones are plain client area, which alone proves little.
    /// </summary>
    public static bool ChecksMaximiseToo(CaptionButtonKind kind, ProbeLayout layout) =>
        layout == ProbeLayout.MaximiseAnchored && kind != CaptionButtonKind.Maximize;

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

/// <summary>What a probe found over a custom title bar's buttons, which decides what the click guard expects.</summary>
public enum ProbeLayout
{
    /// <summary>HTMINBUTTON, HTMAXBUTTON and HTCLOSE zones (<see cref="CaptionHitTestProbe"/>).</summary>
    ButtonCodes,

    /// <summary>Three HTCLIENT holes of a profiled width: HTML buttons (<see cref="CaptionHoleProbe"/>).</summary>
    ClientHoles,

    /// <summary>An HTMAXBUTTON zone between two HTCLIENT zones (<see cref="CaptionMaxAnchorProbe"/>).</summary>
    MaximiseAnchored,
}
