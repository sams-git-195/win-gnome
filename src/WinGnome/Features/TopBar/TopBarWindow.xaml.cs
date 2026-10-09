using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.TopBar;
using WinGnome.Features.TopBar.Popups;
using WinGnome.Features.TopBar.ViewModels;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar;

/// <summary>
/// The GNOME top bar surface. Owns its popups and appearance (colours, blur, sizes); docking and system
/// events are handled by <see cref="TopBarFeature"/>.
/// </summary>
internal sealed partial class TopBarWindow : Window
{
    private const double DotToFontRatio = 7 / BarMetrics.DefaultFontSize;

    // The logo sits a little smaller than the other icons, like the Apple logo next to menu titles.
    private const double LogoToIconRatio = 0.875;

    // Horizontal gap between neighbouring hover pills.
    private const double PillGapDip = 2;
    private const byte HoverAlpha = 0x26;
    private const byte PressedAlpha = 0x40;

    private readonly ShellContext _context;
    private readonly TopBarViewModel _viewModel;
    private readonly PopupHost _popups;
    private readonly TopBarActions _actions;
    private readonly CalendarCard _calendarCard = new();
    private readonly QuickSettingsCard _quickSettingsCard;
    private readonly LogoMenuCard _logoMenuCard = new();
    private readonly Popup _logoPopup;
    private readonly Popup _calendarPopup;
    private readonly Popup _quickSettingsPopup;
    private readonly BlurBackdrop _backdrop;
    private TopBarSettings _settings;
    private TopBarGeometry _geometry;
    private double _scale = 1;
    private bool _blurActive;
    private bool _allowClose;

    /// <param name="context">Shared services.</param>
    /// <param name="viewModel">The bar's view model (owned by the caller).</param>
    /// <param name="settings">Initial settings.</param>
    /// <param name="popups">Popup coordinator (owned by the caller, which disposes it after <see cref="Shutdown"/>).</param>
    /// <param name="backdrop">Blur backdrop (owned by the caller, which disposes it after <see cref="Shutdown"/>).</param>
    public TopBarWindow(ShellContext context, TopBarViewModel viewModel, TopBarSettings settings, PopupHost popups, BlurBackdrop backdrop)
    {
        _context = context;
        _backdrop = backdrop;
        _viewModel = viewModel;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        // The blur lives in a separate window below the bar (see BlurBackdrop), so it can follow the floating,
        // rounded body; the backdrop follows the bar wherever the AppBar, a DPI change or a hide puts it.
        _backdrop.Attach(this);
        IsVisibleChanged += (_, _) => SyncBackdrop();
        LocationChanged += (_, _) => SyncBackdrop();
        SizeChanged += (_, _) => SyncBackdrop();

        _popups = popups;
        _actions = new TopBarActions(context);
        _quickSettingsCard = new QuickSettingsCard(viewModel.Status);
        _calendarCard.ActionRequested += OnActionRequested;
        _quickSettingsCard.ActionRequested += OnActionRequested;
        _logoMenuCard.ActionRequested += OnActionRequested;
        _logoPopup = CreatePopup(_logoMenuCard, LogoButton);

        // Runs after PopupHost's own Opened handler has activated the popup: open with nothing selected, keys ready.
        _logoPopup.Opened += (_, _) => _logoMenuCard.Reset();
        _calendarPopup = CreatePopup(_calendarCard, ClockButton);
        _quickSettingsPopup = CreatePopup(_quickSettingsCard, StatusButton);
        TrayIcons.IconPressed += OnTrayIconPressed;
    }

    /// <summary>Applies the font, colours, blur and sizes. Call after the window has a handle.</summary>
    public void ApplySettings(TopBarSettings settings)
    {
        _settings = settings;
        TopBarFonts.Apply(settings.FontFamily);
        ApplySizes();
        ApplyBackground();
    }

