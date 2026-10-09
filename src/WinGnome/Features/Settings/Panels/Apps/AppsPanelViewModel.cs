using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.Panels.Apps;

/// <summary>
/// Apps: the installed desktop and packaged apps (searchable, with Uninstall) and the start-up items (GNOME Tweaks
/// style switches, Add and Remove). Desktop apps are read from the Uninstall keys on a long-running worker; packaged
/// rows come from the app catalogue the shell has already loaded, so opening the panel loads no WinRT. Uninstall runs
/// the app's own uninstaller (or removes the package for the current user) after a confirmation; start-up items are
/// turned off through StartupApproved, never deleted, and only the user's own Startup-folder shortcuts can be removed
/// (to the Recycle Bin). In safe mode everything is shown and nothing can be changed.
/// </summary>
internal sealed class AppsPanelViewModel : SystemPanelViewModel
{
    private const string AppsSettingsUri = "ms-settings:appsfeatures";

    // LoadAsync shows only the newest load per channel, so each independent list or row action has its own: a startup
    // re-read must never drop the installed-apps walk, a package's details or a removal's result. A row can't start a
    // second detail load or removal while one is in flight (IsBusy), so per-family channels drop nothing.

    private readonly SystemSettingWriter _writer;
    private readonly List<IDisposable> _uninstallWatches = [];
    private IReadOnlyList<InstalledAppRecord> _desktop = [];
    private IReadOnlyList<InstalledAppItem> _allApps = [];
    private IReadOnlyList<InstalledAppItem> _apps = [];
    private IReadOnlyList<StartupAppItem> _startup = [];
    private string _search = "";
    private bool _isLoadingApps;
    private bool _isOpen;

