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

        _client = new WlanClient();
        _client.Changed += OnWlanChanged;
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
        IReadOnlyList<WifiConnectCommand> cancel = _flow.Cancel(_flow.Attempt);
        Run(cancel);
        // Moves the flow on to a fresh attempt number, so a result that arrives after the panel closed is ignored.
        _flow.Begin(WifiProfileKind.HandOff, isSaved: false, DateTimeOffset.Now);

        var client = _client;
        _client = null;
        if (client is not null)
        {
            client.Changed -= OnWlanChanged;
            // A delete queued above still needs the handle, so the handle closes behind it on the writer's queue.
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
        OnPropertyChanged(nameof(HasRadio));
        OnPropertyChanged(nameof(CanToggleWifi));
        OnPropertyChanged(nameof(WifiSubtitle));
        OnPropertyChanged(nameof(WifiOn));
    }

    // ---- Reading ----

    private void Reload(bool scanAfter = false)
    {
        if (_client is not { } client)
        {
            return;
        }

        IsLoading = true;
        LoadAsync(() => client.ReadAsync().GetAwaiter().GetResult(), snapshot => Show(snapshot, scanAfter), onFailed: () =>
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

    private void Show(WlanSnapshot snapshot, bool scanAfter)
    {
        _loaded = true;
        _hasAdapter = snapshot.HasAdapter;
        _locationDenied = snapshot.AccessDenied;
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
        if (scanAfter && snapshot.HasAdapter && !snapshot.AccessDenied)
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
        if (_client is not { } client || !_scanThrottle.TryBegin(DateTimeOffset.Now))
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

    /// <summary>Does what the flow said, in order. A password prompt ends the loop; the dialog's outcome continues it.</summary>
    private void Run(IReadOnlyList<WifiConnectCommand> commands, WifiProfileDocument? document = null)
    {
        (WifiProfileDocument? Document, bool Overwrite)? pendingSet = null;
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
                    document?.Clear();
                    PromptAndContinue(command.Flag);
                    return;
                case WifiConnectCommandKind.SetProfile:
                    if (document is null && _target is { } open && !WifiSecurity.NeedsPassword(open.Kind))
                    {
                        document = TryBuildDocument([]);
                    }

                    if (document is null)
                    {
                        // No profile to write (the password was refused or the name can't be stored): give the attempt up.
                        Run(_flow.Cancel(_flow.Attempt));
                        return;
                    }

                    pendingSet = (document, command.Flag);
                    break;
                case WifiConnectCommandKind.Connect:
                    QueueConnect(pendingSet?.Document, pendingSet?.Overwrite ?? false);
                    pendingSet = null;
                    document = null;
                    break;
            }
        }

        // A set without a connect after it never happens, but a built buffer must not outlive the call.
        document?.Clear();
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
                var document = commands.Count == 0 ? null : TryBuildDocument(typed);
                Run(commands, document);
                return;
            }
            finally
            {
                Array.Clear(typed);
            }
        }
    }

    private WifiProfileDocument? TryBuildDocument(ReadOnlySpan<char> key)
    {
        if (_target is not { } target)
        {
            return null;
        }

        try
        {
            // A saved profile being replaced keeps its name; a new one gets the SSID text (made unique if need be).
            var name = target.IsSaved && target.ProfileName is { } saved
                ? saved
                : WifiProfileName.Choose(target.Name, target.Ssid, _profiles);
            _targetProfile = name;
            return WifiProfileXml.Build(target.Ssid, target.Kind, target.CipherAlgorithm, key, name);
        }
        catch (ArgumentException ex)
        {
            // The message never names the key.
            Log.Warn($"Wi-Fi: could not build a profile for \"{target.Name}\": {ex.Message}");
            Problem = "This network's name or password has characters Windows can't store in a Wi-Fi profile.";
            return null;
        }
    }

    private void QueueConnect(WifiProfileDocument? document, bool overwrite)
    {
        var client = _client;
        var profile = _targetProfile;
        var attempt = _flow.Attempt;
        if (client is null || profile is null || !CanEdit || _flow.State != WifiConnectState.Connecting)
        {
            document?.Clear();
            return;
        }

        _connectingKey = _target is { } target ? SsidText.ToHex(target.Ssid) : null;
        MarkConnecting();
        _connectTimer.Stop();
        _connectTimer.Start();
        _writer.Run($"connect to the Wi-Fi network \"{profile}\"", () =>
        {
            try
            {
                if (document is not null && !client.SetProfileAsync(document, overwrite).GetAwaiter().GetResult())
                {
                    return false;
                }

                return client.ConnectAsync(profile).GetAwaiter().GetResult();
            }
            finally
            {
                document?.Clear();
            }
        }, () => OnConnectRequestFailed(attempt));
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
