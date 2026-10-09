using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>How a probe found a window's caption buttons (for logging and the click guard).</summary>
internal enum ProbeSource
{
    /// <summary>The top-level window answered HTMINBUTTON/HTMAXBUTTON/HTCLOSE (Claude desktop, VS Code).</summary>
    HitTest,

    /// <summary>A child window answered the button codes (Windows App SDK's non-client input window).</summary>
    ChildHitTest,

    /// <summary>HTCLIENT holes matching a built-in profile of an app with HTML buttons (GitHub Desktop).</summary>
    Profile,

    /// <summary>An HTMAXBUTTON zone between HTCLIENT zones in a Windows App SDK window (Dia).</summary>
    MaximiseAnchored,
}

/// <summary>
/// Where the caption buttons of a window that draws its own title bar were found, relative to its visible
/// frame. Only valid while the frame keeps the size and DPI it had when probed.
/// </summary>
/// <param name="Relative">The buttons, relative to the frame's top-left corner, in physical pixels.</param>
internal readonly record struct ProbedCaption(PixelRect Relative, int FrameWidth, int FrameHeight, uint Dpi, ProbeSource Source)
{
    /// <summary>What the click guard expects over each button.</summary>
    public ProbeLayout Layout => Source switch
    {
        ProbeSource.Profile => ProbeLayout.ClientHoles,
        ProbeSource.MaximiseAnchored => ProbeLayout.MaximiseAnchored,
        _ => ProbeLayout.ButtonCodes,
    };

    /// <summary>True when the buttons were found by a rule that the web-buttons setting enables.</summary>
    public bool NeedsWebButtonsSetting => Layout != ProbeLayout.ButtonCodes;

    /// <summary>True when <paramref name="frame"/> and <paramref name="dpi"/> still match the probe.</summary>
    public bool Matches(PixelRect frame, uint dpi) => frame.Width == FrameWidth && frame.Height == FrameHeight && dpi == Dpi;

    /// <summary>True when <paramref name="other"/> was probed at the same frame size and DPI.</summary>
    public bool IsSameSize(ProbedCaption other) => other.FrameWidth == FrameWidth && other.FrameHeight == FrameHeight && other.Dpi == Dpi;
}

/// <summary>
/// Asks a window that draws its own title bar where its caption buttons are, by sending WM_NCHITTEST along the
/// top-right caption strip (see <see cref="CaptionHitTestProbe"/> and <see cref="CaptionHoleProbe"/>). Each
/// point is asked of the deepest visible child window under it, falling back to its parents while they answer
/// HTTRANSPARENT, which is how Windows routes real mouse input. Runs on a thread-pool thread: every message uses
/// SendMessageTimeout with SMTO_ABORTIFHUNG and a short timeout, and the probe gives up at the first failure, so
/// a hung app costs at most one timeout and never blocks the dispatcher.
/// </summary>
internal static class CustomCaptionProbe
{
    private const uint TimeoutMs = 50;

    /// <summary>Distance in DIPs from the frame's right edge to the middle of a 46 DIP close button.</summary>
    private const double CloseCenterInset = 23;

    /// <summary>Longest a whole probe may take before it gives up (a slow but not hung app).</summary>
    private const long BudgetMs = 500;

    /// <summary>How many child levels to descend; real windows nest two or three deep.</summary>
    private const int MaxDepth = 8;

