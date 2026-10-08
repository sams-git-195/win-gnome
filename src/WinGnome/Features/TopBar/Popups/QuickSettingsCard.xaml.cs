using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinGnome.Features.TopBar.ViewModels;

namespace WinGnome.Features.TopBar.Popups;

/// <summary>
/// GNOME-style quick settings: volume and brightness sliders, shortcut tiles, battery state, and the system row with
/// screenshot, settings, lock and an expandable power menu.
/// </summary>
internal sealed partial class QuickSettingsCard : UserControl
{
    private readonly SystemStatusViewModel _status;

    public QuickSettingsCard(SystemStatusViewModel status)
    {
        _status = status;
        DataContext = status;
        InitializeComponent();
    }

    /// <summary>Raised when a tile or button asks for an action.</summary>
    public event EventHandler<TopBarAction>? ActionRequested;

    /// <summary>Collapses the power submenu; called every time the card opens.</summary>
    public void Reset() => PowerMenu.Visibility = Visibility.Collapsed;

    private void OnMuteClick(object sender, RoutedEventArgs e) => _status.ToggleMute();

    private void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _status.NudgeVolume(e.Delta);
    }

    private void OnBrightnessWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _status.NudgeBrightness(e.Delta);
    }

    private void OnPowerClick(object sender, RoutedEventArgs e) =>
        PowerMenu.Visibility = PowerMenu.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TopBarAction action })
        {
            ActionRequested?.Invoke(this, action);
        }
    }
}
