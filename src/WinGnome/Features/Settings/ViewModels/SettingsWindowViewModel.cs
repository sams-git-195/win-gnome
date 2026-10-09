using System.IO;
using System.Windows.Data;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Settings;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Panels;
using WinGnome.Features.Settings.Panels.About;
using WinGnome.Features.Settings.Panels.Appearance;
using WinGnome.Features.Settings.Panels.DateAndTime;
using WinGnome.Features.Settings.Panels.Displays;
using WinGnome.Features.Settings.Panels.Keyboard;
using WinGnome.Features.Settings.Panels.Mouse;
using WinGnome.Features.Settings.Panels.Multitasking;
using WinGnome.Features.Settings.Panels.Power;
using WinGnome.Features.Settings.Panels.Sound;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>
/// The settings window: a GNOME Settings style sidebar of panels (Windows settings, links to Windows Settings and
/// WinGnome's own pages), search over it, and the panel that is showing.
/// </summary>
internal sealed class SettingsWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly ShellContext _context;
    private readonly Dictionary<string, int> _searchRank = new(StringComparer.Ordinal);
    private SidebarEntry _selectedEntry;
    private string _searchText = "";
    private string? _storageProblem;

    public SettingsWindowViewModel(ShellContext context, TweakService tweaks, IDialogService dialogs)
    {
        _context = context;
        var settings = context.Settings;
        _settings = settings;
        _storageProblem = settings.StorageProblem;
        settings.Changed += OnSettingsChanged;

        var panels = new SystemPanelContext(settings, context.Dispatcher, context.IsSafeMode, OpenLink, settings.Directory);
        var pages = new Dictionary<string, SettingsPageViewModel>(StringComparer.Ordinal)
        {
            [PanelIds.Displays] = new DisplaysPanelViewModel(panels),
            [PanelIds.Sound] = new SoundPanelViewModel(panels),
            [PanelIds.Power] = new PowerPanelViewModel(panels),
            [PanelIds.Mouse] = new MousePanelViewModel(panels),
            [PanelIds.Keyboard] = new KeyboardPanelViewModel(panels),
            [PanelIds.Appearance] = new AppearancePanelViewModel(panels),
            [PanelIds.Multitasking] = new MultitaskingPanelViewModel(panels),
            [PanelIds.DateTime] = new DateTimePanelViewModel(panels),
            [PanelIds.About] = new AboutPanelViewModel(panels),
            [PanelIds.General] = new GeneralPageViewModel(settings, context.IsSafeMode, context.ManagesStartupEntry, ShowTaskbarTweaks),
            [PanelIds.TopBar] = new TopBarPageViewModel(settings),
            [PanelIds.Dock] = new DockPageViewModel(settings, context.Theme, context.Icons, dialogs),
            [PanelIds.WindowButtons] = new WindowButtonsPageViewModel(settings),
            [PanelIds.Activities] = new ActivitiesPageViewModel(settings),
            [PanelIds.Streamline] = new StreamlinePageViewModel(settings, tweaks, dialogs),
            [PanelIds.AboutWinGnome] = new AboutPageViewModel(settings, context.Commands, dialogs),
        };

        Entries = SettingsPanelCatalog.All
            .Select(panel => new SidebarEntry(panel, pages.GetValueOrDefault(panel.Id)))
            .Where(entry => entry.IsLink || entry.Page is not null)
            .ToList();
        Pages = Entries.Where(e => e.Page is not null).Select(e => e.Page!).ToList();

        Sidebar = new ListCollectionView(Entries.ToList()) { Filter = IsVisibleInSidebar };
        Sidebar.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SidebarEntry.GroupTitle)));

        // WinGnome's General page is cheap to show; the system panels read Windows only when chosen.
        _selectedEntry = Entries.First(e => e.Id == PanelIds.General);
        _selectedEntry.Page!.OnSelected();
    }

    /// <summary>Every sidebar entry in catalogue order.</summary>
    public IReadOnlyList<SidebarEntry> Entries { get; }

    /// <summary>The pages of the native entries, in sidebar order.</summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>The sidebar: grouped, or filtered and ranked while searching.</summary>
    public ListCollectionView Sidebar { get; }

    /// <summary>Shown as a warning above every page while settings can't be saved.</summary>
    public string? StorageProblem
    {
        get => _storageProblem;
        private set => SetProperty(ref _storageProblem, value);
    }

    /// <summary>
    /// The sidebar entry whose page is showing. Links and null (the list clears its selection while it filters) are
    /// ignored.
    /// </summary>
    public SidebarEntry SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (value is null || value == _selectedEntry)
            {
                return;
            }

            if (value.Page is null)
            {
                // Links are never the current page; the view opens them with OpenLinkEntry on a click or Enter.
                return;
            }

            var previous = _selectedEntry;
            _selectedEntry = value;
            previous.Page?.OnDeselected();
            value.Page.OnSelected();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPage));
        }
    }

    public SettingsPageViewModel SelectedPage => _selectedEntry.Page!;

    /// <summary>Sidebar search text. While it is set the sidebar shows matching panels, best first, without groups.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? ""))
            {
                return;
            }

            ApplySearch();
        }
    }

    /// <summary>Shows a panel by id (see <see cref="PanelIds"/>). Unknown ids are logged and ignored.</summary>
    public void ShowPanel(string panelId)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == panelId);
        if (entry is null)
        {
            Log.Warn($"Settings: no panel \"{panelId}\"");
            return;
        }

        if (entry.IsLink)
        {
            OpenLinkEntry(entry);
        }
        else
        {
            SelectedEntry = entry;
        }
    }

    /// <summary>Opens a link entry's Windows Settings page (or control panel); the current page stays.</summary>
    public void OpenLinkEntry(SidebarEntry entry)
    {
        if (entry.IsLink)
        {
            OpenLink(entry.Panel.LinkUri!);
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
        _settings.Changed -= OnSettingsChanged;
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }

    private void ApplySearch()
    {
        _searchRank.Clear();
        var results = SettingsPanelCatalog.Search(_searchText);
        for (var i = 0; i < results.Count; i++)
        {
            _searchRank[results[i].Id] = i;
        }

        using (Sidebar.DeferRefresh())
        {
            var searching = !string.IsNullOrWhiteSpace(_searchText);
            Sidebar.GroupDescriptions.Clear();
            if (searching)
            {
                Sidebar.CustomSort = Comparer<SidebarEntry>.Create((a, b) => _searchRank[a.Id].CompareTo(_searchRank[b.Id]));
            }
            else
            {
                Sidebar.CustomSort = null;
                Sidebar.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SidebarEntry.GroupTitle)));
            }
        }

        // The list drops its selection when the selected entry is filtered out; show it again when it comes back.
        OnPropertyChanged(nameof(SelectedEntry));
    }

    private bool IsVisibleInSidebar(object item) =>
        string.IsNullOrWhiteSpace(_searchText) || (item is SidebarEntry entry && _searchRank.ContainsKey(entry.Id));

    /// <summary>Opens a Windows Settings URI, or a control panel program from System32 (colorcpl.exe).</summary>
    private void OpenLink(string link)
    {
        var target = link.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(link)
            ? Path.Combine(Environment.SystemDirectory, link)
            : link;
        if (!_context.Launcher.Launch(target))
        {
            Log.Warn($"Settings: could not open {target}");
        }
    }

    /// <summary>Opens the Streamline page scrolled to its Taskbar tweaks.</summary>
    private void ShowTaskbarTweaks()
    {
        var streamline = Pages.OfType<StreamlinePageViewModel>().First();
        streamline.PendingGroup = TweakCategory.Taskbar;
        ShowPanel(PanelIds.Streamline);
    }

    private void OnSettingsChanged(object? sender, AppSettings e) => StorageProblem = _settings.StorageProblem;
}
