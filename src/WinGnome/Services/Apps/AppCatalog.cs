using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services.Apps;

/// <summary>
/// Lists installed apps from shell:AppsFolder (the Start menu's "All apps"). Enumeration runs on a
/// dedicated STA thread; results are published as an immutable snapshot, so lookups are lock-free and
/// safe from any thread. The catalogue refreshes itself when Start-menu shortcuts change.
/// </summary>
internal sealed class AppCatalog : IAppCatalog, IDisposable
{
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromSeconds(2);

    /// <summary>PKEY_Link_TargetParsingPath: the target of the shortcut behind a desktop app.</summary>
    private static readonly WindowProperties.PROPERTYKEY LinkTargetParsingPathKey = new()
    {
        fmtid = new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"),
        pid = 2,
    };

    /// <summary>Documents and web links that show up in "All apps" but are not applications.</summary>
    private static readonly string[] NonAppExtensions = [".url", ".chm", ".txt", ".pdf", ".htm", ".html", ".rtf"];

    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _debounceTimer;
    private volatile Snapshot _snapshot = Snapshot.Empty;
    private Task? _refreshTask;
    private bool _refreshRequested;
    private bool _disposed;

    public AppCatalog(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _debounceTimer = new Timer(_ => RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
        WatchStartMenu(Environment.SpecialFolder.CommonStartMenu);
        WatchStartMenu(Environment.SpecialFolder.StartMenu);
    }

    public IReadOnlyList<AppEntry> Apps => _snapshot.Apps;

    public event EventHandler? Changed;

    /// <summary>
    /// Re-enumerates AppsFolder. While a refresh is running, further calls do not start a second
    /// enumeration: they schedule one more pass and return the running task, which completes once the
    /// catalogue reflects every request made before it finished. Never faults.
    /// </summary>
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_refreshTask is not null)
            {
                _refreshRequested = true;
                return _refreshTask;
            }