    /// <summary>Lays the visible bar body out inside the reserved strip the AppBar was granted.</summary>
    /// <param name="geometry">Pixel geometry computed for the bar's monitor.</param>
    /// <param name="scale">DPI scale of the bar's monitor.</param>
    public void ApplyGeometry(TopBarGeometry geometry, double scale)
    {
        _geometry = geometry;
        _scale = scale;

        // Work from whole physical pixels so the body edges and rounded corners stay crisp.
        var inset = geometry.InsetPx / scale;
        Body.Margin = new Thickness(inset, inset, inset, 0);
        Body.Height = geometry.BodyHeightPx / scale;
        Body.CornerRadius = new CornerRadius(geometry.CornerRadiusPx / scale);
        ApplySizes();
        SyncBackdrop();
    }

    /// <summary>Puts the blur backdrop and then the bar at the top of the topmost band.</summary>
    public void RaiseToTop() => _backdrop.RaiseToTop();

    /// <summary>Closes any open popup (e.g. when the bar hides for a full-screen app).</summary>
    public void ClosePopups() => _popups.Close(restoreFocus: false);

    /// <summary>Closes the window for good; any other close attempt (e.g. Alt+F4) is refused.</summary>
    public void Shutdown()
    {
        _popups.Close(restoreFocus: false);
        _calendarCard.ActionRequested -= OnActionRequested;
        _quickSettingsCard.ActionRequested -= OnActionRequested;
        _logoMenuCard.ActionRequested -= OnActionRequested;
        TrayIcons.IconPressed -= OnTrayIconPressed;
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_allowClose;
        base.OnClosing(e);
    }

    /// <summary>
    /// Text, icon and pill sizes for the bar's monitor, each a whole number of device pixels: grayscale text and
    /// symbolic icons on fractional pixels look soft, and fractional pill margins round unevenly above and below.
    /// </summary>
    private void ApplySizes()
    {
        var iconPx = BarMetrics.SymbolicIconPx(_settings.FontSize, _scale);
        var iconSize = iconPx / _scale;
        Resources["BarFontSize"] = BarMetrics.SnapToDevice(_settings.FontSize, _scale);
        Resources["BarIconSize"] = iconSize;
        Resources["TopBarItemCornerRadius"] = _settings.ItemCornerRadius;

        // Pills keep a GNOME-like gap above and below that grows with the bar.
        var pillInset = BarMetrics.PillInsetPx(_settings.Height, _scale) / _scale;
        var pillGap = BarMetrics.SnapToDevice(PillGapDip, _scale);
        Resources["BarPillMargin"] = new Thickness(pillGap, pillInset, pillGap, pillInset);

        Logo.Size = BarMetrics.SnapToDevice(iconSize * LogoToIconRatio, _scale);
        WorkspaceDots.DotSize = Math.Max(4, BarMetrics.SnapToDevice(_settings.FontSize * DotToFontRatio, _scale));
        _viewModel.FocusedApp.IconSizePx = iconPx;
        _viewModel.Tray.SetIconSlot(iconPx, _scale);
    }

    private void ApplyBackground()
    {
        var background = HexColor.Parse(_settings.BackgroundColor);
        var foreground = HexColor.Parse(_settings.ForegroundColor);
        Resources["BarForeground"] = Frozen(foreground, foreground.A);
        Resources["BarHover"] = Frozen(foreground, HoverAlpha);
        Resources["BarPressed"] = Frozen(foreground, PressedAlpha);

        // The backdrop supplies only the (untinted) blur; the body draws the tint at the chosen opacity in every mode,
        // so the same opacity looks the same with or without blur. Alpha never drops to zero: fully transparent
        // pixels of a layered window are click-through, and empty bar space must still swallow clicks (and close
        // popups).
        _blurActive = _backdrop.SetEffect(_settings.Blur);
        var opacity = Math.Clamp(_settings.Opacity, 0, 1) * (background.A / 255.0);
        Body.Background = Frozen(background, (byte)Math.Max(1, Math.Round(opacity * 255)));
        SyncBackdrop();
    }

