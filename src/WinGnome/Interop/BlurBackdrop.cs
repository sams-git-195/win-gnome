using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

/// <summary>
/// A blur/acrylic material for a shell surface whose visible body is smaller than its window or has rounded
/// corners (the dock, a floating top bar). It lives in a separate window stacked directly below the surface.
/// </summary>
/// <remarks>
/// <para>
/// The accent blur (<see cref="WindowBlur"/>) always fills its window's whole rectangle, and on current Windows 11
/// builds DWM ignores both window regions and DWM blur-behind regions for it (verified on build 26200). So a
/// surface cannot carry the blur itself without it spilling into transparent margins and square corners. Instead
/// the material lives in this window, which the surface moves to its body (<see cref="SetBounds"/>) whenever the
/// body changes, and DWM rounds its corners (see <see cref="BackdropPlacement"/> for the fixed-radius inset).
/// </para>
/// <para>
/// The material is untinted: the surface draws its tint over it at the chosen opacity, so the same opacity looks
/// the same with or without blur, and the tinted edge hides the thin unblurred rim the inset leaves.
/// </para>
/// <para>
/// The window is deliberately not layered (no AllowsTransparency): DWM only rounds the corners of non-layered
/// windows. Its WPF content is transparent so the accent shows through. The surface is owned by this window
/// (<see cref="Attach"/>), which keeps it above its backdrop in the z-order without extra bookkeeping. The
/// backdrop never gets clicks: the surface above covers the same rectangle with hit-testable content.
/// </para>
/// </remarks>
internal sealed class BlurBackdrop : IDisposable
{
    private readonly BackdropWindow _window;
    private Window? _content;
    private PixelRect _bounds;
    private BackdropCorners? _corners;

    /// <param name="title">Window title, for diagnostics (Spy++, logs).</param>
    public BlurBackdrop(string title)
    {
        _window = new BackdropWindow(title, this);
    }

    /// <summary>True once <see cref="SetEffect"/> has applied a material.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while the backdrop window is shown.</summary>
    public bool IsVisible => _window.IsVisible;

    private nint Handle => new WindowInteropHelper(_window).EnsureHandle();

    /// <summary>
    /// Makes <paramref name="content"/> (the surface drawn over the material) an owned window of the backdrop,
    /// so it always stays directly above it. Call before the content is shown.
    /// </summary>
    public void Attach(Window content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        new WindowInteropHelper(content).Owner = Handle;
    }

    /// <summary>
    /// Applies <paramref name="effect"/>, or removes the material for <see cref="BlurEffect.None"/>. Returns
    /// <see cref="IsActive"/>: false when there is no material, including when the system refused (the surface
    /// then draws a plain colour and should keep the backdrop hidden).
    /// </summary>
    public bool SetEffect(BlurEffect effect)
    {
        // The accent needs a handle; the backdrop may not have been shown or placed yet.
        _ = Handle;
        IsActive = effect != BlurEffect.None && WindowBlur.Apply(_window, effect);
        if (!IsActive)
        {
            WindowBlur.Apply(_window, BlurEffect.None);
        }

        return IsActive;
    }

    /// <summary>
    /// Moves the backdrop under a body occupying <paramref name="bodyScreenPx"/> (screen pixels) with corners of
    /// <paramref name="cornerRadiusPx"/>, on a monitor with DPI scale <paramref name="dpiScale"/>. Repeated calls
    /// with the same values are free, so this can run on every layout pass and animation frame.
    /// </summary>
    public void SetBounds(PixelRect bodyScreenPx, double cornerRadiusPx, double dpiScale)
    {
        var hwnd = Handle;
        var placement = BackdropPlacement.Compute(bodyScreenPx, cornerRadiusPx, dpiScale);
        if (placement.Corners != _corners)
        {
            _corners = placement.Corners;
            var preference = placement.Corners switch
            {
                BackdropCorners.Round => NativeMethods.DWMWCP_ROUND,
                BackdropCorners.Small => NativeMethods.DWMWCP_ROUNDSMALL,
                _ => NativeMethods.DWMWCP_DONOTROUND,
            };
            if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) < 0)
            {
                // Windows 10 has no rounded corners; the blur is simply square there.
                Log.Warn($"{_window.Title}: could not set the corner preference");
            }
        }

        if (placement.Bounds == _bounds || placement.Bounds.IsEmpty)
        {
            return;
        }

        _bounds = placement.Bounds;
        NativeMethods.SetWindowPos(hwnd, 0, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>Shows the backdrop (without activating it). Does nothing when it is already shown.</summary>
    public void Show()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }
    }

    /// <summary>Hides the backdrop. Does nothing when it is already hidden.</summary>
    public void Hide()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
    }

    /// <summary>
    /// Puts the backdrop and then the attached surface at the top of the topmost band (e.g. after a full-screen
    /// app pushed itself above them). Raising only the surface could leave other topmost windows between the two,
    /// showing through the tint unblurred.
    /// </summary>
    public void RaiseToTop()
    {
        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
        if (_content is not null && new WindowInteropHelper(_content).Handle is var content && content != 0)
        {
            NativeMethods.SetWindowPos(content, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
        }
    }

    /// <summary>Closes the backdrop. Close the attached surface first: Win32 destroys owned windows with their owner.</summary>
    public void Dispose()
    {
        _content = null;
        _window.Close();
    }

    private sealed class BackdropWindow : Window
    {
        private readonly BlurBackdrop _owner;

        public BackdropWindow(string title, BlurBackdrop owner)
        {
            _owner = owner;
            Title = title;
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

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            ShellSurface.MakeNonActivating(this, topmost: true);

            // Windows 11 draws a 1px border around rounded windows; the surface draws its own outline (or none).
            var hwnd = new WindowInteropHelper(this).Handle;
            var noBorder = NativeMethods.DWMWA_COLOR_NONE;
            _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

            if (HwndSource.FromHwnd(hwnd) is { } source)
            {
                // A non-layered WPF window clears to opaque black unless its composition target is transparent.
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                source.AddHook(WndProc);
            }
        }

        private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_DPICHANGED && lParam != 0 && !_owner._bounds.IsEmpty)
            {
                // Crossing to a monitor with another DPI, WPF would resize the window to the rectangle suggested in
                // lParam, and SetBounds would then skip an unchanged body. Our bounds are exact physical pixels, so
                // substitute them before WPF reads the suggestion (this hook runs before WPF's own handler).
                Marshal.StructureToPtr(RECT.From(_owner._bounds), lParam, fDeleteOld: false);
            }

            return 0;
        }
    }
}
