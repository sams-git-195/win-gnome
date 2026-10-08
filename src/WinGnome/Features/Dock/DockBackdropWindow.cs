using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// Window directly behind the dock body that carries the blur/acrylic material.
/// </summary>
/// <remarks>
/// <para>
/// The accent blur always fills its window's whole rectangle. The dock window cannot carry it because it is
/// larger than the body (the gap to the screen edge and the headroom magnified icons grow into). Clipping with
/// a window region (<see cref="WindowBlur.ClipToRoundedRect"/>) or a DWM blur-behind region does not help either:
/// on current Windows 11 builds DWM ignores both for the accent blur (verified on build 26200). So the material
/// lives in this separate window, which is moved and resized to match the body every time the body changes
/// (layout, magnification, slide animation), and DWM rounds its corners. The material is untinted: the dock
/// window draws the tint over it (see <see cref="PlaceOver"/> for why).
/// </para>
/// <para>
/// The window is deliberately not layered (no AllowsTransparency): DWM only rounds the corners of non-layered
/// windows. Its WPF content is transparent so the accent shows through. The dock window is owned by this one,
/// which keeps the dock above its backdrop in the z-order without extra bookkeeping. It never gets clicks: the
/// dock window above covers the same rectangle with hit-testable content.
/// </para>
/// </remarks>
internal sealed class DockBackdropWindow : Window
{
    /// <summary>Corner radii (DIP) below which DWM's small rounding, or none, is the closer match.</summary>
    private const double SmallCornerThreshold = 6;
    private const double SquareCornerThreshold = 2;

    /// <summary>DWM's corner radii for DWMWCP_ROUND and DWMWCP_ROUNDSMALL, in DIPs.</summary>
    private const double SystemRadius = 8;
    private const double SmallSystemRadius = 4;

    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    private PixelRect _bounds;
    private int _cornerPreference;

    public DockBackdropWindow()
    {
        Title = "WinGnome Dock Backdrop";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>The window handle (created on first use).</summary>
    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Applies the material; returns false when the system refused (the dock then draws a plain colour).</summary>
    public bool ApplyEffect(BlurEffect effect, HexColor tint, double opacity) => WindowBlur.Apply(this, effect, tint, opacity);

    /// <summary>Removes the material.</summary>
    public void ClearEffect() => WindowBlur.Apply(this, BlurEffect.None, default, 0);

    /// <summary>
    /// Moves the backdrop under a body occupying <paramref name="bodyBounds"/> (screen pixels) with
    /// <paramref name="cornerRadiusDip"/> corners. DWM only offers fixed corner radii, so the backdrop is inset
    /// just enough that its rounded corners stay inside the body's; the dock tints the whole body itself, so the
    /// thin unblurred rim this leaves along the edges is not noticeable. Repeated calls with the same values are
    /// free, so this can run on every layout pass and animation frame.
    /// </summary>
    public void PlaceOver(PixelRect bodyBounds, double cornerRadiusDip, double dpiScale)
    {
        var hwnd = Handle;
        var (preference, systemRadiusDip) = cornerRadiusDip < SquareCornerThreshold ? (DWMWCP_DONOTROUND, 0.0)
            : cornerRadiusDip < SmallCornerThreshold ? (DWMWCP_ROUNDSMALL, SmallSystemRadius)
            : (DWMWCP_ROUND, SystemRadius);
        if (preference != _cornerPreference)
        {
            _cornerPreference = preference;
            if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) < 0)
            {
                // Windows 10 has no rounded corners; the blur is simply square there.
                Log.Warn("Dock: could not set the backdrop's corner preference");
            }
        }

        // A corner of radius r inset by k stays inside a corner of radius R when k >= (R - r)(1 - 1/sqrt 2).
        var insetDip = Math.Max(0, cornerRadiusDip - systemRadiusDip) * (1 - (1 / Math.Sqrt(2)));
        var inset = (int)Math.Ceiling(insetDip * dpiScale);
        var bounds = bodyBounds.Inflate(-inset, -inset);
        if (bounds == _bounds || bounds.IsEmpty)
        {
            return;
        }

        _bounds = bounds;
        NativeMethods.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ShellSurface.MakeNonActivating(this, topmost: true);

        // Windows 11 draws a 1px border around rounded windows; the dock draws its own outline.
        var hwnd = new WindowInteropHelper(this).Handle;
        var noBorder = DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

        // A non-layered WPF window clears to opaque black unless its composition target is transparent.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }
}
