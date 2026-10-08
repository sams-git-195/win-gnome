using WinGnome.Core.Settings;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>The settings window: the list of pages and which one is showing.</summary>
internal sealed class SettingsWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private SettingsPageViewModel _selectedPage;
    private string? _storageProblem;

    public SettingsWindowViewModel(ShellContext context, TweakService tweaks, IDialogService dialogs)
    {
        var settings = context.Settings;
        _settings = settings;
        _storageProblem = settings.StorageProblem;
        settings.Changed += OnSettingsChanged;
        Pages =
        [
            new GeneralPageViewModel(settings, context.IsSafeMode, context.ManagesStartupEntry, ShowTaskbarTweaks),
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

    /// <summary>Shown as a warning above every page while settings can't be saved.</summary>
    public string? StorageProblem
    {
        get => _storageProblem;
        private set => SetProperty(ref _storageProblem, value);
    }

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

    /// <summary>Opens the Streamline page scrolled to its Taskbar tweaks.</summary>
    private void ShowTaskbarTweaks()
    {
        var streamline = Pages.OfType<StreamlinePageViewModel>().First();
        streamline.PendingGroup = TweakCategory.Taskbar;
        SelectedPage = streamline;
    }

    /// <summary>Commits edits still waiting on a debounce timer; call before the window closes.</summary>
    public void Flush()
    {
        foreach (var page in Pages)
        {
            page.Flush();
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings e) => StorageProblem = _settings.StorageProblem;

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }
}
