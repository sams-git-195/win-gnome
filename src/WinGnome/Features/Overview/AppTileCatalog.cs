using System.Diagnostics;
using System.Windows.Threading;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Overview;

/// <summary>
/// The overview's view of the installed apps: one <see cref="AppTile"/> per catalogue entry, reused across
/// catalogue refreshes so icons that were already loaded stay loaded.
/// </summary>
/// <remarks>
/// Shell icon extraction costs a few milliseconds per app and must happen on the UI (STA) thread, so with
/// 150+ apps it is spread over Background-priority dispatcher slices of about one frame each. Tiles the
/// user can see right now (search results) jump the queue.
/// </remarks>
internal sealed class AppTileCatalog : IDisposable
{
    /// <summary>Time budget per dispatcher slice, so input and rendering stay smooth while icons load.</summary>
    private static readonly TimeSpan SliceBudget = TimeSpan.FromMilliseconds(12);

    private readonly IAppCatalog _catalog;
    private readonly IIconProvider _icons;
    private readonly Dispatcher _dispatcher;
    private readonly LinkedList<AppTile> _queue = new();
    private readonly HashSet<AppTile> _queued = [];
    private Dictionary<string, AppTile> _byLaunchId = new(StringComparer.OrdinalIgnoreCase);
    private bool _sliceScheduled;
    private int _iconSizePx = 64;

    public AppTileCatalog(IAppCatalog catalog, IIconProvider icons, Dispatcher dispatcher)
    {
        _catalog = catalog;
        _icons = icons;
        _dispatcher = dispatcher;
        _catalog.Changed += OnCatalogChanged;
        Rebuild();
    }

    /// <summary>All apps, sorted by name.</summary>
    public IReadOnlyList<AppTile> Tiles { get; private set; } = [];

    /// <summary>Raised after <see cref="Tiles"/> was replaced because the installed apps changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Sets the icon size in physical pixels (it depends on the monitor's DPI). Icons loaded at another
    /// size are reloaded when next requested.
    /// </summary>
    public int IconSizePx
    {
        get => _iconSizePx;
        set => _iconSizePx = Math.Max(16, value);
    }

    /// <summary>Queues icon loading for every tile, in display order.</summary>
    public void LoadAllIcons()
    {
        foreach (var tile in Tiles)
        {
            Request(tile, urgent: false);
        }
    }

    /// <summary>Queues icon loading for tiles that are about to be shown, ahead of everything else.</summary>
    public void LoadIconsNow(IEnumerable<AppTile> tiles)
    {
        // Reverse so that the first tile ends up at the very front of the queue.
        foreach (var tile in tiles.Reverse())
        {
            Request(tile, urgent: true);
        }
    }

    private void Request(AppTile tile, bool urgent)
    {
        if (tile.IconSizePx == _iconSizePx)
        {
            return;
        }

        if (!_queued.Add(tile))
        {
            if (!urgent)
            {
                return;
            }

            _queue.Remove(tile);
        }

        if (urgent)
        {
            _queue.AddFirst(tile);
        }
        else
        {
            _queue.AddLast(tile);
        }

        if (!_sliceScheduled)
        {
            _sliceScheduled = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, LoadSlice);
        }
    }

    private void LoadSlice()
    {
        _sliceScheduled = false;
        var clock = Stopwatch.StartNew();
        while (_queue.First is { } node && clock.Elapsed < SliceBudget)
        {
            var tile = node.Value;
            _queue.RemoveFirst();
            _queued.Remove(tile);
            tile.Icon = _icons.GetAppIcon(tile.LaunchId, _iconSizePx);
            tile.IconSizePx = _iconSizePx;
        }

        if (_queue.Count > 0)
        {
            _sliceScheduled = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, LoadSlice);
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        var byLaunchId = new Dictionary<string, AppTile>(StringComparer.OrdinalIgnoreCase);
        var tiles = new List<AppTile>(_catalog.Apps.Count);
        foreach (var app in _catalog.Apps)
        {
            if (byLaunchId.ContainsKey(app.LaunchId))
            {
                continue;
            }

            var tile = _byLaunchId.TryGetValue(app.LaunchId, out var existing) && existing.Name == app.Name
                ? existing
                : new AppTile(app.Name, app.LaunchId);
            byLaunchId[app.LaunchId] = tile;
            tiles.Add(tile);
        }

        _byLaunchId = byLaunchId;
        Tiles = tiles;

        // Drop queued work for apps that no longer exist.
        for (var node = _queue.First; node is not null;)
        {
            var next = node.Next;
            if (!byLaunchId.ContainsKey(node.Value.LaunchId))
            {
                _queued.Remove(node.Value);
                _queue.Remove(node);
            }

            node = next;
        }
    }

    public void Dispose()
    {
        _catalog.Changed -= OnCatalogChanged;
        _queue.Clear();
        _queued.Clear();
    }
}
