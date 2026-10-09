using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Controls.TrafficLights;

/// <summary>
/// Turns one of WinGnome's own windows into a GNOME-style header-bar window: the Windows caption goes (through
/// <see cref="WindowChrome"/>, keeping DWM's shadow, rounded corners and resize borders) and the window's own round
/// buttons are drawn in the first row of its root grid, in the user's window-button style. The style follows
/// <c>WindowButtons</c> settings live, even while the Window buttons feature itself is off.
/// </summary>
/// <remarks>
/// The maximise circle answers WM_NCHITTEST with HTMAXBUTTON so that hovering it opens Windows 11 Snap Layouts.
/// WPF never sees the mouse there, so this class relays hover and click from the non-client messages.
/// </remarks>
internal sealed class HeaderBarWindow : IDisposable
{
    /// <summary>Height of the header bar in DIPs (the root grid's first row must match it).</summary>
    public const double CaptionHeight = 46;

    private readonly Window _window;
    private readonly Grid _root;
    private readonly SettingsService _settings;
    private readonly bool _closeOnly;
    private readonly WindowChrome _chrome;
    private readonly TrafficLightButtonsView _buttons = new();
    private HwndSource? _source;
    private CaptionOverlayLayout? _layout;
    private bool _maximisePressed;
    private bool _disposed;

