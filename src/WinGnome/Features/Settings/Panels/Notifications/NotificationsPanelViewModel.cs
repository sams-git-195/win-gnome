using System.Collections.ObjectModel;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Notifications;

/// <summary>The switches shown under an expanded app (a type of its own so the view can pick a template for it).</summary>
/// <param name="Enabled">The app's own switch; the others are unavailable while it is off.</param>
/// <param name="Banner">Whether the app's notifications pop up as banners.</param>
/// <param name="Centre">Whether the app's notifications are kept in the notification centre.</param>
internal sealed record NotificationAppOptions(VerifiedSwitch Enabled, VerifiedSwitch Banner, VerifiedSwitch Centre);

/// <summary>One app in the list: its switch and, expanded, its banner and notification-centre switches.</summary>
internal sealed class NotificationAppViewModel : ObservableObject
{
    private readonly NotificationAppOptions _options;
    private bool _isExpanded;

    public NotificationAppViewModel(NotificationAppRow row, Func<NotificationAppViewModel, NotificationAppSetting, Action<VerifiedSwitch, bool>> request)
    {
        Id = row.Id;
        Name = row.Name;
        Enabled = new VerifiedSwitch(request(this, NotificationAppSetting.Enabled));
        Banner = new VerifiedSwitch(request(this, NotificationAppSetting.ShowBanner));
        Centre = new VerifiedSwitch(request(this, NotificationAppSetting.ShowInActionCenter));
        _options = new NotificationAppOptions(Enabled, Banner, Centre);
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        Show(row);
    }

    public string Id { get; }

    public string Name { get; }

    public VerifiedSwitch Enabled { get; }

    public VerifiedSwitch Banner { get; }

    public VerifiedSwitch Centre { get; }

    public ICommand ToggleExpandedCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(Details));
                OnPropertyChanged(nameof(ExpandGlyph));
            }
        }
    }

    /// <summary>Segoe Fluent Icons chevron: down when collapsed, up when expanded.</summary>
    public string ExpandGlyph => IsExpanded ? "" : "";

    /// <summary>The banner and notification-centre switches while expanded, else null (the row shows no details area).</summary>
    public NotificationAppOptions? Details => IsExpanded ? _options : null;

    public void Show(NotificationAppRow row)
    {
        Enabled.Confirm(row.Enabled);
        Banner.Confirm(row.Banner);
        Centre.Confirm(row.InCentre);
    }
}

/// <summary>
/// Notifications: the master switch, lock-screen notifications and a per-app list, all through
/// <see cref="NotificationSettingsStore"/>. Every switch is verified-set: the write runs on the writer thread, the
/// registry is read again, and the switch shows what was read. The Do Not Disturb row appears only once a Do Not
/// Disturb service is supplied (spec 0017).
/// </summary>
internal sealed class NotificationsPanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private readonly Dictionary<string, NotificationAppViewModel> _known = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public NotificationsPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Notifications)
    {
        _writer = context.CreateWriter();
        Master = new VerifiedSwitch((sw, on) => Change(sw, "notifications", $"turn notifications {OnOff(on)}",
            () => NotificationSettingsStore.SetMaster(on)));
        LockScreen = new VerifiedSwitch((sw, on) => Change(sw, "lock screen notifications",
            $"turn lock screen notifications {OnOff(on)}", () => NotificationSettingsStore.SetLockScreen(on)));
    }

    /// <summary>
    /// The Do Not Disturb switch, or null (the row is hidden) while no Do Not Disturb service exists. Spec 0017's
    /// service will fill it in Open when it joins <see cref="SystemPanelServices"/> (noted under KI-085).
    /// </summary>
    public VerifiedSwitch? DoNotDisturb { get; private set; }

    public VerifiedSwitch Master { get; }

    public VerifiedSwitch LockScreen { get; }

    /// <summary>The line under the lock-screen row: its description, or why it is locked.</summary>
    public string LockScreenSubtitle { get; private set; } = "Show notifications on the lock screen.";

    public ObservableCollection<NotificationAppViewModel> Apps { get; } = [];

    /// <summary>The line under the app list: empty-state text or how the list was built.</summary>
    public string AppsDescription => !_loaded
        ? "Loading apps..."
        : Apps.Count == 0
            ? "No apps have sent notifications yet."
            : "Apps that have sent a notification. Turn one off to stop its banners and sounds.";

    protected override void Open() => Reload();

    private void Reload() => LoadAsync(NotificationSettingsStore.Read, Show);

    private void Show(NotificationSnapshot snapshot)
    {
        Master.Confirm(snapshot.Master);
        LockScreen.CanChange = !snapshot.LockScreenPolicy;
        LockScreen.Confirm(snapshot.LockScreen && !snapshot.LockScreenPolicy);
        LockScreenSubtitle = snapshot.LockScreenPolicy
            ? "Turned off by your organisation, so it can't be changed here."
            : "Show notifications on the lock screen.";
        OnPropertyChanged(nameof(LockScreenSubtitle));

        var rows = NotificationAppList.Build(snapshot.Keys, NameOf);
        var shown = new List<NotificationAppViewModel>(rows.Count);
        foreach (var row in rows)
        {
            if (_known.TryGetValue(row.Id, out var existing))
            {
                existing.Show(row);
                shown.Add(existing);
            }
            else
            {
                var created = new NotificationAppViewModel(row, AppRequest);
                _known[row.Id] = created;
                shown.Add(created);
            }
        }

        // Rebuilding an unchanged list would also drop the rows from the accessibility tree, so only touch it on a change.
        if (!Apps.SequenceEqual(shown))
        {
            Apps.Clear();
            foreach (var app in shown)
            {
                Apps.Add(app);
            }
        }

        _loaded = true;
        OnPropertyChanged(nameof(AppsDescription));
    }

    private string? NameOf(string id) => Context.Services.Apps.FindForWindow(id, null)?.Name;

    private Action<VerifiedSwitch, bool> AppRequest(NotificationAppViewModel app, NotificationAppSetting setting) =>
        (sw, on) => Change(sw, $"notifications from {app.Name}", $"turn {setting} {OnOff(on)} for {app.Name}",
            () => NotificationSettingsStore.SetApp(app.Id, setting, on));

    /// <summary>Writes on the writer thread, then reads everything back and shows it. Does nothing in safe mode.</summary>
    private void Change(VerifiedSwitch target, string label, string what, Action write)
    {
        if (Context.IsReadOnly)
        {
            Log.Info($"Safe mode: did not {what}");
            return;
        }

        target.Begin();
        _writer.Run(what,
            () =>
            {
                try
                {
                    write();
                    return true;
                }
                finally
                {
                    Context.Dispatcher.BeginInvoke(() => Written(target));
                }
            },
            () => ReportWriteFailure(label));
    }

    private void Written(VerifiedSwitch target)
    {
        target.End();
        Reload();
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
