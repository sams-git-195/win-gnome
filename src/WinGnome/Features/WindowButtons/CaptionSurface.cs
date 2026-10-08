using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Core.Geometry;
using WinGnome.Core.Theming;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// A small opaque window painted in a title bar's colour and kept directly above another app's window: either
/// the traffic-light buttons, or a plain mask that hides the native buttons when the circles sit on the left.
/// It never activates, never appears in Alt+Tab or the taskbar, and is positioned in physical pixels.
/// </summary>
/// <remarks>
/// <para>
/// The surface is deliberately NOT an owned window of its target. A cross-process owner relationship makes
/// Windows attach the two threads' input queues (verified on Windows 11: both threads then report the same
/// active window). With one overlay per app, every decorated app would end up sharing one input queue with
/// WinGnome's UI thread, so a single hung app could freeze input to WinGnome and every other app. Instead the
/// surface is re-stacked directly above its target whenever the target can have moved in the z-order.
/// </para>
/// <para>
/// AllowsTransparency stays false: the surface is opaque anyway (it must hide the native buttons), circles are
/// anti-aliased against the opaque background, and layered windows cost more to compose.
/// </para>
/// </remarks>
internal sealed class CaptionSurface : Window
{
    private PixelRect _bounds;
    private bool _isTopmost;
    private int _cornerRadius;
    private HexColor _fill;
    private bool _closed;