    /// <summary>
    /// Probes <paramref name="hwnd"/> whose visible frame is <paramref name="frame"/>. Thread-pool thread: never
    /// throws and never logs (failures go through ThrottledLog, which is UI-thread only); a failure comes back in
    /// <paramref name="error"/>.
    /// </summary>
    /// <param name="webButtonWidth">
    /// For an app with a built-in profile (and the setting on), the width in DIPs of its HTML buttons: HTCLIENT
    /// holes of that width are then accepted when the window reports no button codes. Otherwise null.
    /// </param>
    /// <param name="allowMaximiseAnchor">
    /// True for a Windows App SDK window (with the setting on) that may report only its maximise button: an
    /// HTMAXBUTTON zone between HTCLIENT zones is then accepted (<see cref="CaptionMaxAnchorProbe"/>).
    /// </param>
    /// <returns>The buttons, or null when the window does not report exactly the minimise/maximise/close trio.</returns>
    public static ProbedCaption? Probe(
        nint hwnd, PixelRect frame, uint dpi, double? webButtonWidth, bool allowMaximiseAnchor, out string? error)
    {
        error = null;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var rowY = CaptionHitTestProbe.RowY(frame, scale);
            var xs = CaptionHitTestProbe.RowSamples(frame, scale);
            if (!TryHitTestRow(hwnd, xs, rowY, clock, out var rowCodes, out var fromChild))
            {
                return null;
            }

            var columnX = frame.Right - (int)Math.Round(CloseCenterInset * scale, MidpointRounding.AwayFromZero);
            var maxTopGap = CaptionHitTestProbe.MaxTopGapPixels(scale);
            if (CaptionHitTestProbe.FindGroupLeft(xs, rowCodes, scale) is { } groupLeft)
            {
                return TryHitTestColumn(hwnd, frame, columnX, scale, clock, out var columnCodes)
                    && CaptionHitTestProbe.FindCloseExtent(columnCodes, maxTopGap) is { } extent
                    ? Result(frame, groupLeft, extent, dpi, fromChild ? ProbeSource.ChildHitTest : ProbeSource.HitTest)
                    : null;
            }

            if (allowMaximiseAnchor && CaptionMaxAnchorProbe.FindGroup(xs, rowCodes, scale) is { } anchored)
            {
                return TryHitTestColumn(hwnd, frame, anchored.MaximiseMiddle, scale, clock, out var maxColumn)
                    && CaptionHitTestProbe.FindZoneExtent(maxColumn, CaptionHitTestProbe.HtMaxButton, maxTopGap) is { } maxExtent
                    ? Result(frame, anchored.GroupLeft, maxExtent, dpi, ProbeSource.MaximiseAnchored)
                    : null;
            }

            if (webButtonWidth is not { } width)
            {
                return null;
            }

            // The drag strips between HTML buttons are 1 px wide: sample every pixel.
            var holeXs = CaptionHoleProbe.RowSamples(frame, scale);
            if (!TryHitTestRow(hwnd, holeXs, rowY, clock, out var holeCodes, out _)
                || CaptionHoleProbe.FindClientHoles(holeXs, holeCodes, scale, width) is not { } holesLeft)
            {
                return null;
            }

            return TryHitTestColumn(hwnd, frame, columnX, scale, clock, out var holeColumn)
                && CaptionHoleProbe.FindHoleExtent(holeColumn, maxTopGap) is { } holeExtent
                ? Result(frame, holesLeft, holeExtent, dpi, ProbeSource.Profile)
                : null;
        }
        catch (Exception ex)
        {
            // Thread-pool thread: an exception here would otherwise be lost with the task.
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// The click guard's single question: what <paramref name="hwnd"/> (or its deepest child there) answers at
    /// a screen point now. Thread-pool thread; null when it did not answer in time.
    /// </summary>
    public static int? HitTestAt(nint hwnd, int x, int y)
    {
        try
        {
            return TryHitTestDeep(hwnd, x, y, System.Diagnostics.Stopwatch.StartNew(), out var code, out _) ? code : null;
        }
        catch (Exception)
        {
            // Treated like no answer: the click is dropped and the window probed again.
            return null;
        }
    }

    private static ProbedCaption Result(PixelRect frame, int groupLeft, (int Top, int Bottom) extent, uint dpi, ProbeSource source)
    {
        var buttons = CaptionHitTestProbe.ButtonsRect(frame, groupLeft, extent);
        return new ProbedCaption(buttons.Offset(-frame.Left, -frame.Top), frame.Width, frame.Height, dpi, source);
    }

    private static bool TryHitTestRow(
        nint hwnd, int[] xs, int y, System.Diagnostics.Stopwatch clock, out int[] codes, out bool fromChild)
    {
        codes = new int[xs.Length];
        fromChild = false;
        for (var i = 0; i < xs.Length; i++)
        {
            if (!TryHitTestDeep(hwnd, xs[i], y, clock, out codes[i], out var byChild))
            {
                return false;
            }

            fromChild |= byChild;
        }

        return true;
    }

    /// <summary>One-pixel samples down the column through the close button, from the frame top.</summary>
    private static bool TryHitTestColumn(
        nint hwnd, PixelRect frame, int x, double scale, System.Diagnostics.Stopwatch clock, out int[] codes)
    {
        codes = new int[CaptionHitTestProbe.ColumnSampleCount(scale)];
        for (var i = 0; i < codes.Length; i++)
        {
            if (!TryHitTestDeep(hwnd, x, frame.Top + i, clock, out codes[i], out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Hit-tests the deepest visible, non-transparent child of <paramref name="top"/> under the point, then its
    /// parents in turn while they answer HTTRANSPARENT. Chromium's render-widget child is WS_EX_TRANSPARENT, so
    /// Electron apps still answer from their top-level window.
    /// </summary>
    private static bool TryHitTestDeep(
        nint top, int x, int y, System.Diagnostics.Stopwatch clock, out int code, out bool byChild)
    {
        Span<nint> chain = stackalloc nint[MaxDepth + 1];
        chain[0] = top;
        var depth = 0;
        while (depth < MaxDepth)
        {
            // ChildWindowFromPointEx takes client coordinates and only looks at direct children: no messages.
            var point = new POINT { X = x, Y = y };
            if (!NativeMethods.ScreenToClient(chain[depth], ref point))
            {
                break;
            }

            var child = NativeMethods.ChildWindowFromPointEx(
                chain[depth], point, NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPTRANSPARENT);
            if (child == 0 || child == chain[depth])
            {
                break;
            }

            chain[++depth] = child;
        }

        code = NativeMethods.HTTRANSPARENT;
        byChild = false;
        for (var level = depth; level >= 0; level--)
        {
            if (!TryHitTest(chain[level], x, y, clock, out code))
            {
                return false;
            }

            if (code != NativeMethods.HTTRANSPARENT)
            {
                byChild = level > 0;
                return true;
            }
        }

        return true;
    }

    /// <summary>One hit test; false when it timed out, failed, or the probe has used up its time budget.</summary>
    private static bool TryHitTest(nint hwnd, int x, int y, System.Diagnostics.Stopwatch clock, out int code)
    {
        code = 0;
        if (clock.ElapsedMilliseconds > BudgetMs)
        {
            return false;
        }

        // MAKELPARAM: two 16-bit screen coordinates (negative on monitors left of or above the primary), each
        // truncated to its own half, with the upper half of a 64-bit lParam zero.
        var lParam = (nint)(uint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        var ok = NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_NCHITTEST, 0, lParam, NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out var result);
        code = (int)result;
        return ok != 0;
    }
}
