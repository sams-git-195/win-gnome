using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Where the caption buttons of a window that draws its own title bar were found, relative to its visible
/// frame. Only valid while the frame keeps the size and DPI it had when probed.
/// </summary>
/// <param name="Relative">The buttons, relative to the frame's top-left corner, in physical pixels.</param>
internal readonly record struct ProbedCaption(PixelRect Relative, int FrameWidth, int FrameHeight, uint Dpi)
{
    /// <summary>True when <paramref name="frame"/> and <paramref name="dpi"/> still match the probe.</summary>
    public bool Matches(PixelRect frame, uint dpi) => frame.Width == FrameWidth && frame.Height == FrameHeight && dpi == Dpi;
}

/// <summary>
/// Asks a window that draws its own title bar where its caption buttons are, by sending WM_NCHITTEST along the
/// top-right caption strip (see <see cref="CaptionHitTestProbe"/>). Runs on a thread-pool thread: every message
/// uses SendMessageTimeout with SMTO_ABORTIFHUNG and a short timeout, and the probe gives up at the first
/// failure, so a hung app costs at most one timeout and never blocks the dispatcher.
/// </summary>
internal static class CustomCaptionProbe
{
    private const uint TimeoutMs = 50;

    /// <summary>Largest gap in DIPs allowed between the frame top and the close button's top edge.</summary>
    private const double MaxTopGap = 4;

    /// <summary>Distance in DIPs from the frame's right edge to the middle of a 46 DIP close button.</summary>
    private const double CloseCenterInset = 23;

    /// <summary>Probes <paramref name="hwnd"/> whose visible frame is <paramref name="frame"/>.</summary>
    /// <returns>The buttons, or null when the window does not report exactly the minimise/maximise/close trio.</returns>
    public static ProbedCaption? Probe(nint hwnd, PixelRect frame, uint dpi)
    {
        try
        {
            var scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var xs = CaptionHitTestProbe.RowSamples(frame, scale);
            var rowY = CaptionHitTestProbe.RowY(frame, scale);
            var rowCodes = new int[xs.Length];
            for (var i = 0; i < xs.Length; i++)
            {
                if (!TryHitTest(hwnd, xs[i], rowY, out rowCodes[i]))
                {
                    return null;
                }
            }

            var groupLeft = CaptionHitTestProbe.FindGroupLeft(xs, rowCodes, scale);
            if (groupLeft is null)
            {
                return null;
            }

            var columnX = frame.Right - (int)Math.Round(CloseCenterInset * scale, MidpointRounding.AwayFromZero);
            var columnCodes = new int[CaptionHitTestProbe.ColumnSampleCount(scale)];
            for (var i = 0; i < columnCodes.Length; i++)
            {
                if (!TryHitTest(hwnd, columnX, frame.Top + i, out columnCodes[i]))
                {
                    return null;
                }
            }

            var maxTopGap = (int)Math.Round(MaxTopGap * scale, MidpointRounding.AwayFromZero);
            if (CaptionHitTestProbe.FindCloseExtent(columnCodes, maxTopGap) is not { } extent)
            {
                return null;
            }

            var buttons = CaptionHitTestProbe.ButtonsRect(frame, groupLeft.Value, extent);
            return new ProbedCaption(buttons.Offset(-frame.Left, -frame.Top), frame.Width, frame.Height, dpi);
        }
        catch (Exception ex)
        {
            // Thread-pool thread: an exception here would otherwise be lost with the task.
            ThrottledLog.Warn("probe", $"Probing the caption buttons of 0x{hwnd:X} failed: {ex.Message}");
            return null;
        }
    }

    private static bool TryHitTest(nint hwnd, int x, int y, out int code)
    {
        // WM_NCHITTEST packs signed 16-bit screen coordinates; negative values (monitors left of or above the
        // primary) must be truncated, not sign-extended into the other half.
        var lParam = (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        var ok = NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_NCHITTEST, 0, lParam, NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out var result);
        code = (int)result;
        return ok != 0;
    }
}
