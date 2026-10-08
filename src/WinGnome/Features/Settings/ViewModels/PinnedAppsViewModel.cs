using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>One pinned app in the editor.</summary>
internal sealed class PinnedAppItem(PinnedApp app, IIconProvider icons, bool canMoveUp, bool canMoveDown)
{
    private const int IconPixels = 32;

    private readonly Lazy<ImageSource?> _icon = new(() => icons.GetAppIcon(app.LaunchId, IconPixels));

    public string Name => app.Name;

    public string LaunchId => app.LaunchId;

    public ImageSource? Icon => _icon.Value;

    public bool CanMoveUp { get; } = canMoveUp;

    public bool CanMoveDown { get; } = canMoveDown;
}

/// <summary>Edits the dock's pinned apps: reorder, remove, add from the installed apps, reset to the defaults.</summary>
internal sealed class PinnedAppsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly IIconProvider _icons;
    private readonly IDialogService _dialogs;

    public PinnedAppsViewModel(SettingsService settings, IIconProvider icons, IDialogService dialogs)
    {
        _settings = settings;
        _icons = icons;
        _dialogs = dialogs;
        MoveUpCommand = new RelayCommand(item => Move(item, -1));
        MoveDownCommand = new RelayCommand(item => Move(item, 1));
        RemoveCommand = new RelayCommand(Remove);
        AddCommand = new RelayCommand(Add);
        ResetCommand = new RelayCommand(Reset);
        Rebuild();
        settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<PinnedAppItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand ResetCommand { get; }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var current = settings.Dock.PinnedApps;
        var unchanged = current.Count == Items.Count
            && current.Select((app, i) => app.LaunchId == Items[i].LaunchId && app.Name == Items[i].Name).All(same => same);
        if (!unchanged)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Items.Clear();
        var apps = _settings.Current.Dock.PinnedApps;
        for (var i = 0; i < apps.Count; i++)
        {
            Items.Add(new PinnedAppItem(apps[i], _icons, canMoveUp: i > 0, canMoveDown: i < apps.Count - 1));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Edit(Action<List<PinnedApp>> change) =>
        _settings.Update(s =>
        {
            var apps = s.Dock.PinnedApps.ToList();
            change(apps);
            s.Dock.PinnedApps = apps;
        });

    private void Move(object? parameter, int offset)
    {
        if (parameter is PinnedAppItem item)
        {
            var index = Items.IndexOf(item);
            Edit(apps => PinnedAppsEditor.Move(apps, index, offset));
        }
    }

    private void Remove(object? parameter)
    {
        if (parameter is PinnedAppItem item)
        {
            var index = Items.IndexOf(item);
            Edit(apps => apps.RemoveAt(index));
        }
    }

    private void Add()
    {
        var hidden = _settings.Current.Dock.PinnedApps.Select(a => a.LaunchId).ToList();
        var chosen = _dialogs.PickApp(hidden);
        if (chosen is not null)
        {
            Edit(apps => PinnedAppsEditor.TryAdd(apps, new PinnedApp { Name = chosen.Name, LaunchId = chosen.LaunchId }));
        }
    }

    private void Reset()
    {
        if (_dialogs.Confirm("Reset pinned apps?", "The dock goes back to its default set of pinned apps.", "Reset", isDestructive: true))
        {
            Edit(apps =>
            {
                apps.Clear();
                apps.AddRange(DockSettings.DefaultPinnedApps());
            });
        }
    }
}