    public CaptionSurface(UIElement? content)
    {
        Title = "WinGnome window buttons";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Focusable = false;
        Content = content;
        Closed += (_, _) => _closed = true;

        Handle = new WindowInteropHelper(this).EnsureHandle();
        ShellSurface.MakeNonActivating(this, topmost: false);
        var source = HwndSource.FromHwnd(Handle);
        if (source is not null)
        {
            source.AddHook(WndProc);

            // Dozens of these tiny windows can exist at once; software rendering avoids a Direct3D swap chain per
            // window, and drawing a few anti-aliased circles on the CPU is trivial.
            source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        }

        // No DWM border or rounding: a DWM-rounded window also gets a drop shadow, which would smear over the
        // target's client area below the title bar. The rounded corner is cut with a window region instead.
        var noBorder = NativeMethods.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));
        var square = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ReapplyBounds);
    }

    /// <summary>The surface's HWND (created eagerly so it can be positioned before it is first shown).</summary>
    public nint Handle { get; }

    /// <summary>True once the window has been closed (by us, or because its HWND was destroyed).</summary>
    public bool IsClosed => _closed;

    /// <summary>Physical pixels per DIP of the monitor the surface is currently on.</summary>
    public double SurfaceScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Paints the whole surface in <paramref name="color"/> (the title bar colour behind the buttons).</summary>
    public void SetFill(HexColor color)
    {
        if (_closed || (color == _fill && Background is not null))
        {
            return;
        }

        _fill = color;
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        Background = brush;
    }

    /// <summary>Moves and resizes the surface (physical pixels) without touching its z-order or activation.</summary>
    public void Place(PixelRect bounds)
    {
        if (_closed || bounds == _bounds)
        {
            return;
        }

        var resized = bounds.Width != _bounds.Width || bounds.Height != _bounds.Height;
        _bounds = bounds;
        ApplyBounds();
        if (resized && _cornerRadius > 0)
        {
            ApplyRegion();
        }
    }

    /// <summary>
    /// Matches the rounded top-right corner of a restored Windows 11 window (radius in physical pixels, 0 for
    /// square), so the opaque surface does not show a square corner where the target's own corner is cut away.
    /// </summary>
    public void SetTopRightCornerRadius(int radius)
    {
        radius = Math.Max(0, radius);
        if (_closed || radius == _cornerRadius)
        {
            return;
        }

        _cornerRadius = radius;
        ApplyRegion();
    }

    /// <summary>
    /// Places the surface directly above <paramref name="below"/> in the z-order, matching its topmost state, so
    /// windows covering the target also cover the surface.
    /// </summary>
    public void StackAbove(nint below, bool topmost)
    {
        if (_closed)
        {
            return;
        }

        const uint Flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;
        if (topmost != _isTopmost)
        {
            // Entering or leaving the topmost band needs the explicit markers; inserting next to another window
            // does not reliably change the topmost state.
            SetWindowPos(topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST, Flags);
            _isTopmost = topmost;
        }

        var above = NativeMethods.GetWindow(below, NativeMethods.GW_HWNDPREV);
        if (IsDirectlyAbove(above))
        {
            return;
        }

        nint insertAfter;
        if (above == 0)
        {
            insertAfter = topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
        }
        else if (!topmost && (NativeMethods.GetExStyle(above) & NativeMethods.WS_EX_TOPMOST) != 0)
        {
            // The target is the highest normal window. Inserting below a topmost window would drag the surface
            // into the topmost band; HWND_TOP puts a normal window at the top of the normal band instead.
            insertAfter = NativeMethods.HWND_TOP;
        }
        else
        {
            insertAfter = above;
        }

        SetWindowPos(insertAfter, Flags);
    }

    /// <summary>Shows the surface without activating it.</summary>
    public void Reveal()
    {
        if (!_closed && !IsVisible)
        {
            Show();
        }
    }

    /// <summary>Hides the surface.</summary>
    public void Conceal()
    {
        if (!_closed && IsVisible)
        {
            Hide();
        }
    }

    /// <summary>Closes the window. Tolerates being called twice or while WPF is already tearing it down.</summary>
    public void Destroy()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            Close();
        }
        catch (InvalidOperationException ex)
        {
            // Close() throws while the window is already closing; it is going away either way.
            ThrottledLog.Warn("surface-close", $"Closing a window-buttons surface failed: {ex.Message}");
        }

        _closed = true;
    }

    /// <summary>
    /// True when this surface already sits above the target with only invisible windows in between, where
    /// <paramref name="above"/> is the window immediately above the target. Apps keep
    /// invisible helper windows (IME windows, which they own) right above themselves; Windows keeps owned windows
    /// above their owner, so such windows end up between the target and the surface after every restack and
    /// must not trigger yet another SetWindowPos.
    /// </summary>
    private bool IsDirectlyAbove(nint above)
    {
        const int MaxInvisibleWindows = 16;
        for (var i = 0; i < MaxInvisibleWindows && above != 0; i++)
        {
            if (above == Handle)
            {
                return true;
            }

            if (NativeMethods.IsWindowVisible(above))
            {
                return false;
            }

            above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV);
        }

        return false;
    }

    /// <summary>Cuts the top-right corner along a circle of <see cref="_cornerRadius"/>, or removes the cut.</summary>
    private void ApplyRegion()
    {
        if (_closed || _bounds.IsEmpty)
        {
            return;
        }

        if (_cornerRadius == 0)
        {
            _ = NativeMethods.SetWindowRgn(Handle, 0, true);
            return;
        }

        // A rounded rectangle that extends past the left and bottom edges, so only its top-right corner falls
        // inside the window. Regions exclude their right and bottom edges, hence the +1.
        var diameter = _cornerRadius * 2;
        var region = NativeMethods.CreateRoundRectRgn(-diameter, 0, _bounds.Width + 1, _bounds.Height + diameter, diameter, diameter);
        if (region == 0)
        {
            ThrottledLog.Warn("surface-region", "CreateRoundRectRgn failed for a window-buttons surface");
            return;
        }

        // On success the system owns the region.
        if (NativeMethods.SetWindowRgn(Handle, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
            ThrottledLog.Warn("surface-region", $"SetWindowRgn on a window-buttons surface failed: error {Marshal.GetLastPInvokeError()}");
        }
    }

    private void ReapplyBounds()
    {
        if (!_closed && !_bounds.IsEmpty)
        {
            ApplyBounds();
        }
    }

    private void ApplyBounds() =>
        SetWindowPos(0, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER, move: true);

    private void SetWindowPos(nint insertAfter, uint flags, bool move = false)
    {
        var ok = move
            ? NativeMethods.SetWindowPos(Handle, insertAfter, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, flags)
            : NativeMethods.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0, flags);
        if (!ok)
        {
            ThrottledLog.Warn("surface-pos", $"SetWindowPos on a window-buttons surface failed: error {Marshal.GetLastPInvokeError()}");
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DPICHANGED && lParam != 0 && !_bounds.IsEmpty)
        {
            // WPF resizes the window to the rectangle suggested in lParam when it crosses to a monitor with another
            // DPI. Our bounds are already exact physical pixels, so substitute them before WPF reads the suggestion
            // (this hook runs before WPF's own handler) to avoid a frame at the wrong size. The deferred
            // ReapplyBounds after DpiChanged is the backstop should WPF ever position the window differently.
            Marshal.StructureToPtr(RECT.From(_bounds), lParam, fDeleteOld: false);
        }

        return 0;
    }
}
