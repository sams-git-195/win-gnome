using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.ViewModels;

/// <summary>Root view model of the bar: the per-area view models plus the settings-driven visibility toggles.</summary>
internal sealed class TopBarViewModel : ObservableObject, IDisposable
{
    private TopBarSettings _settings;

    public TopBarViewModel(ShellContext context, TopBarSettings settings)
    {
        _settings = settings;
        Clock = new ClockViewModel(context.Dispatcher, settings);
        Workspaces = new WorkspacesViewModel(context.Dispatcher);
        FocusedApp = new FocusedAppViewModel(context.Windows, context.Apps, context.Icons);
        Status = new SystemStatusViewModel(context.Dispatcher, settings.ShowBatteryPercentage);
    }

    public ClockViewModel Clock { get; }

    public WorkspacesViewModel Workspaces { get; }

    public FocusedAppViewModel FocusedApp { get; }

    public SystemStatusViewModel Status { get; }

    public bool ShowActivitiesButton => _settings.ShowActivitiesButton;

    public bool ShowWorkspaceIndicator => _settings.ShowWorkspaceIndicator;

    public bool ShowFocusedAppName => _settings.ShowFocusedAppName;

    public void ApplySettings(TopBarSettings settings)
    {
        _settings = settings;
        Clock.ApplySettings(settings);
        Status.ApplySettings(settings.ShowBatteryPercentage);
        OnPropertyChanged(nameof(ShowActivitiesButton));
        OnPropertyChanged(nameof(ShowWorkspaceIndicator));
        OnPropertyChanged(nameof(ShowFocusedAppName));
    }

    public void Dispose()
    {
        Clock.Dispose();
        Workspaces.Dispose();
        FocusedApp.Dispose();
        Status.Dispose();
    }
}
