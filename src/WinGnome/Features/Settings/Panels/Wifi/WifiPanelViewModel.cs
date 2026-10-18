using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Devices.Radios;
using WinGnome.Core.Connectivity;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Services.Connectivity;

namespace WinGnome.Features.Settings.Panels.Wifi;

/// <summary>One row of the Visible Networks list.</summary>
internal sealed class WifiNetworkItem(WifiNetworkRow row, bool isConnecting)
{
    public WifiNetworkRow Row { get; } = row;

    public string Name => Row.Name;

    public string Subtitle => isConnecting ? "Connecting…" : Row.Subtitle;

    public bool IsConnected => Row.IsConnected;

    public bool IsSecured => Row.IsSecured;

    public bool CanConnect => !Row.IsConnected && !isConnecting;

    public bool CanDisconnect => Row.IsConnected;

    public bool CanForget => Row.IsSaved;

    public string SignalGlyph => GlyphFor(Row.SignalLevel);

    /// <summary>Signal strength as the cellular-bars glyphs of Segoe Fluent Icons (one to four bars).</summary>
    public static string GlyphFor(int level) => level switch
    {
        <= 1 => "",
        2 => "",
        3 => "",
        _ => "",
    };
}

/// <summary>
/// One row of the Saved Networks list. Kept across reloads (by profile name), so a write in flight on its switch isn't
/// lost when a WLAN event re-reads the list before the write finishes.
/// </summary>
internal sealed class WifiSavedNetworkItem : ObservableObject
{
    private WifiSavedNetworkRow _row;
    private bool _isConnecting;

    public WifiSavedNetworkItem(WifiSavedNetworkRow row, Action<WifiSavedNetworkItem, VerifiedSwitch, bool> changeAutoConnect)
    {
        _row = row;
        ProfileName = row.ProfileName;
        AutoConnect = new VerifiedSwitch((sw, on) => changeAutoConnect(this, sw, on));
    }

    /// <summary>What connect, forget and the connect-mode edit take.</summary>
    public string ProfileName { get; }

    public WifiSavedNetworkRow Row => _row;

    public string Name => _row.Name;

    public string Subtitle => _isConnecting ? "Connecting…" : _row.Subtitle;

    public bool CanConnect => !_row.IsConnected && !_isConnecting;

    /// <summary>True when the Connect automatically switch and Forget are offered (not for Group Policy or unreadable profiles).</summary>
    public bool CanEdit => _row.CanEdit;

    /// <summary>True when the visible list gave this network's signal.</summary>
    public bool HasSignal => _row.SignalLevel is not null;

    public string SignalGlyph => WifiNetworkItem.GlyphFor(_row.SignalLevel ?? 0);

    /// <summary>The profile's connect-automatically setting, as Windows last reported it.</summary>
    public VerifiedSwitch AutoConnect { get; }

    /// <param name="row">The row as just read.</param>
    /// <param name="isConnecting">True while this panel connects to it.</param>
    /// <param name="canEdit">False in safe mode, where no switch can change.</param>
    public void Show(WifiSavedNetworkRow row, bool isConnecting, bool canEdit)
    {
        _row = row;
        _isConnecting = isConnecting;
        AutoConnect.CanChange = canEdit && row.CanEdit;
        // While a write is in flight the switch waits for the re-read after it, which shows the written value.
        if (AutoConnect.IsIdle)
        {
            AutoConnect.Confirm(row.AutoConnect);
        }

        OnPropertyChanged(nameof(Row));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(HasSignal));
        OnPropertyChanged(nameof(SignalGlyph));
    }
}

/// <summary>
/// Wi-Fi: the Wi-Fi switch, the current network, the visible networks when Windows shares them (or a notice saying
/// why not), the saved networks with connect, connect automatically and forget, and links for the rest. Only the
/// visible list, scans and the current connection's WLAN query depend on location access; everything else uses calls
/// Windows doesn't gate. Reads happen when the panel opens and when Windows reports a change (no polling); every
/// Windows call runs off the UI thread. Connecting follows <see cref="WifiConnectFlow"/>, which decides what to write,
/// when a failed attempt must delete the profile it made, and when to ask for the password again. Passwords only ever live in <c>char[]</c>
/// buffers that are cleared as soon as the profile is written, and are never logged.
/// </summary>
internal sealed class WifiPanelViewModel : SystemPanelViewModel
{
    private const string WifiLink = "ms-settings:network-wifi";
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly WifiLocationNoticeText NoNotice = WifiLocationNotice.For(WifiLocationBlock.None, callRefused: false);

