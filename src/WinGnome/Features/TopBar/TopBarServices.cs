using WinGnome.Core.Settings;
using WinGnome.Features.TopBar.Popups;
using WinGnome.Features.TopBar.Tray;
using WinGnome.Features.TopBar.ViewModels;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar;

/// <summary>
/// Everything the bars on all monitors share, built once: the clock, workspaces and system status (one set of
/// timers, audio and registry callbacks however many bars there are), the notification area, and one popup host so
/// only one popup is open at a time across all bars.
/// </summary>
internal sealed class TopBarServices : IDisposable
{
    public TopBarServices(ShellContext context, TopBarSettings settings)
    {
        Clock = new ClockViewModel(context.Dispatcher, settings);
        Workspaces = new WorkspacesViewModel(context.Dispatcher);
        Status = new SystemStatusViewModel(context.Dispatcher, settings.ShowBatteryPercentage);
        Tray = new TrayModel(context.Dispatcher, context.Commands.Quit, uri => context.Launcher.Launch(uri));
        Tray.SetEnabled(settings.ShowTrayIcons);
        Popups = new PopupHost(context.Windows);
        Logo = new LogoProvider(context.Dispatcher);
        Logo.ApplySettings(settings);
    }

    public ClockViewModel Clock { get; }

    public WorkspacesViewModel Workspaces { get; }

    public SystemStatusViewModel Status { get; }

    public TrayModel Tray { get; }

    public PopupHost Popups { get; }

    /// <summary>Resolves the logo mark and decodes a custom image once, shared by every bar.</summary>
    public LogoProvider Logo { get; }

    public void ApplySettings(TopBarSettings settings)
    {
        Clock.ApplySettings(settings);
        Status.ApplySettings(settings.ShowBatteryPercentage);
        Tray.SetEnabled(settings.ShowTrayIcons);
        Logo.ApplySettings(settings);
    }

    public void Dispose()
    {
        Logo.Dispose();
        Popups.Dispose();
        Tray.Dispose();
        Status.Dispose();
        Workspaces.Dispose();
        Clock.Dispose();
    }
}