    private HeaderBarWindow(Window window, Grid root, SettingsService settings, bool closeOnly)
    {
        _window = window;
        _root = root;
        _settings = settings;
        _closeOnly = closeOnly;
        _chrome = new WindowChrome
        {
            CaptionHeight = CaptionHeight,

            // One pixel of DWM frame keeps the window's shadow and Windows 11 rounded corners.
            GlassFrameThickness = new Thickness(1),
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            CornerRadius = default,
            UseAeroCaptionButtons = false,
        };

        try
        {
            WindowChrome.SetWindowChrome(window, _chrome);
            Grid.SetRow(_buttons, 0);
            Grid.SetColumnSpan(_buttons, Math.Max(1, root.ColumnDefinitions.Count));
            Panel.SetZIndex(_buttons, 1);
            _buttons.HorizontalAlignment = HorizontalAlignment.Left;
            _buttons.VerticalAlignment = VerticalAlignment.Top;
            WindowChrome.SetIsHitTestVisibleInChrome(_buttons, true);
            _buttons.IsKeyboardNavigable = true;
            root.Children.Add(_buttons);

            _buttons.ButtonClicked += OnButtonClicked;
            root.SizeChanged += OnRootSizeChanged;
            window.SourceInitialized += OnSourceInitialized;
            window.Activated += OnActivationChanged;
            window.Deactivated += OnActivationChanged;
            window.StateChanged += OnStateChanged;
            window.DpiChanged += OnDpiChanged;
            window.Closed += OnClosed;
            settings.Changed += OnSettingsChanged;

            // Attached after the handle exists (SourceInitialized has already fired): hook up now.
            if (new WindowInteropHelper(window).Handle != 0)
            {
                OnSourceInitialized(window, EventArgs.Empty);
            }
        }
        catch
        {
            // Don't leave handlers on the shared SettingsService pointing at a half-built header bar.
            Dispose();
            throw;
        }
    }
    /// <summary>
    /// Gives <paramref name="window"/> a header bar. Call from the window's constructor, after InitializeComponent.
    /// The header bar lives in the first row of <paramref name="root"/>, the window's content, which must be
    /// <see cref="CaptionHeight"/> DIPs tall. Text and controls in that row are caption area (drag, double-click
    /// to maximise, right-click for the system menu) unless marked IsHitTestVisibleInChrome. Released when the
    /// window closes.
    /// </summary>
    /// <param name="closeOnly">Show only the close circle (dialogs).</param>
    public static HeaderBarWindow Attach(Window window, Grid root, SettingsService settings, bool closeOnly)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(settings);
        return new HeaderBarWindow(window, root, settings, closeOnly);
    }

    /// <summary>Raised after the buttons move (resize, settings change), so titles can keep clear of them.</summary>
    public event EventHandler? ButtonsArranged;

    /// <summary>The button group's area in DIPs relative to the root grid, or <see cref="Rect.Empty"/> when hidden.</summary>
    public Rect ButtonsBounds { get; private set; } = Rect.Empty;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _window.Closed -= OnClosed;
        _window.DpiChanged -= OnDpiChanged;
        _window.StateChanged -= OnStateChanged;
        _window.Deactivated -= OnActivationChanged;
        _window.Activated -= OnActivationChanged;
        _window.SourceInitialized -= OnSourceInitialized;
        _root.SizeChanged -= OnRootSizeChanged;
        _buttons.ButtonClicked -= OnButtonClicked;
        _root.Children.Remove(_buttons);
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    /// <summary>
    /// Self-test: asks the window what is under the maximise circle's centre, the way Windows does before showing
    /// Snap Layouts. Null when there is nothing to check (no maximise circle, or not laid out yet).
    /// </summary>
    /// <returns>True when WM_NCHITTEST answers HTMAXBUTTON there.</returns>
    public bool? ProbeSnapLayoutsHitTest()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        var maximise = _layout?.Buttons.FirstOrDefault(b => b.Kind == CaptionButtonKind.Maximize);
        if (hwnd == 0 || _layout is null || maximise is null || !CanMaximize || !NativeMethods.GetWindowRect(hwnd, out var window))
        {
            return null;
        }

        var bar = _root.TranslatePoint(default, _window);
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        var x = window.Left + (int)Math.Round((bar.X + _layout.Bounds.Left + maximise.CenterX) * scale);
        var y = window.Top + (int)Math.Round((bar.Y + _layout.Bounds.Top + maximise.CenterY) * scale);
        var point = (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        return NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCHITTEST, 0, point) == NativeMethods.HTMAXBUTTON;
    }

    private bool CanMaximize => !_closeOnly && _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    private bool CanMinimize => !_closeOnly && _window.ResizeMode != ResizeMode.NoResize;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // WindowChrome answers WM_NCHITTEST in its own hook. This hook must see the message first, or the maximise
        // circle reads as HTCLIENT and Snap Layouts never shows; the self-test checks it does
        // (ProbeSnapLayoutsHitTest), because the order depends on WPF internals.
        var handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        if (_closeOnly)
        {
            RemoveMinMaxBoxes(handle);
        }

        UpdateMaximisedMargin();
        UpdateLayout();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            UpdateLayout();
        }
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateMaximisedMargin();
        UpdateLayout();
    }

    private void OnSettingsChanged(object? sender, AppSettings e) => UpdateLayout();

    private void OnActivationChanged(object? sender, EventArgs e) => _buttons.IsWindowActive = _window.IsActive;

    private void OnStateChanged(object? sender, EventArgs e) => UpdateMaximisedMargin();

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void UpdateLayout()
    {
        var settings = _settings.Current.WindowButtons;
        var layout = CaptionButtonLayout.ComputeForHeaderBar(_root.ActualWidth, CaptionHeight, settings, _closeOnly);
        _layout = layout;
        if (layout is null)
        {
            _buttons.Visibility = Visibility.Collapsed;
            SetButtonsBounds(Rect.Empty);
            return;
        }

        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        _buttons.Margin = new Thickness(layout.Bounds.Left, layout.Bounds.Top, 0, 0);
        _buttons.Width = layout.Bounds.Width;
        _buttons.Height = layout.Bounds.Height;
        _buttons.SetSurfaceScale(scale);
        _buttons.SetAppearance(TrafficLightPalette.For(settings), settings);
        _buttons.SetLayout(layout, scale, CanMinimize, CanMaximize);
        _buttons.IsWindowActive = _window.IsActive;
        _buttons.Visibility = Visibility.Visible;
        var bounds = layout.Bounds;
        SetButtonsBounds(new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }

    private void SetButtonsBounds(Rect bounds)
    {
        if (bounds != ButtonsBounds)
        {
            ButtonsBounds = bounds;
            ButtonsArranged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// A maximised window extends past the monitor by its sizing frame and padded border on every side; pad the
    /// content by the same amount so the header bar and its buttons stay on screen, and grow the caption to match.
    /// Measured at the window's own DPI: SystemParameters only knows the primary monitor's.
    /// </summary>
    private void UpdateMaximisedMargin()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        var margin = default(Thickness);
        if (_window.WindowState == WindowState.Maximized && handle != 0)
        {
            var dpi = NativeMethods.GetDpiForWindow(handle);
            var scale = dpi / 96.0;
            var padded = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);
            var x = CaptionButtonGeometry.MaximisedOverhangDips(NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXFRAME, dpi), padded, scale);
            var y = CaptionButtonGeometry.MaximisedOverhangDips(NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYFRAME, dpi), padded, scale);
            margin = new Thickness(x, y, x, y);
        }

        _root.Margin = margin;
        _chrome.CaptionHeight = CaptionHeight + margin.Top;
        _buttons.IsTargetMaximized = _window.WindowState == WindowState.Maximized;
    }

    /// <summary>
    /// Dialogs have only a close circle, so drop the minimise and maximise boxes too: no double-click maximise, no
    /// Snap Layouts, no Maximise in the system menu.
    /// </summary>
    private static void RemoveMinMaxBoxes(nint handle)
    {
        var style = NativeMethods.GetStyle(handle);
        var trimmed = style & ~(NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_MINIMIZEBOX);
        if (trimmed != style && NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_STYLE, (nint)trimmed) == 0)
        {
            Log.Warn($"Could not remove the minimise and maximise boxes from the dialog 0x{handle:X}: error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }
    }

    private void OnButtonClicked(CaptionButtonKind kind)
    {
        switch (kind)
        {
            case CaptionButtonKind.Close:
                SystemCommands.CloseWindow(_window);
                break;
            case CaptionButtonKind.Minimize:
                SystemCommands.MinimizeWindow(_window);
                break;
            default:
                ToggleMaximise();
                break;
        }
    }

    private void ToggleMaximise()
    {
        if (_window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(_window);
        }
        else
        {
            SystemCommands.MaximizeWindow(_window);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                if (IsOverMaximise(hwnd, lParam))
                {
                    handled = true;
                    return NativeMethods.HTMAXBUTTON;
                }

                break;

            case NativeMethods.WM_NCMOUSEMOVE:
                // Left unhandled: DefWindowProc's handling is what opens Snap Layouts.
                if (wParam == NativeMethods.HTMAXBUTTON)
                {
                    TrackNonClientLeave(hwnd);
                    _buttons.SetNonClientPointer(CaptionButtonKind.Maximize, _maximisePressed);
                }
                else
                {
                    ClearNonClientPointer();
                }

                break;

            case NativeMethods.WM_NCMOUSELEAVE:
                ClearNonClientPointer();
                break;

            // Handled so DefWindowProc doesn't run its own modal tracking of the (invisible) native button. There is no
            // mouse capture: pressing the circle and dragging off it cancels the click (WM_NCMOUSEMOVE elsewhere or
            // WM_NCMOUSELEAVE clears the press), and releasing outside the window does nothing.
            case NativeMethods.WM_NCLBUTTONDOWN when wParam == NativeMethods.HTMAXBUTTON:
                _maximisePressed = true;
                _buttons.SetNonClientPointer(CaptionButtonKind.Maximize, pressed: true);
                handled = true;
                break;

            // A double-click is down, up, double-click, up. The first up already toggled; swallowing the second press
            // keeps a double-click from toggling straight back (and DefWindowProc from maximising on its own).
            case NativeMethods.WM_NCLBUTTONDBLCLK when wParam == NativeMethods.HTMAXBUTTON:
                handled = true;
                break;

            case NativeMethods.WM_NCLBUTTONUP when wParam == NativeMethods.HTMAXBUTTON:
                var click = _maximisePressed;
                _maximisePressed = false;
                _buttons.SetNonClientPointer(CaptionButtonKind.Maximize, pressed: false);
                handled = true;
                if (click)
                {
                    ToggleMaximise();
                }

                break;
        }

        return 0;
    }

    private bool IsOverMaximise(nint hwnd, nint lParam)
    {
        if (_layout is null || !CanMaximize || !NativeMethods.GetWindowRect(hwnd, out var window))
        {
            return false;
        }

        // Screen coordinates, signed: monitors left of or above the primary have negative ones.
        var x = (short)(lParam & 0xFFFF);
        var y = (short)((lParam >> 16) & 0xFFFF);

        // With WindowChrome the client area is the whole window rectangle, so window-relative is client-relative.
        var bar = _root.TranslatePoint(default, _window);
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        var kind = CaptionButtonHitTest.FindInHeaderBar(_layout, x - window.Left, y - window.Top, scale, bar.X, bar.Y);
        return kind == CaptionButtonKind.Maximize;
    }

    private void ClearNonClientPointer()
    {
        _maximisePressed = false;
        _buttons.SetNonClientPointer(null, pressed: false);
    }

    /// <summary>Asks for WM_NCMOUSELEAVE, which Windows only sends to windows that request it.</summary>
    private static void TrackNonClientLeave(nint hwnd)
    {
        var request = new TRACKMOUSEEVENT
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = NativeMethods.TME_LEAVE | NativeMethods.TME_NONCLIENT,
            hwndTrack = hwnd,
        };
        if (!NativeMethods.TrackMouseEvent(ref request))
        {
            Log.Warn($"TrackMouseEvent(TME_NONCLIENT) failed for the header bar: error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }
    }
}
