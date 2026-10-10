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

    /// <summary>Signal strength as the cellular-bars glyphs of Segoe Fluent Icons (one to four bars).</summary>
    public string SignalGlyph => Row.SignalLevel switch
    {
        <= 1 => "",
        2 => "",
        3 => "",
        _ => "",
    };
}

/// <summary>
/// Wi-Fi: the Wi-Fi switch, the visible networks with connect, disconnect and forget, and links for the rest. Reads
/// happen when the panel opens and when Windows reports a change (no polling); every Windows call runs off the UI
/// thread. Connecting follows <see cref="WifiConnectFlow"/>, which decides what to write, when a failed attempt must
/// delete the profile it made, and when to ask for the password again. Passwords only ever live in <c>char[]</c>
/// buffers that are cleared as soon as the profile is written, and are never logged.
/// </summary>
internal sealed class WifiPanelViewModel : SystemPanelViewModel
{
    private const string WifiLink = "ms-settings:network-wifi";
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly SystemSettingWriter _writer;
    private readonly WifiConnectFlow _flow = new(ConnectTimeout);
    private readonly ScanThrottle _scanThrottle = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _connectTimer;
    private WlanClient? _client;
    private RadioClient? _radio;
    private WifiLocationProbe? _location;
    private bool _userAskedNearby;
    private bool _gatedCallsAllowed;
    private bool _offerShowNearby;
    private int _open;
    private IReadOnlyList<string> _profiles = [];
    private WifiNetworkRow? _target;
    private string? _targetProfile;
    private string? _connectingKey;
    private bool _isLoading;
    private bool _hasAdapter;
    private bool _locationDenied;
    private bool _connectedWithoutName;
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
        ConnectCommand = new RelayCommand(parameter => Connect(parameter as WifiNetworkItem), _ => CanChange);
        DisconnectCommand = new RelayCommand(parameter => Disconnect(parameter as WifiNetworkItem), _ => CanChange);
        ForgetCommand = new RelayCommand(parameter => Forget(parameter as WifiNetworkItem), _ => CanChange);
        OpenLocationSettingsCommand = new RelayCommand(() => context.OpenLink("ms-settings:privacy-location"));
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

    public ICommand RefreshCommand { get; }

    /// <summary>Parameter: the <see cref="WifiNetworkItem"/>.</summary>
    public ICommand ConnectCommand { get; }

    public ICommand DisconnectCommand { get; }

    public ICommand ForgetCommand { get; }

    public ICommand OpenLocationSettingsCommand { get; }

    public ICommand AirplaneModeCommand { get; }

    public ICommand HotspotCommand { get; }

    public ICommand HiddenNetworkCommand { get; }

    /// <summary>Makes the first location-gated call, which is what raises Windows' consent prompt.</summary>
    public ICommand ShowNearbyCommand { get; }

    /// <summary>True when access hasn't been asked for yet, so the notice offers "Show nearby networks" instead of a link to Settings.</summary>
    public bool OfferShowNearby => _offerShowNearby;

    public bool OffersLocationSettings => !_offerShowNearby;

    public string LocationNoticeTitle => _offerShowNearby
        ? "Windows needs your permission to show nearby networks"
        : "Windows needs location access to show nearby networks";

    public string LocationNoticeSubtitle => _offerShowNearby
        ? "Windows will ask whether WinGnome may use your location. Turning Wi-Fi on or off and disconnecting work without it."
        : "Turn on location access for WinGnome (and for desktop apps) in Windows Settings, then press Refresh. Turning Wi-Fi on or off and disconnecting still work.";

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

    /// <summary>True when the list of networks (not the location notice or the empty states) shows.</summary>
    public bool ShowsList => _hasAdapter && !_locationDenied;

    public bool ShowsLocationNotice => _hasAdapter && _locationDenied;

    /// <summary>"Connected" without the network's name, when Windows won't say which one (location access).</summary>
    public bool ShowsConnectedWithoutName => _hasAdapter && _connectedWithoutName;

    public bool ShowsEmptyListNote => ShowsList && !_isLoading && Networks.Count == 0;

    protected override void Open()
    {
        Interlocked.Increment(ref _open);
        Networks.Clear();
        _loaded = false;
        _hasAdapter = false;
        _locationDenied = false;
        _connectedWithoutName = false;
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
        _connectingKey = null;
        _target = null;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(ShowsNoAdapter));
        OnPropertyChanged(nameof(ShowsList));
        OnPropertyChanged(nameof(ShowsLocationNotice));
        OnPropertyChanged(nameof(ShowsConnectedWithoutName));
        OnPropertyChanged(nameof(ShowsEmptyListNote));
        OnPropertyChanged(nameof(OfferShowNearby));
        OnPropertyChanged(nameof(OffersLocationSettings));
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
        LoadAsync(() =>
        {
            // The access state decides whether the gated calls run at all: a refused call can re-raise Windows' dialog.
            var decision = WifiLocationPolicy.Decide(location.Check(), asked);
            return (Snapshot: client.ReadAsync(decision.CallGated).GetAwaiter().GetResult(), Decision: decision);
        }, result => Show(result.Snapshot, result.Decision, scanAfter), onFailed: () =>
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

    private void Show(WlanSnapshot snapshot, WifiLocationDecision decision, bool scanAfter)
    {
        _loaded = true;
        _hasAdapter = snapshot.HasAdapter;
        _locationDenied = snapshot.AccessDenied || decision.ShowNotice;
        _offerShowNearby = decision.OfferShowNearby && !snapshot.AccessDenied;
        _gatedCallsAllowed = decision.CallGated && !snapshot.AccessDenied;
        _profiles = snapshot.Profiles;
        var rows = WifiNetworkList.Build(snapshot.Networks, snapshot.Profiles, snapshot.CurrentSsid);
        _connectedWithoutName = snapshot.IsConnected && snapshot.CurrentSsid is null && !rows.Any(r => r.IsConnected);

        Networks.Clear();
        foreach (var row in rows)
        {
            Networks.Add(new WifiNetworkItem(row, _connectingKey is not null && _connectingKey == SsidText.ToHex(row.Ssid)));
        }

        IsLoading = false;
        NotifyState();
        if (scanAfter)
        {
            RequestScan();
        }
    }

    private void Refresh()
    {
        Problem = null;
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

    private void Connect(WifiNetworkItem? item)
    {
        if (item is null || !CanChange || _client is null)
        {
            return;
        }

        Problem = null;
        _connectTimer.Stop();
        _target = item.Row;
        _targetProfile = item.Row.ProfileName;
        _connectingKey = null;
        var commands = _flow.Begin(item.Row.Kind, item.Row.IsSaved, DateTimeOffset.Now);
        Run(commands);
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

    private void Disconnect(WifiNetworkItem? item)
    {
        if (item is null || _client is not { } client)
        {
            return;
        }

        Problem = null;
        _writer.Run($"disconnect from the Wi-Fi network \"{item.Name}\"",
            () => client.DisconnectAsync().GetAwaiter().GetResult(),
            () => ReportWriteFailure("disconnecting"));
    }

    private void Forget(WifiNetworkItem? item)
    {
        if (item?.Row.ProfileName is not { } profile || _client is not { } client)
        {
            return;
        }

        if (!Context.Services.Dialogs.Confirm(
                $"Forget \"{item.Name}\"?",
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
