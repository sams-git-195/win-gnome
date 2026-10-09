using WinGnome.Core.Settings;
using WinGnome.Features.TopBar.Tray;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>
/// Root view model of one bar: the shared clock, workspace and status view models, this bar's focused app and tray
/// icons, plus the settings-driven visibility toggles.
/// </summary>
internal sealed class TopBarViewModel : ObservableObject, IDisposable
{
    private TopBarSettings _settings;

    public TopBarViewModel(ShellContext context, TopBarServices services, TopBarSettings settings)
    {
        _settings = settings;
        Clock = services.Clock;
        Workspaces = services.Workspaces;
        Status = services.Status;
        FocusedApp = new FocusedAppViewModel(context.Windows, context.Apps, context.Icons);
        Tray = new TrayBarIcons(services.Tray);
    }

    public ClockViewModel Clock { get; }

    public WorkspacesViewModel Workspaces { get; }

    public FocusedAppViewModel FocusedApp { get; }

    public SystemStatusViewModel Status { get; }

    public TrayBarIcons Tray { get; }

    public bool ShowLogoMenu => _settings.ShowLogoMenu;

    public bool ShowActivitiesButton => _settings.ShowActivitiesButton;

    public bool ShowWorkspaceIndicator => _settings.ShowWorkspaceIndicator;

    public bool ShowFocusedAppName => _settings.ShowFocusedAppName;

    /// <summary>Per-bar toggles only; the shared view models get their settings from <see cref="TopBarServices"/>.</summary>
    public void ApplySettings(TopBarSettings settings)
    {
        _settings = settings;
        OnPropertyChanged(nameof(ShowLogoMenu));
        OnPropertyChanged(nameof(ShowActivitiesButton));
        OnPropertyChanged(nameof(ShowWorkspaceIndicator));
        OnPropertyChanged(nameof(ShowFocusedAppName));
    }

    /// <summary>Disposes only what this bar owns; the shared view models belong to <see cref="TopBarServices"/>.</summary>
    public void Dispose()
    {
        FocusedApp.Dispose();
        Tray.Dispose();
    }
}