    public AppsPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Apps)
    {
        _writer = context.CreateWriter();
        UninstallCommand = new RelayCommand(p => Uninstall(p as InstalledAppItem));
        AddStartupCommand = new RelayCommand(AddStartup, () => CanEdit);
        RemoveStartupCommand = new RelayCommand(p => RemoveStartup(p as StartupAppItem));
        RefreshCommand = new RelayCommand(Refresh);
        TaskManagerCommand = new RelayCommand(() => ShellLaunch.SystemTool("taskmgr.exe", "/0 /startup"));
        StartupSettingsCommand = new RelayCommand(() => context.OpenLink("ms-settings:startupapps"));
    }

    /// <summary>The installed apps matching <see cref="Search"/>.</summary>
    public IReadOnlyList<InstalledAppItem> Apps
    {
        get => _apps;
        private set => SetProperty(ref _apps, value);
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? ""))
            {
                ApplySearch();
            }
        }
    }

    /// <summary>True until the desktop apps have been read.</summary>
    public bool IsLoadingApps
    {
        get => _isLoadingApps;
        private set => SetProperty(ref _isLoadingApps, value);
    }

    public bool HasNoMatches => !_isLoadingApps && _apps.Count == 0;

    public IReadOnlyList<StartupAppItem> StartupApps
    {
        get => _startup;
        private set => SetProperty(ref _startup, value);
    }

    /// <summary>Uninstalls the <see cref="InstalledAppItem"/> after a confirmation, or opens Windows Settings for it.</summary>
    public ICommand UninstallCommand { get; }

    public ICommand AddStartupCommand { get; }

    /// <summary>Moves a user Startup-folder shortcut (<see cref="StartupAppItem"/>) to the Recycle Bin after a confirmation.</summary>
    public ICommand RemoveStartupCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand TaskManagerCommand { get; }

    public ICommand StartupSettingsCommand { get; }

    protected override void Open()
    {
        _isOpen = true;
        Context.Services.Apps.Changed += OnCatalogChanged;
        Refresh();
    }

    protected override void Close()
    {
        _isOpen = false;
        Context.Services.Apps.Changed -= OnCatalogChanged;
        foreach (var watch in _uninstallWatches)
        {
            watch.Dispose();
        }

        _uninstallWatches.Clear();
        _desktop = [];
        _allApps = [];
        _search = "";
        Apps = [];
        StartupApps = [];
        OnPropertyChanged(nameof(Search));
    }

    private void Refresh()
    {
        RefreshApps();
        RefreshStartup();
    }

    private void RefreshApps()
    {
        if (!_isOpen)
        {
            return;
        }

        IsLoadingApps = _desktop.Count == 0;
        OnPropertyChanged(nameof(HasNoMatches));
        LoadAsync(InstalledAppsService.Read, records =>
        {
            _desktop = records;
            IsLoadingApps = false;
            RebuildApps();
        }, longRunning: true, channel: "installed");
    }

    private void RefreshStartup()
    {
        if (_isOpen)
        {
            LoadAsync(StartupAppsService.Read, rows =>
                StartupApps = rows.Select(r => new StartupAppItem(r, CanEdit, Context.Services.Icons, SetStartupEnabled)).ToList(),
                channel: "startup");
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => RebuildApps();

    /// <summary>Merges the desktop apps with the packaged rows from the app catalogue, by name.</summary>
    private void RebuildApps()
    {
        if (!_isOpen || _isLoadingApps)
        {
            return;
        }

        var icons = Context.Services.Icons;
        var packaged = PackagedAppRows.Build(Context.Services.Apps.Apps.Select(a => new CatalogApp(a.Name, a.ParsingName)));
        _allApps = _desktop.Select(r => InstalledAppItem.ForDesktop(r, Environment.SystemDirectory, CanEdit, icons))
            .Concat(packaged.Select(p => InstalledAppItem.ForPackage(p, CanEdit, icons, LoadPackageDetails)))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplySearch();
    }

    private void ApplySearch()
    {
        Apps = AppListSearch.Filter(_allApps, i => i.Name, i => i.Record?.Publisher, _search);
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private void LoadPackageDetails(InstalledAppItem item)
    {
        if (item.HasDetails || item.IsBusy || item.Package is not { } package)
        {
            return;
        }

        item.IsBusy = true;
        LoadAsync(() => PackagedAppsService.Details(package.FamilyName), details =>
        {
            item.IsBusy = false;
            item.ShowDetails(details);
        }, channel: "details:" + package.FamilyName);
    }

    private void Uninstall(InstalledAppItem? item)
    {
        if (item is null || item.IsBusy)
        {
            return;
        }

        if (!item.HasOwnUninstall)
        {
            Context.OpenLink(AppsSettingsUri);
            return;
        }

        if (!CanEdit)
        {
            return;
        }

        if (item.Package is { } package)
        {
            UninstallPackage(item, package);
        }
        else if (item.Plan is { } plan)
        {
            UninstallDesktop(item, plan);
        }
    }

    private void UninstallDesktop(InstalledAppItem item, PlannedCommand plan)
    {
        if (!Context.Services.Dialogs.Confirm($"Uninstall {item.Name}?",
                $"{item.Name}'s own uninstaller will start. It may ask for permission and has its own steps to follow.",
                "Uninstall", isDestructive: true))
        {
            return;
        }

        Log.Info($"Apps: starting the uninstaller of \"{item.Name}\": {plan.Executable} {plan.Arguments}");
        item.IsBusy = true;
        IDisposable? watch = null;
        watch = ShellLaunch.StartAndWatch(plan, () =>
        {
            // Posted to the dispatcher only while the panel is open (Close disposes the watch first).
            _uninstallWatches.Remove(watch!);
            watch!.Dispose();
            RefreshApps();
        });
        _uninstallWatches.Add(watch);
    }

    private void UninstallPackage(InstalledAppItem item, PackagedAppRow package)
    {
        if (!Context.Services.Dialogs.Confirm($"Uninstall {item.Name}?",
                $"{item.Name} and its data will be removed for your account.", "Uninstall", isDestructive: true))
        {
            return;
        }

        item.IsBusy = true;
        var family = package.FamilyName;
        LoadAsync(() => RemovePackage(family), result =>
        {
            item.IsBusy = false;
            Problem = result switch
            {
                PackageRemoval.NotRemovable => $"{item.Name} is part of Windows and can't be uninstalled here.",
                PackageRemoval.Failed => $"Windows couldn't uninstall {item.Name}. You can try in Windows Settings instead.",
                _ => null,
            };

            // The catalogue raises Changed when it has re-read the Start menu, which rebuilds the list.
            _ = Context.Services.Apps.RefreshAsync();
        }, longRunning: true, channel: "remove:" + family);
    }

    private static PackageRemoval RemovePackage(string familyName)
    {
        try
        {
            return PackagedAppsService.Remove(familyName);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Warn($"Apps: removing package family {familyName} failed", ex);
            return PackageRemoval.Failed;
        }
    }

    private void SetStartupEnabled(StartupAppItem item, bool enabled)
    {
        if (!CanEdit || !item.Row.Editable || item.IsBusy)
        {
            return;
        }

        item.IsBusy = true;
        var entry = item.Row.Entry;
        var dispatcher = Context.Dispatcher;
        _writer.Run($"turn the start-up item \"{entry.Name}\" {(enabled ? "on" : "off")}", () =>
        {
            try
            {
                return StartupAppsService.SetEnabled(entry, enabled);
            }
            finally
            {
                // Verified set: the list always shows what Windows holds after the write, whether it worked or not.
                dispatcher.BeginInvoke(RefreshStartup);
            }
        }, () => ReportWriteFailure($"the start-up item \"{item.Name}\""));
    }

    private void AddStartup()
    {
        if (!CanEdit || Context.Services.Dialogs.PickApp([]) is not { } app)
        {
            return;
        }

        string? created = null;
        ShellThread.Run($"Apps: could not add a start-up shortcut for \"{app.Name}\"", () =>
        {
            try
            {
                created = StartupAppsService.AddShortcut(app.Name, app.ParsingName);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
            {
                Log.Warn($"Apps: could not add a start-up shortcut for \"{app.Name}\"", ex);
            }

            return true;
        }, () =>
        {
            if (created is null && _isOpen)
            {
                Problem = $"WinGnome couldn't add {app.Name} to the start-up apps. You can try in Windows Settings instead.";
            }

            RefreshStartup();
        });
    }

    private void RemoveStartup(StartupAppItem? item)
    {
        if (item is null || !item.CanRemove
            || !Context.Services.Dialogs.Confirm($"Remove {item.Name} from start-up?",
                "The shortcut goes to the Recycle Bin, so you can restore it from there. The app itself stays installed.",
                "Remove", isDestructive: true))
        {
            return;
        }

        item.IsBusy = true;
        var path = item.Row.Entry.CommandLine;
        var removed = false;
        ShellThread.Run($"Apps: could not remove the start-up shortcut {path}", () =>
        {
            try
            {
                removed = StartupAppsService.Recycle(path);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
            {
                Log.Warn($"Apps: could not move {path} to the Recycle Bin", ex);
            }

            return true;
        }, () =>
        {
            if (!removed && _isOpen)
            {
                Problem = $"WinGnome couldn't remove {item.Name} from the start-up apps.";
            }

            RefreshStartup();
        });
    }
}
