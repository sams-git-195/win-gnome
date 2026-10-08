using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.TopBar.Popups;

/// <summary>
/// Opens the bar's popup cards one at a time and dismisses them GNOME-style: on Escape, on a click outside,
/// when another popup is opened, or when the same bar item is clicked again.
/// </summary>
/// <remarks>
/// WPF's own <c>StaysOpen=false</c> dismissal relies on mouse capture, but the bar is a no-activate window, so
/// our thread is never the foreground thread and "background" capture only sees clicks over our own windows:
/// clicks on other applications would never close the popup. Instead the opened popup is explicitly activated
/// (allowed, because our process just received the click) and closes when it loses activation. That also gives
/// it keyboard focus for Escape and slider arrow keys. Clicks on the bar itself never change activation (the
/// bar is no-activate), so the bar's buttons decide whether to toggle or switch popups.
/// </remarks>
internal sealed class PopupHost : IDisposable
{
    /// <summary>Transparent space around each card that its drop shadow is drawn into (DIPs).</summary>
    public const double ShadowMargin = 14;

    /// <summary>Gap between the bar item and the card (DIPs).</summary>
    private const double AnchorGap = 6;

    private readonly WindowTracker _windows;
    private Popup? _current;
    private HwndSource? _source;
    private nint _previousForeground;

    public PopupHost(WindowTracker windows)
    {
        _windows = windows;
        _windows.ForegroundChanged += OnForegroundChanged;
    }

    /// <summary>Wraps <paramref name="content"/> in a themed, shadowed card inside a transparent popup managed by this host.</summary>
    public Popup Create(FrameworkElement content)
    {
        var card = new Border
        {
            Style = (Style)Application.Current.FindResource("CardBorder"),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = ShadowMargin, ShadowDepth = 2, Direction = 270, Opacity = 0.35 },
        };
        var frame = new Border
        {
            Padding = new Thickness(ShadowMargin),
            Child = card,
            Focusable = true,
            FocusVisualStyle = null,
        };
        frame.PreviewKeyDown += OnPreviewKeyDown;

        var popup = new Popup
        {
            Child = frame,
            AllowsTransparency = true,
            StaysOpen = true,
            Placement = PlacementMode.Bottom,
            PopupAnimation = PopupAnimation.Fade,
        };
        popup.Opened += OnOpened;
        return popup;
    }

    public bool IsOpen(Popup popup) => ReferenceEquals(_current, popup);

    /// <summary>Opens <paramref name="popup"/> under <paramref name="anchor"/>, or closes it if it is already open.</summary>
    public void Toggle(Popup popup, FrameworkElement anchor, PopupAlignment alignment)
    {
        if (IsOpen(popup))
        {
            Close(restoreFocus: true);
            return;
        }

        if (_current is null)
        {
            _previousForeground = NativeMethods.GetForegroundWindow();
        }
        else
        {
            // Switching popups: keep the window that was in front before the first one opened.
            Close(restoreFocus: false);
        }

        popup.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        popup.PlacementTarget = anchor;
        popup.HorizontalOffset = PopupPlacement.HorizontalOffset(alignment, anchor.ActualWidth, popup.Child.DesiredSize.Width, ShadowMargin);
        popup.VerticalOffset = AnchorGap - ShadowMargin;
        _current = popup;
        popup.IsOpen = true;
    }

    /// <summary>
    /// Closes the open popup. With <paramref name="restoreFocus"/>, and if the popup still owns the foreground,
    /// focus goes back to the window that was active before it opened (as after closing a GNOME menu).
    /// </summary>
    public void Close(bool restoreFocus)
    {
        if (_current is not { } popup)
        {
            return;
        }

        _current = null;
        var popupOwnedForeground = _source is not null && NativeMethods.GetForegroundWindow() == _source.Handle;
        _source?.RemoveHook(PopupWndProc);
        _source = null;
        popup.IsOpen = false;

        if (restoreFocus && popupOwnedForeground && _previousForeground != 0 && NativeMethods.IsWindow(_previousForeground)
            && !NativeMethods.SetForegroundWindow(_previousForeground))
        {
            Log.Warn($"Could not give focus back to 0x{_previousForeground:X} after closing a popup");
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup || !IsOpen(popup) || PresentationSource.FromVisual(popup.Child) is not HwndSource source)
        {
            return;
        }

        _source = source;
        source.AddHook(PopupWndProc);
        if (!NativeMethods.SetForegroundWindow(source.Handle))
        {
            // Dismissal then falls back to foreground-change notifications (see OnForegroundChanged).
            Log.Warn($"Could not activate the popup (error {Marshal.GetLastPInvokeError()})");
        }

        Keyboard.Focus(popup.Child);
    }

    private nint PopupWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_ACTIVATE && (wParam & 0xFFFF) == NativeMethods.WA_INACTIVE && _source?.Handle == hwnd)
        {
            // Never tear the window down inside its own activation message.
            var popup = _current;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (popup is not null && IsOpen(popup))
                {
                    Close(restoreFocus: false);
                }
            });
        }

        return 0;
    }

    /// <summary>
    /// Safety net for when activation was refused: any other window coming to the front dismisses the popup.
    /// WinEvents arrive asynchronously, so this compares against the live foreground window rather than the
    /// event's (possibly stale) one, and ignores events until the popup has actually been shown.
    /// </summary>
    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        if (_current is not null && _source is not null && NativeMethods.GetForegroundWindow() != _source.Handle)
        {
            Close(restoreFocus: false);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(restoreFocus: true);
        }
    }

    public void Dispose()
    {
        _windows.ForegroundChanged -= OnForegroundChanged;
        Close(restoreFocus: false);
    }
}
