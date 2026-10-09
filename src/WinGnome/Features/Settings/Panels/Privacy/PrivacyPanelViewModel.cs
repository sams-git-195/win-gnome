using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Privacy;

/// <summary>One app in a capability's list: a switch for a packaged app, only its use for a desktop app.</summary>
internal sealed class PrivacyAppViewModel : ObservableObject
{
    public PrivacyAppViewModel(ConsentAppRow row, Action<PrivacyAppViewModel, VerifiedSwitch, bool> change)
    {
        Key = row.Key;
        Name = row.Name;
        IsDesktop = row.IsDesktop;
        Switch = new VerifiedSwitch((sw, on) => change(this, sw, on));
    }

    /// <summary>The registry key name (the package family name for a packaged app).</summary>
    public string Key { get; }

    public string Name { get; }

    public bool IsDesktop { get; }

    /// <summary>True for a packaged app, whose switch is shown; a desktop app only shows its use.</summary>
    public bool HasSwitch => !IsDesktop;

    /// <summary>"In use", "Last used ..." or null.</summary>
    public string? UseText { get; private set; }

    public VerifiedSwitch Switch { get; }

    public void Show(ConsentAppRow row, ConsentRowState state)
    {
        UseText = row.UseText;
        OnPropertyChanged(nameof(UseText));
        Switch.CanChange = state.CanChange && !IsDesktop;
        Switch.Confirm(state.IsOn);
    }
}

/// <summary>Camera, Microphone or Location: its two "Let apps access" switches, the device-wide state and the app list.</summary>
internal sealed class PrivacyCapabilityViewModel : ObservableObject
{
    private readonly Dictionary<string, PrivacyAppViewModel> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<PrivacyCapabilityViewModel, VerifiedSwitch, bool> _changeMaster;
    private readonly Action<PrivacyAppViewModel, VerifiedSwitch, bool> _changeApp;
    private bool _isExpanded;
    private bool _loaded;
    private string _deviceText = "Checking...";

    public PrivacyCapabilityViewModel(
        PrivacyCapability capability,
        string title,
        string noun,
        Action<PrivacyCapabilityViewModel, VerifiedSwitch, bool> changeMaster,
        Action<PrivacyAppViewModel, VerifiedSwitch, bool> changeApp,
        Action openDevice)
    {
        Capability = capability;
        Title = title;
        Noun = noun;
        _changeMaster = changeMaster;
        _changeApp = changeApp;
        Apps = new VerifiedSwitch((sw, on) => _changeMaster(this, sw, on));
        Desktop = new VerifiedSwitch((sw, on) => _changeMaster(this, sw, on));
        OpenDeviceCommand = new RelayCommand(openDevice);
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public PrivacyCapability Capability { get; }

    public string Title { get; }

    /// <summary>The device in lower case, for row text ("camera").</summary>
    public string Noun { get; }

    /// <summary>"Let apps access your ...".</summary>
    public VerifiedSwitch Apps { get; }

    /// <summary>"Let desktop apps access your ...".</summary>
    public VerifiedSwitch Desktop { get; }

    public ObservableCollection<PrivacyAppViewModel> AppList { get; } = [];

    public ICommand OpenDeviceCommand { get; }

    public ICommand ToggleExpandedCommand { get; }

    /// <summary>What Windows says about the device-wide switch, which only an administrator can change.</summary>
    public string DeviceText
    {
        get => _deviceText;
        private set => SetProperty(ref _deviceText, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandLabel));
            }
        }
    }

    public string ExpandLabel => IsExpanded ? "Hide" : "Show";

    public string AppsSummary => !_loaded
        ? "Loading..."
        : AppList.Count == 0
            ? "No apps have asked for this yet."
            : string.Create(CultureInfo.CurrentCulture, $"{AppList.Count} {(AppList.Count == 1 ? "app" : "apps")}");

    public void Show(ConsentSnapshot snapshot, IReadOnlyList<ConsentAppRow> rows)
    {
        DeviceText = ConsentValue.IsAllowed(snapshot.Device) ? "On" : "Off. Turn it on in Windows Settings; this needs an administrator.";

        var apps = ConsentEffective.ForMaster(snapshot.Device, snapshot.User);
        Apps.CanChange = apps.CanChange;
        Apps.Confirm(apps.IsOn);
        var desktop = ConsentEffective.ForMaster(snapshot.Device, snapshot.Desktop);
        Desktop.CanChange = desktop.CanChange;
        Desktop.Confirm(desktop.IsOn);

        var shown = new List<PrivacyAppViewModel>(rows.Count);
        foreach (var row in rows)
        {
            if (!_known.TryGetValue(row.Key, out var app))
            {
                app = new PrivacyAppViewModel(row, _changeApp);
                _known[row.Key] = app;
            }

            // A desktop app's own value isn't something Windows applies per app, so its row only shows its use.
            app.Show(row, ConsentEffective.For(snapshot.Device, snapshot.User, row.Value));
            shown.Add(app);
        }

        // Rebuilding an unchanged list would also drop the rows from the accessibility tree, so only touch it on a change.
        if (!AppList.SequenceEqual(shown))
        {
            AppList.Clear();
            foreach (var app in shown)
            {
                AppList.Add(app);
            }
        }

        _loaded = true;
        OnPropertyChanged(nameof(AppsSummary));
    }
}

