using System.Collections.ObjectModel;
using System.Windows.Media;
using WinGnome.Core.Search;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>One installed app in the picker. The icon loads when the row is first drawn.</summary>
internal sealed class AppPickerItem
{
    private const int IconPixels = 32;

    private readonly Lazy<ImageSource?> _icon;

    public AppPickerItem(AppEntry entry, IIconProvider icons)
    {
        Entry = entry;
        _icon = new Lazy<ImageSource?>(() => icons.GetAppIcon(entry.LaunchId, IconPixels));
    }

    public AppEntry Entry { get; }

    public string Name => Entry.Name;

    public ImageSource? Icon => _icon.Value;
}

/// <summary>A searchable list of installed apps that are not pinned yet.</summary>
internal sealed class AppPickerViewModel : ObservableObject, IDisposable
{
    private const int MaxResults = 1000;

    private readonly IAppCatalog _catalog;
    private readonly IIconProvider _icons;
    private readonly HashSet<string> _hidden;
    private string _query = "";
    private AppPickerItem? _selected;

    public AppPickerViewModel(IAppCatalog catalog, IIconProvider icons, IEnumerable<string> hiddenLaunchIds)
    {
        _catalog = catalog;
        _icons = icons;
        _hidden = new HashSet<string>(hiddenLaunchIds, StringComparer.OrdinalIgnoreCase);
        Search();
        catalog.Changed += OnCatalogChanged;
    }

    public ObservableCollection<AppPickerItem> Results { get; } = [];

    /// <summary>Text typed into the search box.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? ""))
            {
                Search();
            }
        }
    }

    public AppPickerItem? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>Message shown instead of the list while it is empty, or null.</summary>
    public string? EmptyMessage =>
        Results.Count > 0 ? null
        : _catalog.Apps.Count == 0 ? "Loading your apps…"
        : "No matching apps.";

    /// <summary>The selected app; the window reads it after the user confirmed.</summary>
    public AppEntry? Chosen => Selected?.Entry;

    public void Dispose() => _catalog.Changed -= OnCatalogChanged;

    private void OnCatalogChanged(object? sender, EventArgs e) => Search();

    private void Search()
    {
        var candidates = _catalog.Apps.Where(a => !_hidden.Contains(a.LaunchId));
        var matches = FuzzyMatcher.Rank(candidates, a => a.Name, _query, MaxResults);
        Results.Clear();
        foreach (var match in matches)
        {
            Results.Add(new AppPickerItem(match, _icons));
        }

        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