    /// <summary>
    /// Shows the blur backdrop under the visible body, or hides it (no blur, or the bar is hidden). The backdrop is
    /// used for the edge-to-edge bar too: one path for every layout, and the tint behaves identically in all of them.
    /// </summary>
    private void SyncBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!_blurActive || !IsVisible || hwnd == 0)
        {
            _backdrop.Hide();
            return;
        }

        // From the window's actual rectangle rather than the last docking result: the AppBar also repositions
        // the bar on its own (ABN_POSCHANGED).
        var window = NativeMethods.GetWindowBounds(hwnd);
        var body = _geometry.BodyRect(window.Width).Offset(window.Left, window.Top);
        if (window.IsEmpty || body.IsEmpty)
        {
            _backdrop.Hide();
            return;
        }

        _backdrop.SetBounds(body, _geometry.CornerRadiusPx, _scale);
        if (!_backdrop.IsVisible)
        {
            _backdrop.Show();

            // Shown in its old z-order slot: make sure no other topmost window sits between it and the bar.
            _backdrop.RaiseToTop();
        }
    }

    private static SolidColorBrush Frozen(HexColor color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private Popup CreatePopup(FrameworkElement card, Button anchor)
    {
        var popup = _popups.Create(card);

        // Keep the bar item highlighted while its menu is open, as GNOME does.
        popup.Opened += (_, _) => anchor.SetResourceReference(BackgroundProperty, "BarPressed");
        popup.Closed += (_, _) => anchor.ClearValue(BackgroundProperty);
        return popup;
    }

    private void OnLogoClick(object sender, RoutedEventArgs e) => _popups.Toggle(_logoPopup, LogoButton, PopupAlignment.Start);

    /// <summary>An app's tray menu is about to open: our own popups make way, without pulling focus back.</summary>
    private void OnTrayIconPressed(object? sender, EventArgs e) => _popups.Close(restoreFocus: false);

    private void OnActivitiesClick(object sender, RoutedEventArgs e)
    {
        _popups.Close(restoreFocus: false);
        _context.Commands.ShowOverview(OverviewMode.Windows);
    }

    private void OnClockClick(object sender, RoutedEventArgs e)
    {
        if (!_popups.IsOpen(_calendarPopup))
        {
            _calendarCard.ShowToday();
        }

        _popups.Toggle(_calendarPopup, ClockButton, PopupAlignment.Center);
    }

    private void OnStatusClick(object sender, RoutedEventArgs e)
    {
        if (!_popups.IsOpen(_quickSettingsPopup))
        {
            _quickSettingsCard.Reset();
            _viewModel.Status.RefreshBrightness();
        }

        _popups.Toggle(_quickSettingsPopup, StatusButton, PopupAlignment.End);
    }

    private void OnStatusWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _viewModel.Status.NudgeVolume(e.Delta);
    }

    private void OnWorkspaceWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _viewModel.Workspaces.Scroll(e.Delta);
    }

    private void OnWorkspaceDotClicked(object? sender, int index)
    {
        _popups.Close(restoreFocus: true);
        _viewModel.Workspaces.SwitchTo(index);
    }

    /// <summary>A click on empty bar space dismisses an open popup.</summary>
    private void OnBodyMouseDown(object sender, MouseButtonEventArgs e) => _popups.Close(restoreFocus: true);

    private void OnActionRequested(object? sender, TopBarAction action)
    {
        // Give focus back first so shortcuts and launched apps land where the user was; a confirmation dialog
        // takes focus itself, so do not bounce it through the previous window.
        _popups.Close(restoreFocus: !TopBarActions.AsksForConfirmation(action));

        // Run after the popup has gone, so e.g. a screenshot never captures it.
        Dispatcher.BeginInvoke(() => _actions.Execute(action), DispatcherPriority.Background);
    }
}