    private readonly SystemSettingWriter _writer;
    private readonly WifiConnectFlow _flow = new(ConnectTimeout);
    private readonly ScanThrottle _scanThrottle = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _connectTimer;
    private WlanClient? _client;
    private RadioClient? _radio;
    private WifiLocationProbe? _location;
    private bool _userAskedNearby;
    private bool _callRefused;
    private bool _gatedCallsAllowed;
    private WifiLocationNoticeText _notice = NoNotice;
    private int _open;
    private IReadOnlyList<string> _profiles = [];
    private Dictionary<string, WifiSavedNetworkItem> _savedItems = new(StringComparer.OrdinalIgnoreCase);
    private WifiCurrentNetwork? _current;
    private WifiSavedNetworkItem? _currentSaved;
    private WifiNetworkRow? _target;
    private string? _targetProfile;
    private string? _connectingKey;
    private bool _isLoading;
    private bool _hasAdapter;
    private bool _radioPresent;
    private bool _radioCanChange = true;
    private bool _wifiOn;
    private bool _isBusy;
    private bool _loaded;

    public WifiPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Wifi)
    {
        _writer = context.CreateWriter();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, context.Dispatcher) { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Reload();
        };
        _connectTimer = new DispatcherTimer(DispatcherPriority.Normal, context.Dispatcher) { Interval = ConnectTimeout };
        _connectTimer.Tick += (_, _) => OnConnectTimeout();

        RefreshCommand = new RelayCommand(Refresh);
        ConnectCommand = new RelayCommand(parameter => Connect((parameter as WifiNetworkItem)?.Row), _ => CanChange);
        ConnectSavedCommand = new RelayCommand(parameter => ConnectSaved(parameter as WifiSavedNetworkItem), _ => CanChange);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => CanChange);
        ForgetCommand = new RelayCommand(Forget, _ => CanChange);
        OpenLocationSettingsCommand = new RelayCommand(() => context.OpenLink("ms-settings:privacy-location"));
        // Windows' own page lists and connects to nearby networks: as a system component it isn't gated on location.
        ShowWindowsListCommand = new RelayCommand(() => context.OpenLink(WifiLink));
        AirplaneModeCommand = new RelayCommand(() => context.OpenLink("ms-settings:network-airplanemode"));
        HotspotCommand = new RelayCommand(() => context.OpenLink("ms-settings:network-mobilehotspot"));
        HiddenNetworkCommand = new RelayCommand(() => context.OpenLink(WifiLink));
        ShowNearbyCommand = new RelayCommand(() =>
        {
            _userAskedNearby = true;
            Reload(scanAfter: true);
        });
    }

    public ObservableCollection<WifiNetworkItem> Networks { get; } = [];

    public ObservableCollection<WifiSavedNetworkItem> SavedNetworks { get; } = [];

    public ICommand RefreshCommand { get; }

    /// <summary>Parameter: the <see cref="WifiNetworkItem"/>.</summary>
    public ICommand ConnectCommand { get; }

    /// <summary>Parameter: the <see cref="WifiSavedNetworkItem"/>.</summary>
    public ICommand ConnectSavedCommand { get; }

    /// <summary>Disconnects the adapter from whichever network it is on.</summary>
    public ICommand DisconnectCommand { get; }

    /// <summary>Parameter: a <see cref="WifiNetworkItem"/> or a <see cref="WifiSavedNetworkItem"/>.</summary>
    public ICommand ForgetCommand { get; }

    public ICommand OpenLocationSettingsCommand { get; }

    /// <summary>Opens Windows' Wi-Fi page, which lists nearby networks where WinGnome can't.</summary>
    public ICommand ShowWindowsListCommand { get; }

    public ICommand AirplaneModeCommand { get; }

    public ICommand HotspotCommand { get; }

    public ICommand HiddenNetworkCommand { get; }

    /// <summary>Makes the first location-gated call, which is what raises Windows' consent prompt.</summary>
    public ICommand ShowNearbyCommand { get; }

    /// <summary>True when access hasn't been asked for yet, so the notice offers "Show nearby networks".</summary>
    public bool OfferShowNearby => _notice.OfferShowNearby;

    public bool OffersLocationSettings => _notice.OfferLocationSettings;

    public bool OffersWindowsList => _notice.OfferWindowsList;

    public string LocationNoticeTitle => _notice.Title;

    public string LocationNoticeSubtitle => _notice.Subtitle;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>True while a switch or forget is in flight; the controls that change something wait.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanChange));
                OnPropertyChanged(nameof(CanToggleWifi));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanChange => CanEdit && !IsBusy;

    /// <summary>True when the machine has a Wi-Fi radio; the Wi-Fi switch and the Airplane Mode row only show then.</summary>
    public bool HasRadio => _radioPresent;

    public bool CanToggleWifi => CanChange && _radioPresent && _radioCanChange;

    public string WifiSubtitle => !_radioPresent
        ? "No Wi-Fi radio found."
        : _radioCanChange ? "Turn the Wi-Fi radio on or off." : "Wi-Fi is turned off by a hardware switch, Airplane Mode or policy.";

    /// <summary>
    /// The Wi-Fi switch. Setting it asks Windows to switch the radio and then shows what the radio reports; the field
    /// is only ever set from a read, so a refused change snaps back.
    /// </summary>
    public bool WifiOn
    {
        get => _wifiOn;
        set
        {
            if (value == _wifiOn || !CanToggleWifi)
            {
                OnPropertyChanged();
                return;
            }

            SetRadio(value);
        }
    }

    public bool ShowsNoAdapter => _loaded && !_hasAdapter;

    /// <summary>True when the list of visible networks (not the location notice or the empty states) shows.</summary>
    public bool ShowsList => _hasAdapter && _gatedCallsAllowed;

    /// <summary>In place of the visible list when Windows withholds it, with the reason the gate found.</summary>
    public bool ShowsLocationNotice => _hasAdapter && !_gatedCallsAllowed && _notice.Title.Length > 0;

    public bool ShowsEmptyListNote => ShowsList && !_isLoading && Networks.Count == 0;

    /// <summary>True whenever the adapter is connected, whether or not Windows names the network.</summary>
    public bool ShowsCurrentNetwork => _hasAdapter && _current is not null;

    public string CurrentNetworkName => _current?.Name ?? "";

    public string CurrentNetworkSubtitle => _current?.Subtitle ?? "";

    public bool ShowsCurrentSignal => _current?.SignalLevel is not null;

    public string CurrentSignalGlyph => WifiNetworkItem.GlyphFor(_current?.SignalLevel ?? 0);

    /// <summary>The connected network's saved row when its profile can be changed: its switch and Forget show in Current network too.</summary>
    public WifiSavedNetworkItem? CurrentSaved => _currentSaved;

    public bool ShowsCurrentProfileOptions => _currentSaved is not null;

    public bool ShowsSavedNetworks => _hasAdapter && SavedNetworks.Count > 0;

    public bool ShowsNoSavedNote => _loaded && _hasAdapter && !_isLoading && SavedNetworks.Count == 0;

    protected override void Open()
    {
        Interlocked.Increment(ref _open);
        Networks.Clear();
        ClearSaved();
        _loaded = false;
        _hasAdapter = false;
        _gatedCallsAllowed = false;
        _callRefused = false;
        _notice = NoNotice;
        NotifyState();

        if (Context.Options.SelfTest)
        {
            // An unattended run must not raise Windows' location prompt or touch a radio.
            Log.Info("Wi-Fi panel: self-test, not reading adapters, networks or radios");
            _loaded = true;
            NotifyState();
            return;
        }

        _userAskedNearby = false;
        _client = new WlanClient();
        _client.Changed += OnWlanChanged;
        _location = new WifiLocationProbe();
        _location.Changed += OnLocationChanged;
        _radio = new RadioClient(RadioKind.WiFi);
        _radio.Changed += OnRadioChanged;
        ReadRadio();
        Reload(scanAfter: true);
    }

    protected override void Close()
    {
        Interlocked.Increment(ref _open);
        _refreshTimer.Stop();
        _connectTimer.Stop();
        // Windows carries on connecting after the panel closes, so a profile this attempt wrote stays (it can be
        // forgotten from the list); the attempt's later results are ignored.
        _flow.Abandon();

        if (_location is { } location)
        {
            location.Changed -= OnLocationChanged;
            location.Dispose();
            _location = null;
        }

        var client = _client;
        _client = null;
        if (client is not null)
        {
            client.Changed -= OnWlanChanged;
            // A write or delete still queued needs the handle, so the handle closes behind it on the writer's queue.
            if (CanEdit)
            {
                _writer.Run("release the Wi-Fi handle", () =>
                {
                    client.Dispose();
                    return true;
                }, static () => { });
            }
            else
            {
                client.Dispose();
            }
        }

        _radio?.Dispose();
        _radio = null;
        Networks.Clear();
        ClearSaved();
        _connectingKey = null;
        _target = null;
    }

    private void ClearSaved()
    {
        SavedNetworks.Clear();
        _savedItems = new(StringComparer.OrdinalIgnoreCase);
        _current = null;
        _currentSaved = null;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(ShowsNoAdapter));
        OnPropertyChanged(nameof(ShowsList));
        OnPropertyChanged(nameof(ShowsLocationNotice));
        OnPropertyChanged(nameof(ShowsEmptyListNote));
        OnPropertyChanged(nameof(ShowsCurrentNetwork));
        OnPropertyChanged(nameof(CurrentNetworkName));
        OnPropertyChanged(nameof(CurrentNetworkSubtitle));
        OnPropertyChanged(nameof(ShowsCurrentSignal));
        OnPropertyChanged(nameof(CurrentSignalGlyph));
        OnPropertyChanged(nameof(CurrentSaved));
        OnPropertyChanged(nameof(ShowsCurrentProfileOptions));
        OnPropertyChanged(nameof(ShowsSavedNetworks));
        OnPropertyChanged(nameof(ShowsNoSavedNote));
        OnPropertyChanged(nameof(OfferShowNearby));
        OnPropertyChanged(nameof(OffersLocationSettings));
        OnPropertyChanged(nameof(OffersWindowsList));
        OnPropertyChanged(nameof(LocationNoticeTitle));
        OnPropertyChanged(nameof(LocationNoticeSubtitle));
        OnPropertyChanged(nameof(HasRadio));
        OnPropertyChanged(nameof(CanToggleWifi));
        OnPropertyChanged(nameof(WifiSubtitle));
        OnPropertyChanged(nameof(WifiOn));
    }

    // ---- Reading ----

    private void Reload(bool scanAfter = false)
    {
        if (_client is not { } client || _location is not { } location)
        {
            return;
        }

        IsLoading = true;
        var asked = _userAskedNearby;
        var refusedBefore = _callRefused;
        LoadAsync(() =>
        {
            // The reason decides whether the gated calls run at all: a refused call can re-raise Windows' dialog, so
            // one that was refused in this visit isn't repeated until the user presses Refresh.
            var block = WifiLocationGate.Evaluate(WifiLocationGateReader.Read(location));
            var callGated = WifiLocationPolicy.Decide(block, asked).CallGated && !refusedBefore;
            return (Snapshot: client.ReadAsync(callGated).GetAwaiter().GetResult(), Block: block);
        }, result => Show(result.Snapshot, result.Block, scanAfter), onFailed: () =>
        {
            _loaded = true;
            IsLoading = false;
            NotifyState();
        }, longRunning: true, channel: "networks");
    }

    private void ReadRadio()
    {
        if (_radio is not { } radio)
        {
            return;
        }

        LoadAsync(radio.Read, reading =>
        {
            _radioPresent = reading.Present;
            _radioCanChange = reading.CanChange;
            _wifiOn = reading.IsOn;
            IsBusy = false;
            NotifyState();
        }, onFailed: () => IsBusy = false, longRunning: true, channel: "radio");
    }

    private void Show(WlanSnapshot snapshot, WifiLocationBlock block, bool scanAfter)
    {
        _loaded = true;
        _hasAdapter = snapshot.HasAdapter;
        _callRefused |= snapshot.ListRefused;
        _gatedCallsAllowed = snapshot.ListRead;
        _notice = WifiLocationNotice.For(block, _callRefused);
        _profiles = snapshot.Profiles;
        var rows = WifiNetworkList.Build(snapshot.Networks, snapshot.Profiles, snapshot.CurrentSsid);
        var visible = snapshot.ListRead ? rows : null;

        Networks.Clear();
        foreach (var row in rows)
        {
            Networks.Add(new WifiNetworkItem(row, _connectingKey is not null && _connectingKey == SsidText.ToHex(row.Ssid)));
        }

        var saved = WifiSavedNetworkList.Build(snapshot.SavedProfiles, snapshot.CurrentSsid, visible);
        ShowSaved(saved);
        _current = WifiCurrentNetworkBuilder.Build(snapshot.IsConnected, snapshot.CurrentSsid, snapshot.SignalBars, saved, visible);

        IsLoading = false;
        NotifyState();
        if (scanAfter)
        {
            RequestScan();
        }
    }

    /// <summary>
    /// Shows the saved rows, reusing each profile's item so a switch with a write in flight keeps waiting for it. The
    /// connected row also drives the Current network group's switch and Forget.
    /// </summary>
    private void ShowSaved(IReadOnlyList<WifiSavedNetworkRow> rows)
    {
        var items = new Dictionary<string, WifiSavedNetworkItem>(StringComparer.OrdinalIgnoreCase);
        SavedNetworks.Clear();
        _currentSaved = null;
        foreach (var row in rows)
        {
            // Profile names are unique per adapter; a second row with a name already taken gets its own item.
            var item = !items.ContainsKey(row.ProfileName) && _savedItems.TryGetValue(row.ProfileName, out var known)
                ? known
                : new WifiSavedNetworkItem(row, ChangeAutoConnect);
            item.Show(row, IsConnectingTo(row.ProfileName), CanEdit);
            items.TryAdd(row.ProfileName, item);
            SavedNetworks.Add(item);
            if (row.IsConnected && row.CanEdit)
            {
                _currentSaved ??= item;
            }
        }

        _savedItems = items;
    }

    private bool IsConnectingTo(string profileName) =>
        _connectingKey is not null && string.Equals(profileName, _targetProfile, StringComparison.OrdinalIgnoreCase);

    private void Refresh()
    {
        Problem = null;
        // An explicit Refresh may try a refused gated call once more (location access may have been turned on).
        _callRefused = false;
        RequestScan();
        Reload();
    }

    private void RequestScan()
    {
        // No scan while the gated calls are off: it would hit the same location check.
        if (_client is not { } client || !_gatedCallsAllowed || !_scanThrottle.TryBegin(DateTimeOffset.Now))
        {
            return;
        }

        Observe(client.ScanAsync(), "scan for Wi-Fi networks");
    }

    private void OnWlanChanged(WlanChange change)
    {
        // A WLAN service thread: only hand over to the dispatcher (never wait for it, WlanCloseHandle would deadlock).
        var generation = Volatile.Read(ref _open);
        Context.Dispatcher.BeginInvoke(() =>
        {
            if (generation != Volatile.Read(ref _open))
            {
                return;
            }

            if (change.Kind == WlanChangeKind.ConnectionFinished)
            {
                OnConnectionFinished(change);
            }

            // One re-read for a burst of events.
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void OnLocationChanged()
    {
        var generation = Volatile.Read(ref _open);
        Context.Dispatcher.BeginInvoke(() =>
        {
            if (generation != Volatile.Read(ref _open))
            {
                return;
            }

            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void OnRadioChanged()
    {
        var generation = Volatile.Read(ref _open);
        Context.Dispatcher.BeginInvoke(() =>
        {
            if (generation != Volatile.Read(ref _open))
            {
                return;
            }

            ReadRadio();
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    // ---- Switch ----

    private void SetRadio(bool on)
    {
        if (_radio is not { } radio)
        {
            return;
        }

        Problem = null;
        IsBusy = true;
        _writer.Run($"turn Wi-Fi {(on ? "on" : "off")}", () =>
        {
            try
            {
                return radio.Set(on);
            }
            finally
            {
                // Success, refusal or exception: show what the radio really reports.
                Context.Dispatcher.BeginInvoke(ReadRadio);
            }
        }, () => ReportWriteFailure("the Wi-Fi switch"));
    }

    // ---- Connecting ----

    private void Connect(WifiNetworkRow? row)
    {
        if (row is null || !CanChange || _client is null)
        {
            return;
        }

        Problem = null;
        _connectTimer.Stop();
        _target = row;
        _targetProfile = row.ProfileName;
        _connectingKey = null;
        var commands = _flow.Begin(row.Kind, row.IsSaved, DateTimeOffset.Now);
        Run(commands);
    }

    /// <summary>A saved network connects through the same flow as a saved visible one, so a rejected key re-prompts.</summary>
    private void ConnectSaved(WifiSavedNetworkItem? item)
    {
        if (item is not null)
        {
            Connect(WifiSavedNetworkList.ToNetworkRow(item.Row));
        }
    }

    /// <summary>
    /// Does what the flow said, in order. A password prompt ends the loop; the dialog's outcome continues it.
    /// <paramref name="key"/> is a copy of the typed password that this method (through <see cref="QueueConnect"/>) owns
    /// and always clears.
    /// </summary>
    private void Run(IReadOnlyList<WifiConnectCommand> commands, char[]? key = null)
    {
        bool? overwrite = null;
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case WifiConnectCommandKind.HandOff:
                    Context.OpenLink(WifiLink);
                    break;
                case WifiConnectCommandKind.DeleteProfile:
                    QueueDelete(_targetProfile);
                    break;
                case WifiConnectCommandKind.PromptPassword:
                    ClearKey(key);
                    PromptAndContinue(command.Flag);
                    return;
                case WifiConnectCommandKind.SetProfile:
                    if (key is null && _target is { } open && !WifiSecurity.NeedsPassword(open.Kind))
                    {
                        key = [];
                    }

                    if (key is null || !CanStore(key))
                    {
                        // No key to write, or one that can't be stored: give the attempt up (nothing was written yet).
                        ClearKey(key);
                        _connectTimer.Stop();
                        Run(_flow.Cancel(_flow.Attempt));
                        return;
                    }

                    overwrite = command.Flag;
                    _targetProfile = ProfileNameFor(command.Flag);
                    break;
                case WifiConnectCommandKind.Connect:
                    QueueConnect(overwrite is null ? null : key, overwrite ?? false);
                    key = null;
                    overwrite = null;
                    break;
            }
        }

        // A key without a connect after it never happens, but the buffer must not outlive the call.
        ClearKey(key);
    }

    private static void ClearKey(char[]? key)
    {
        if (key is not null)
        {
            Array.Clear(key);
        }
    }

    private void PromptAndContinue(bool retry)
    {
        if (_target is not { } target)
        {
            return;
        }

        var attempt = _flow.Attempt;
        var note = retry ? "The password was not accepted." : null;
        while (true)
        {
            var typed = Context.Services.Dialogs.PromptWifiPassword(target.Name, note);
            if (typed is null)
            {
                Run(_flow.Cancel(attempt));
                return;
            }

            try
            {
                var check = WifiPassphrase.Validate(target.Kind, typed);
                if (!check.IsValid)
                {
                    note = check.Message;
                    continue;
                }

                var commands = _flow.PasswordSubmitted(attempt, DateTimeOffset.Now);
                Run(commands, commands.Count == 0 ? null : (char[])typed.Clone());
                return;
            }
            finally
            {
                Array.Clear(typed);
            }
        }
    }

    /// <summary>True when the network's name and the key can go into a profile (no characters XML can't carry).</summary>
    private bool CanStore(ReadOnlySpan<char> key)
    {
        if (_target is { } target && WifiProfileXml.IsXmlSafe(target.Name) && WifiProfileXml.IsXmlSafe(key))
        {
            return true;
        }

        Problem = "This network's name or password has characters Windows can't store in a Wi-Fi profile.";
        return false;
    }

    /// <summary>A saved profile being replaced keeps its name; a new one gets the SSID text (made unique if need be).</summary>
    private string ProfileNameFor(bool overwrite)
    {
        var target = _target!;
        return overwrite && target.ProfileName is { } saved
            ? saved
            : WifiProfileName.Choose(target.Name, target.Ssid, _profiles);
    }

    private void QueueConnect(char[]? key, bool overwrite)
    {
        var client = _client;
        var profile = _targetProfile;
        var target = _target;
        var attempt = _flow.Attempt;
        if (client is null || profile is null || target is null || !CanEdit || _flow.State != WifiConnectState.Connecting)
        {
            ClearKey(key);
            return;
        }

        _connectingKey = SsidText.ToHex(target.Ssid);
        MarkConnecting();
        _connectTimer.Stop();
        _connectTimer.Start();
        var writeFailed = false;
        _writer.Run($"connect to the Wi-Fi network \"{profile}\"", () =>
        {
            try
            {
                if (key is not null)
                {
                    if (!WriteProfile(client, target, profile, key, overwrite))
                    {
                        writeFailed = true;
                        return false;
                    }

                    // Queued before the connect starts, so it reaches the flow before any outcome of the connect.
                    Context.Dispatcher.BeginInvoke(() => _flow.ProfileWritten(attempt));
                }

                return client.ConnectAsync(profile).GetAwaiter().GetResult();
            }
            finally
            {
                ClearKey(key);
            }
        }, () =>
        {
            if (writeFailed)
            {
                OnProfileWriteFailed(attempt);
            }
            else
            {
                OnConnectRequestFailed(attempt);
            }
        });
    }

    /// <summary>
    /// Writes the profile on the writer thread. A new network gets a whole profile; a saved one whose key was rejected
    /// keeps its own XML and only its key is replaced, so its other settings survive.
    /// </summary>
    private static bool WriteProfile(WlanClient client, WifiNetworkRow target, string profile, char[] key, bool overwrite)
    {
        WifiProfileDocument? document = null;
        try
        {
            if (overwrite)
            {
                var existing = client.GetProfileXmlAsync(profile).GetAwaiter().GetResult();
                if (existing is null)
                {
                    return false;
                }

                document = WifiProfileXml.ReplaceKey(existing, target.Kind, key, profile);
            }
            else
            {
                document = WifiProfileXml.Build(target.Ssid, target.Kind, target.CipherAlgorithm, key, profile);
            }

            return client.SetProfileAsync(document, overwrite).GetAwaiter().GetResult();
        }
        catch (ArgumentException ex)
        {
            // The message never names the key.
            Log.Warn($"Wi-Fi: could not build the profile \"{profile}\": {ex.Message}");
            return false;
        }
        finally
        {
            document?.Clear();
        }
    }

    /// <summary>
    /// Windows refused to save the profile, so this attempt owns nothing and deletes nothing: whatever is saved under
    /// that name (the profile may already have existed) is not ours.
    /// </summary>
    private void OnProfileWriteFailed(int attempt)
    {
        if (attempt != _flow.Attempt)
        {
            return;
        }

        _connectTimer.Stop();
        _connectingKey = null;
        _flow.ProfileWriteFailed(attempt);
        ReportWriteFailure($"saving the network \"{_target?.Name}\"");
        Reload();
    }

    private void MarkConnecting()
    {
        for (var i = 0; i < Networks.Count; i++)
        {
            var row = Networks[i].Row;
            if (_connectingKey == SsidText.ToHex(row.Ssid))
            {
                Networks[i] = new WifiNetworkItem(row, isConnecting: true);
            }
        }

        foreach (var item in SavedNetworks)
        {
            item.Show(item.Row, IsConnectingTo(item.ProfileName), CanEdit);
        }
    }

    private void OnConnectRequestFailed(int attempt)
    {
        if (attempt != _flow.Attempt)
        {
            return;
        }

        _connectTimer.Stop();
        _connectingKey = null;
        Run(_flow.Cancel(attempt));
        ReportWriteFailure($"connecting to \"{_target?.Name}\"");
        Reload();
    }

    private void OnConnectionFinished(WlanChange change)
    {
        if (_flow.State != WifiConnectState.Connecting
            || !string.Equals(change.ProfileName, _targetProfile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _connectTimer.Stop();
        var outcome = WlanReasons.Classify(change.ReasonCode);
        if (outcome != WlanReasonClass.Success)
        {
            Log.Warn($"Wi-Fi: connecting to \"{_target?.Name}\" failed (reason 0x{change.ReasonCode:X}, {outcome})");
        }

        _connectingKey = null;
        var commands = _flow.Result(_flow.Attempt, outcome);
        if (_flow.State == WifiConnectState.Failed)
        {
            Problem = outcome == WlanReasonClass.NetworkNotAvailable
                ? $"\"{_target?.Name}\" isn't reachable right now."
                : $"Couldn't connect to \"{_target?.Name}\".";
        }

        Reload();
        Run(commands);
    }

    private void OnConnectTimeout()
    {
        _connectTimer.Stop();
        var commands = _flow.CheckTimeout(_flow.Attempt, DateTimeOffset.Now);
        if (_flow.State != WifiConnectState.TimedOut)
        {
            return;
        }

        _connectingKey = null;
        Problem = $"Connecting to \"{_target?.Name}\" took too long.";
        Run(commands);
        Reload();
    }

    // ---- Disconnect and forget ----

    private void Disconnect()
    {
        if (_client is not { } client)
        {
            return;
        }

        Problem = null;
        _writer.Run($"disconnect from the Wi-Fi network \"{_current?.Name}\"",
            () => client.DisconnectAsync().GetAwaiter().GetResult(),
            () => ReportWriteFailure("disconnecting"));
    }

    /// <param name="parameter">The visible or saved row to forget.</param>
    private void Forget(object? parameter)
    {
        (string? profile, string name) = parameter switch
        {
            WifiNetworkItem network => (network.Row.ProfileName, network.Name),
            WifiSavedNetworkItem saved when saved.CanEdit => (saved.ProfileName, saved.Name),
            _ => ((string?)null, ""),
        };
        if (profile is null || _client is not { } client)
        {
            return;
        }

        if (!Context.Services.Dialogs.Confirm(
                $"Forget \"{name}\"?",
                "This PC will stop remembering the network and its password. You can connect again later by entering the password.",
                "Forget",
                isDestructive: true))
        {
            return;
        }

        Problem = null;
        IsBusy = true;
        _writer.Run($"forget the Wi-Fi network \"{profile}\"", () =>
        {
            try
            {
                return client.DeleteProfileAsync(profile).GetAwaiter().GetResult();
            }
            finally
            {
                Context.Dispatcher.BeginInvoke(() =>
                {
                    IsBusy = false;
                    Reload();
                });
            }
        }, () => ReportWriteFailure("forgetting the network"));
    }

    // ---- Connect automatically ----

    /// <summary>
    /// Rewrites the profile's connection mode on the writer thread, then re-reads: the switch shows what Windows saved,
    /// so a refused overwrite snaps it back. Never runs in safe mode (the switch is disabled there too).
    /// </summary>
    private void ChangeAutoConnect(WifiSavedNetworkItem item, VerifiedSwitch target, bool on)
    {
        var profile = item.ProfileName;
        var what = $"turn Connect automatically {(on ? "on" : "off")} for the Wi-Fi network \"{profile}\"";
        if (!CanEdit)
        {
            Log.Info($"Safe mode: did not {what}");
            return;
        }

        if (_client is not { } client)
        {
            return;
        }

        Problem = null;
        target.Begin();
        _writer.Run(what, () =>
        {
            try
            {
                return client.SetConnectionModeAsync(profile, on).GetAwaiter().GetResult();
            }
            finally
            {
                Context.Dispatcher.BeginInvoke(() =>
                {
                    target.End();
                    Reload();
                });
            }
        }, () => ReportWriteFailure($"Connect automatically for \"{item.Name}\""));
    }

    private void QueueDelete(string? profile)
    {
        if (profile is null || _client is not { } client || !CanEdit)
        {
            return;
        }

        _writer.Run($"delete the Wi-Fi profile \"{profile}\"",
            () => client.DeleteProfileAsync(profile).GetAwaiter().GetResult(),
            () => Log.Warn($"Wi-Fi: the profile \"{profile}\" could not be deleted; it stays saved and can be forgotten from the list"));
    }

    private static void Observe(Task task, string what) =>
        task.ContinueWith(t => Log.Warn($"Wi-Fi: could not {what}", t.Exception?.GetBaseException()), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
