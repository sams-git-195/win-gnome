using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>The settings window: the list of pages and which one is showing.</summary>
internal sealed class SettingsWindowViewModel : ObservableObject, IDisposable
{
    private SettingsPageViewModel _selectedPage;

    public SettingsWindowViewModel(ShellContext context, TweakService tweaks, IDialogService dialogs)
    {
        var settings = context.Settings;
        Pages =
        [
            new GeneralPageViewModel(settings, context.IsSafeMode),
            new TopBarPageViewModel(settings),
            new DockPageViewModel(settings, context.Theme, context.Icons, dialogs),
            new WindowButtonsPageViewModel(settings),
            new ActivitiesPageViewModel(settings),
            new StreamlinePageViewModel(settings, tweaks, dialogs),
            new AboutPageViewModel(settings, context.Commands, dialogs),
        ];
        _selectedPage = Pages[0];
    }

    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    public SettingsPageViewModel SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value is not null && SetProperty(ref _selectedPage, value))
            {
                value.OnSelected();
            }
        }
    }

    /// <summary>Commits edits still waiting on a debounce timer; call before the window closes.</summary>
    public void Flush()
    {
        foreach (var page in Pages)
        {
            page.Flush();
        }
    }

    public void Dispose()
    {
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }
}
