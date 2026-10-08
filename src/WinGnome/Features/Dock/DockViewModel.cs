using System.Collections.ObjectModel;
using System.Globalization;
using WinGnome.Core.Dock;
using WinGnome.Core.Settings;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Dock;

/// <summary>
/// The dock's item list. Entry instances are kept across refreshes (keyed by pin or app identity) and the
/// collection is edited in place, so the view keeps its containers: no flicker, no lost hover state.
/// </summary>
internal sealed class DockViewModel
{
    /// <summary>Shell parsing name of the Recycle Bin; also accepted by the launcher and the icon provider.</summary>
    public const string RecycleBinLaunchId = "shell:RecycleBinFolder";

    private readonly IIconProvider _icons;
    private readonly IAppCatalog _catalog;
    private readonly Dictionary<string, DockAppEntry> _appEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly DockSeparatorEntry _separator;
    private readonly DockActionEntry _recycleBin;
    private readonly DockActionEntry _showApplications;

    public DockViewModel(IIconProvider icons, IAppCatalog catalog)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _separator = new DockSeparatorEntry(Appearance);
        _recycleBin = new DockActionEntry(Appearance, DockActionKind.RecycleBin, "Recycle Bin");
        _showApplications = new DockActionEntry(Appearance, DockActionKind.ShowApplications, "Show Applications");
    }

    public DockAppearance Appearance { get; } = new();

    /// <summary>Everything shown in the dock, in display order.</summary>
    public ObservableCollection<DockEntry> Entries { get; } = [];

    /// <summary>App entries only (pinned first, then running), in display order. Super+N indexes into this.</summary>
    public IReadOnlyList<DockAppEntry> AppEntries { get; private set; } = [];

    /// <summary>Number of full-size slots (apps and buttons).</summary>
    public int CellCount => Entries.Count(e => e is not DockSeparatorEntry);

    /// <summary>Number of separators.</summary>
    public int SeparatorCount => Entries.Count(e => e is DockSeparatorEntry);

    /// <summary>Launch ids of the pinned entries in their current display order (after a drag preview).</summary>
    public IReadOnlyList<string> PinnedLaunchIds =>
        Entries.OfType<DockAppEntry>().Where(e => e.IsPinned && e.App.LaunchId is not null).Select(e => e.App.LaunchId!).ToList();

    /// <summary>Brings the entries in line with <paramref name="apps"/> and the button settings.</summary>
    /// <param name="apps">Dock model from <see cref="DockModelBuilder"/>.</param>
    /// <param name="settings">Dock settings (which built-in buttons to show).</param>
    /// <param name="iconSizePx">Icon size to load, in physical pixels.</param>
    public void Update(IReadOnlyList<DockApp> apps, DockSettings settings, int iconSizePx)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(settings);

        var desired = new List<DockEntry>(apps.Count + 3);
        var appEntries = new List<DockAppEntry>(apps.Count);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sawPinned = false;
        var separatorAdded = false;

        foreach (var app in apps)
        {
            var key = app.IsPinned ? "pin:" + app.LaunchId : "run:" + app.Identity;
            if (!keys.Add(key))
            {
                continue;
            }

            if (!app.IsPinned && sawPinned && !separatorAdded)
            {
                desired.Add(_separator);
                separatorAdded = true;
            }

            sawPinned |= app.IsPinned;
            if (!_appEntries.TryGetValue(key, out var entry))
            {
                entry = new DockAppEntry(Appearance, key);
                _appEntries.Add(key, entry);
            }

            entry.Update(app);
            LoadIcon(entry, iconSizePx);
            desired.Add(entry);
            appEntries.Add(entry);
        }

        foreach (var stale in _appEntries.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _appEntries.Remove(stale);
        }

        if (settings.ShowRecycleBin)
        {
            LoadIcon(_recycleBin, iconSizePx);
            desired.Add(_recycleBin);
        }

        if (settings.ShowAppsButton)
        {
            desired.Add(_showApplications);
        }

        Reconcile(desired);
        AppEntries = appEntries;
    }

    /// <summary>Drag preview: moves pinned <paramref name="dragged"/> into the slot of pinned <paramref name="target"/>.</summary>
    public void PreviewMove(DockAppEntry dragged, DockAppEntry target)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(target);
        var from = Entries.IndexOf(dragged);
        var to = Entries.IndexOf(target);
        if (from >= 0 && to >= 0 && from != to && dragged.IsPinned && target.IsPinned)
        {
            Entries.Move(from, to);
        }
    }

    /// <summary>Minimal in-place edits that turn <see cref="Entries"/> into <paramref name="desired"/>.</summary>
    private void Reconcile(List<DockEntry> desired)
    {
        var keep = new HashSet<DockEntry>(desired);
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Entries[i]))
            {
                Entries.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < Entries.Count && ReferenceEquals(Entries[i], desired[i]))
            {
                continue;
            }

            var existing = Entries.IndexOf(desired[i]);
            if (existing >= 0)
            {
                Entries.Move(existing, i);
            }
            else
            {
                Entries.Insert(i, desired[i]);
            }
        }
    }

    /// <summary>
    /// Start-menu icon first (pin launch id, the catalogue entry the window belongs to, or its AUMID), else the
    /// window's own icon. Loaded only when the inputs change, because window icons are not cached by the provider.
    /// </summary>
    private void LoadIcon(DockAppEntry entry, int sizePx)
    {
        // The size is unknown until the first layout; loading now would only query window icons for nothing.
        if (sizePx <= 0)
        {
            return;
        }

        var app = entry.App;
        var source = app.LaunchId ?? _catalog.FindForWindow(app.AppUserModelId, app.ProcessPath)?.LaunchId ?? app.AppUserModelId;
        var firstWindow = app.Windows.Count > 0 ? app.Windows[0] : 0;
        var key = string.Join('|', sizePx.ToString(CultureInfo.InvariantCulture),
            source ?? app.ProcessPath ?? firstWindow.ToString(CultureInfo.InvariantCulture), app.IsRunning ? "1" : "0");
        if (key == entry.IconKey)
        {
            return;
        }

        var icon = source is null ? null : _icons.GetAppIcon(source, sizePx);
        if (icon is null && firstWindow != 0)
        {
            icon = _icons.GetWindowIcon(firstWindow, app.ProcessPath, sizePx);
        }

        entry.Icon = icon;
        entry.IconKey = key;
    }

    private void LoadIcon(DockActionEntry entry, int sizePx)
    {
        var key = sizePx.ToString(CultureInfo.InvariantCulture);
        if (sizePx > 0 && key != entry.IconKey)
        {
            entry.Icon = _icons.GetAppIcon(RecycleBinLaunchId, sizePx);
            entry.IconKey = key;
        }
    }
}
