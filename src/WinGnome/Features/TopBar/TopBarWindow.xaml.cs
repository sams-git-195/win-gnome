using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    // GNOME's panel icons are 16 px next to ~13.5 px text; keep that ratio when the font size changes.
    private const double IconToFontRatio = 16 / 13.5;
    private const double DotToFontRatio = 7 / 13.5;
    private const byte HoverAlpha = 0x26;
    private const byte PressedAlpha = 0x40;

    private readonly ShellContext _context;
    private readonly TopBarViewModel _viewModel;
    private readonly PopupHost _popups;
    private readonly TopBarActions _actions;
    private readonly CalendarCard _calendarCard = new();
    private readonly QuickSettingsCard _quickSettingsCard;
    private readonly Popup _calendarPopup;
    private readonly Popup _quickSettingsPopup;
    private TopBarSettings _settings;
    private TopBarGeometry _geometry;
    private int _windowWidthPx;
    private bool _blurActive;
    private bool _clipped;
    private bool _allowClose;

    /// <param name="context">Shared services.</param>
    /// <param name="viewModel">The bar's view model (owned by the caller).</param>
    /// <param name="settings">Initial settings.</param>
    /// <param name="popups">Popup coordinator (owned by the caller, which disposes it after <see cref="Shutdown"/>).</param>
    public TopBarWindow(ShellContext context, TopBarViewModel viewModel, TopBarSettings settings, PopupHost popups)
    {
        _context = context;
        _viewModel = viewModel;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        _popups = popups;
        _actions = new TopBarActions(context);
        _quickSettingsCard = new QuickSettingsCard(viewModel.Status);
        _calendarCard.ActionRequested += OnActionRequested;
        _quickSettingsCard.ActionRequested += OnActionRequested;
        _calendarPopup = CreatePopup(_calendarCard, ClockButton);
        _quickSettingsPopup = CreatePopup(_quickSettingsCard, StatusButton);
    }

    /// <summary>Applies colours, blur and sizes. Call after the window has a handle.</summary>
    public void ApplySettings(TopBarSettings settings)
    {
        _settings = settings;
        ApplySizes();
        ApplyBackground();
    }

    /// <summary>Lays the visible bar body out inside the reserved strip the AppBar was granted.</summary>
    /// <param name="geometry">Pixel geometry computed for the bar's monitor.</param>
    /// <param name="windowWidthPx">Width of the granted AppBar rectangle in physical pixels.</param>
    /// <param name="scale">DPI scale of the bar's monitor.</param>
    public void ApplyGeometry(TopBarGeometry geometry, int windowWidthPx, double scale)
    {
        _geometry = geometry;
        _windowWidthPx = windowWidthPx;

        // Work from whole physical pixels so the body edges and rounded corners stay crisp.
        var inset = geometry.InsetPx / scale;
        Body.Margin = new Thickness(inset, inset, inset, 0);
        Body.Height = geometry.BodyHeightPx / scale;
        Body.CornerRadius = new CornerRadius(geometry.CornerRadiusPx / scale);

        // Pills keep a GNOME-like gap above and below that grows with the bar.
        var pillInset = Math.Max(2, Math.Round(_settings.Height * 0.1));
        Resources["BarPillMargin"] = new Thickness(2, pillInset, 2, pillInset);

        _viewModel.FocusedApp.IconSizePx = (int)Math.Round(IconSize * scale);
        UpdateBlurClip();
    }

    /// <summary>Closes any open popup (e.g. when the bar hides for a full-screen app).</summary>
    public void ClosePopups() => _popups.Close(restoreFocus: false);

    /// <summary>Closes the window for good; any other close attempt (e.g. Alt+F4) is refused.</summary>
    public void Shutdown()
    {
        _popups.Close(restoreFocus: false);
        _calendarCard.ActionRequested -= OnActionRequested;
        _quickSettingsCard.ActionRequested -= OnActionRequested;
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_allowClose;
        base.OnClosing(e);
    }

    private double IconSize => Math.Round(_settings.FontSize * IconToFontRatio);

    private void ApplySizes()
    {
        Resources["BarFontSize"] = _settings.FontSize;
        Resources["BarIconSize"] = IconSize;
        WorkspaceDots.DotSize = Math.Max(4, Math.Round(_settings.FontSize * DotToFontRatio));
    }

    private void ApplyBackground()
    {
        var background = HexColor.Parse(_settings.BackgroundColor);
        var foreground = HexColor.Parse(_settings.ForegroundColor);
        Resources["BarForeground"] = Frozen(foreground, foreground.A);
        Resources["BarHover"] = Frozen(foreground, HoverAlpha);
        Resources["BarPressed"] = Frozen(foreground, PressedAlpha);

        var opacity = Math.Clamp(_settings.Opacity, 0, 1) * (background.A / 255.0);
        if (_settings.Blur != BlurEffect.None)
        {
            _blurActive = WindowBlur.Apply(this, _settings.Blur, background with { A = 255 }, opacity);
        }
        else if (_blurActive)
        {
            WindowBlur.Apply(this, BlurEffect.None, background, 0);
            _blurActive = false;
        }

        // With blur the tint comes from the accent colour. Alpha never drops to zero: fully transparent pixels of a
        // layered window are click-through, and empty bar space must still swallow clicks (and close popups).
        var alpha = _blurActive ? (byte)1 : (byte)Math.Max(1, Math.Round(opacity * 255));
        Body.Background = Frozen(background, alpha);
        UpdateBlurClip();
    }

    /// <summary>The blur fills the whole window, so a floating or rounded bar clips it to the visible body.</summary>
    private void UpdateBlurClip()
    {
        if (_blurActive && _geometry.IsInset && _windowWidthPx > 0)
        {
            var body = _geometry.BodyRect(_windowWidthPx);
            WindowBlur.ClipToRoundedRect(this, body.Left, body.Top, body.Right, body.Bottom, _geometry.CornerRadiusPx);
            _clipped = true;
        }
        else if (_clipped)
        {
            WindowBlur.ClearClip(this);
            _clipped = false;
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