            _refreshRequested = true;
            _refreshTask = Task.Run(RefreshLoopAsync);
            return _refreshTask;
        }
    }

    public AppEntry? FindByLaunchId(string launchId)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            return null;
        }

        var snapshot = _snapshot;
        var id = launchId.Trim();
        return snapshot.ByParsingName.GetValueOrDefault(id) ?? snapshot.ByAppUserModelId.GetValueOrDefault(id);
    }

    public AppEntry? FindForWindow(string? appUserModelId, string? processPath)
    {
        var snapshot = _snapshot;
        if (!string.IsNullOrWhiteSpace(appUserModelId))
        {
            // Packaged and explicit-AUMID apps usually use the AUMID as their parsing name too, which
            // covers entries whose AUMID property could not be read.
            var id = appUserModelId.Trim();
            var match = snapshot.ByAppUserModelId.GetValueOrDefault(id) ?? snapshot.ByParsingName.GetValueOrDefault(id);
            if (match is not null)
            {
                return match;
            }
        }

        var path = NormalizePath(processPath);
        return path is null ? null : snapshot.ByTargetPath.GetValueOrDefault(path);
    }

    public string ResolvePath(string launchId)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            return launchId ?? string.Empty;
        }

        var target = FindByLaunchId(launchId)?.TargetPath;
        return !string.IsNullOrEmpty(target) ? target : KnownFolderPath.Resolve(launchId.Trim(), NativeMethods.GetKnownFolderPath);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _debounceTimer.Dispose();
    }

    private async Task RefreshLoopAsync()
    {
        while (true)
        {
            lock (_gate)
            {
                if (!_refreshRequested || _disposed)
                {
                    _refreshTask = null;
                    return;
                }

                _refreshRequested = false;
            }

            await RefreshOnceAsync().ConfigureAwait(false);
        }
    }

    private async Task RefreshOnceAsync()
    {
        List<AppEntry> apps;
        try
        {
            apps = await RunOnStaThreadAsync(EnumerateAppsFolder).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Keep the previous list: a transient shell failure must not empty the dock or the overview.
            Log.Warn("AppCatalog: enumerating shell:AppsFolder failed", ex);
            return;
        }

        var previous = _snapshot.Apps;
        if (previous.Count == apps.Count && previous.SequenceEqual(apps))
        {
            return;
        }

        _snapshot = Snapshot.Create(apps);
        Log.Info($"AppCatalog: {apps.Count} apps");
        _ = _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>Runs <paramref name="work"/> on a new background STA thread (the shell requires an apartment).</summary>
    private static Task<T> RunOnStaThreadAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                // Never let an exception escape a raw thread: that would terminate the process.
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "WinGnome AppCatalog",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>Enumerates AppsFolder's children. Throws if the folder itself cannot be opened.</summary>
    private static List<AppEntry> EnumerateAppsFolder()
    {
        object? folderObject = null;
        object? enumObject = null;
        try
        {
            var folderId = ShellGuids.AppsFolder;
            var shellItemId = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(NativeMethods.SHGetKnownFolderItem(ref folderId, 0, 0, ref shellItemId, out folderObject));
            if (folderObject is not IShellItem folder)
            {
                throw new InvalidOperationException("shell:AppsFolder is not an IShellItem");
            }

            var handlerId = ShellGuids.BindEnumItems;
            var enumId = typeof(IEnumShellItems).GUID;
            Marshal.ThrowExceptionForHR(folder.BindToHandler(0, ref handlerId, ref enumId, out enumObject));
            if (enumObject is not IEnumShellItems items)
            {
                throw new InvalidOperationException("shell:AppsFolder did not return IEnumShellItems");
            }

            var apps = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (items.Next(1, out var child, out var fetched) == 0 && fetched == 1 && child is not null)
            {
                try
                {
                    var entry = ReadEntry(child);
                    if (entry is not null && seen.Add(entry.ParsingName))
                    {
                        apps.Add(entry);
                    }
                }
                catch (COMException ex)
                {
                    Log.Warn("AppCatalog: skipped an AppsFolder item", ex);
                }
                finally
                {
                    Marshal.ReleaseComObject(child);
                }
            }

            apps.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
            return apps;
        }
        finally
        {
            if (enumObject is not null)
            {
                Marshal.ReleaseComObject(enumObject);
            }

            if (folderObject is not null)
            {
                Marshal.ReleaseComObject(folderObject);
            }
        }
    }

    /// <summary>Builds an entry for one AppsFolder child, or null for items that are not launchable apps.</summary>
    private static AppEntry? ReadEntry(IShellItem item)
    {
        if (item.GetDisplayName(SIGDN.NormalDisplay, out var name) != 0 || string.IsNullOrWhiteSpace(name)
            || item.GetDisplayName(SIGDN.ParentRelativeParsing, out var parsingName) != 0 || string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        if (IsJunk(name, parsingName))
        {
            return null;
        }

        string? appUserModelId = null;
        string? targetPath = null;
        if (item is IShellItem2 item2) // QueryInterface on the same RCW; released with the item.
        {
            appUserModelId = GetStringProperty(item2, WindowProperties.AppUserModelIdKey);
            targetPath = GetStringProperty(item2, LinkTargetParsingPathKey);
        }

        // Classic apps without a shortcut are listed as "{KNOWNFOLDERID}\relative\app.exe": expand that so
        // running windows can be matched by executable path.
        if (string.IsNullOrEmpty(targetPath) && KnownFolderPath.TryParse(parsingName, out _, out _))
        {
            targetPath = KnownFolderPath.Resolve(parsingName, NativeMethods.GetKnownFolderPath);
        }

        // Only keep real file-system targets; shell namespace targets such as "::{CLSID}" are not paths.
        if (targetPath is not null && !Path.IsPathFullyQualified(targetPath))
        {
            targetPath = null;
        }

        return new AppEntry(name.Trim(), parsingName, appUserModelId, targetPath);
    }

    private static bool IsJunk(string name, string parsingName) =>
        name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)
        || parsingName.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || parsingName.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || Array.Exists(NonAppExtensions, ext => parsingName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads an optional string property; missing properties and per-item failures yield null.</summary>
    private static string? GetStringProperty(IShellItem2 item, WindowProperties.PROPERTYKEY key)
    {
        try
        {
            return item.GetString(ref key, out var value) == 0 && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Canonical form of an absolute file-system path, or null if it is empty, relative or malformed.</summary>
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void WatchStartMenu(Environment.SpecialFolder startMenu)
    {
        try
        {
            var root = Environment.GetFolderPath(startMenu);
            var programs = Path.Combine(root, "Programs");
            if (string.IsNullOrEmpty(root) || !Directory.Exists(programs))
            {
                return;
            }

            var watcher = new FileSystemWatcher(programs)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnStartMenuChanged;
            watcher.Deleted += OnStartMenuChanged;
            watcher.Changed += OnStartMenuChanged;
            watcher.Renamed += OnStartMenuChanged;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Log.Warn($"AppCatalog: cannot watch the {startMenu} folder", ex);
        }
    }

    private void OnStartMenuChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Typically a buffer overflow during a large install: changes were lost, so just reload.
        Log.Warn("AppCatalog: Start menu watcher error", e.GetException());
        ScheduleRefresh();
    }

    /// <summary>Restarts the debounce timer so a burst of file events produces a single refresh.</summary>
    private void ScheduleRefresh()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _debounceTimer.Change(WatchDebounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Immutable view of the catalogue plus lookup indexes, swapped atomically on refresh.</summary>
    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = Create([]);

        private Snapshot(IReadOnlyList<AppEntry> apps)
        {
            Apps = apps;
        }

        public IReadOnlyList<AppEntry> Apps { get; }

        public Dictionary<string, AppEntry> ByParsingName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AppEntry> ByAppUserModelId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, AppEntry> ByTargetPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Snapshot Create(List<AppEntry> apps)
        {
            var snapshot = new Snapshot(apps.AsReadOnly());
            var pathRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in apps)
            {
                // TryAdd: when several entries share a key, the first in name order wins.
                snapshot.ByParsingName.TryAdd(app.ParsingName, app);
                if (!string.IsNullOrEmpty(app.AppUserModelId))
                {
                    snapshot.ByAppUserModelId.TryAdd(app.AppUserModelId, app);
                }

                if (NormalizePath(app.TargetPath) is { } target && PathMatchRank(app) is { } rank
                    && (!pathRanks.TryGetValue(target, out var best) || rank < best))
                {
                    pathRanks[target] = rank;
                    snapshot.ByTargetPath[target] = app;
                }
            }

            return snapshot;
        }

        /// <summary>
        /// How well an entry represents "the app" for a window of its target executable (lower is better):
        /// 0 = the entry is the executable itself, 1 = a shortcut with an explicit AUMID. Shortcuts the shell
        /// had to give a generated AUMID ("Microsoft.AutoGenerated.{...}") are launchers with arguments, such
        /// as WSL GUI apps (wslg.exe), developer prompts (cmd.exe) or folder shortcuts (explorer.exe); they
        /// never represent every window of their executable, so they are not used for path matching.
        /// </summary>
        private static int? PathMatchRank(AppEntry app)
        {
            if (KnownFolderPath.LooksLikeFileSystemPath(app.ParsingName))
            {
                return 0;
            }

            return app.ParsingName.StartsWith("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase) ? null : 1;
        }
    }
}