/// <summary>
/// Privacy: for Camera, Microphone and Location the "Let apps access" and "Let desktop apps access" switches of the
/// current user, the device-wide state (read-only) and the apps that have asked, through <see cref="ConsentStore"/>.
/// Packaged apps have a switch, desktop apps only show whether they are in use. Every switch is verified-set: written
/// on the settings writer thread, read back, and shown as read.
/// </summary>
internal sealed class PrivacyPanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;

    public PrivacyPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Privacy)
    {
        _writer = context.CreateWriter();
        Camera = Create(PrivacyCapability.Camera, "Camera", "camera", "ms-settings:privacy-webcam");
        Microphone = Create(PrivacyCapability.Microphone, "Microphone", "microphone", "ms-settings:privacy-microphone");
        Location = Create(PrivacyCapability.Location, "Location", "location", "ms-settings:privacy-location");
        OpenScreenLockCommand = new RelayCommand(() => context.OpenLink("ms-settings:lockscreen"));
    }

    public PrivacyCapabilityViewModel Camera { get; }

    public PrivacyCapabilityViewModel Microphone { get; }

    public PrivacyCapabilityViewModel Location { get; }

    public ICommand OpenScreenLockCommand { get; }

    protected override void Open() => Reload();

    private PrivacyCapabilityViewModel Create(PrivacyCapability capability, string title, string noun, string link) =>
        new(capability, title, noun, ChangeMaster, ChangeApp, () => Context.OpenLink(link));

    private void Reload() => LoadAsync(ReadAll, Show);

    private static (ConsentSnapshot Camera, ConsentSnapshot Microphone, ConsentSnapshot Location) ReadAll() =>
        (ConsentStore.Read(PrivacyCapability.Camera), ConsentStore.Read(PrivacyCapability.Microphone), ConsentStore.Read(PrivacyCapability.Location));

    private void Show((ConsentSnapshot Camera, ConsentSnapshot Microphone, ConsentSnapshot Location) all)
    {
        var names = PackageNames();
        var clock = new ConsentClock(DateTime.UtcNow, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        Show(Camera, all.Camera, names, clock);
        Show(Microphone, all.Microphone, names, clock);
        Show(Location, all.Location, names, clock);
    }

    private static void Show(PrivacyCapabilityViewModel target, ConsentSnapshot snapshot, Dictionary<string, string> names, ConsentClock clock) =>
        target.Show(snapshot, ConsentAppList.Build(snapshot.Apps, family => names.GetValueOrDefault(family), clock));

    /// <summary>Package family name to app name, from the catalogue the shell already loaded (the first app of a package names it).</summary>
    private Dictionary<string, string> PackageNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in Context.Services.Apps.Apps)
        {
            var bang = app.ParsingName.IndexOf('!', StringComparison.Ordinal);
            if (bang > 0)
            {
                names.TryAdd(app.ParsingName[..bang], app.Name);
            }
        }

        return names;
    }

    private void ChangeMaster(PrivacyCapabilityViewModel capability, VerifiedSwitch target, bool on)
    {
        var desktop = ReferenceEquals(target, capability.Desktop);
        var kind = desktop ? "desktop apps" : "apps";
        Change(target, $"the {capability.Noun} setting for {kind}", $"turn {capability.Noun} access for {kind} {(on ? "on" : "off")}",
            () =>
            {
                if (desktop)
                {
                    ConsentStore.SetDesktop(capability.Capability, on);
                }
                else
                {
                    ConsentStore.SetUser(capability.Capability, on);
                }
            });
    }

    private void ChangeApp(PrivacyAppViewModel app, VerifiedSwitch target, bool on)
    {
        var capability = CapabilityOf(app);
        Change(target, $"{capability.Noun} access for {app.Name}", $"turn {capability.Noun} access for {app.Name} {(on ? "on" : "off")}",
            () => ConsentStore.SetApp(capability.Capability, app.Key, on));
    }

    private PrivacyCapabilityViewModel CapabilityOf(PrivacyAppViewModel app) =>
        new[] { Camera, Microphone, Location }.First(c => c.AppList.Contains(app));

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
}
