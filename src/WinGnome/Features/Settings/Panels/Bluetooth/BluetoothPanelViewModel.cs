using System.Collections.ObjectModel;
using System.Windows.Input;
using Windows.Devices.Radios;
using WinGnome.Core.Connectivity;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Services.Connectivity;

namespace WinGnome.Features.Settings.Panels.Bluetooth;

/// <summary>One row of the Devices list.</summary>
/// <param name="Row">The merged paired device.</param>
internal sealed record BluetoothDeviceItem(BluetoothDeviceRow Row)
{
    public string Name => Row.Name;

    public string Status => Row.Status;

    public bool IsConnected => Row.IsConnected;

    /// <summary>Connecting and disconnecting a paired device has no documented API, so a disconnected one points at Windows Settings.</summary>
    public bool ShowsConnectInWindows => !Row.IsConnected;
}

/// <summary>
/// Bluetooth: the Bluetooth switch (the radio) and the paired devices with whether each is connected, Remove Device,
/// and links to Windows Settings for pairing and for connecting. The devices come from two device watchers that run
/// only while the panel is open. Pairing new devices stays in Windows Settings in this version.
/// </summary>
internal sealed class BluetoothPanelViewModel : SystemPanelViewModel
{
    private const string BluetoothLink = "ms-settings:bluetooth";

    private readonly SystemSettingWriter _writer;
    private readonly BluetoothDeviceList _devices = new();
    private RadioClient? _radio;
    private BluetoothClient? _client;
    private int _open;
    private int _refreshQueued;
    private bool _radioRead;
    private bool _radioPresent;
    private bool _radioCanChange = true;
    private bool _bluetoothOn;
    private bool _isBusy;
    private bool _devicesRead;

    public BluetoothPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Bluetooth)
    {
        _writer = context.CreateWriter();
        AddDeviceCommand = new RelayCommand(() => context.OpenLink(BluetoothLink));
        ConnectInWindowsCommand = new RelayCommand(() => context.OpenLink(BluetoothLink));
        RemoveCommand = new RelayCommand(parameter => Remove(parameter as BluetoothDeviceItem), _ => CanChange);
    }

    public ObservableCollection<BluetoothDeviceItem> Devices { get; } = [];

    public ICommand AddDeviceCommand { get; }

    public ICommand ConnectInWindowsCommand { get; }

    /// <summary>Parameter: the <see cref="BluetoothDeviceItem"/>.</summary>
    public ICommand RemoveCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanChange));
                OnPropertyChanged(nameof(CanToggleBluetooth));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanChange => CanEdit && !IsBusy;

    public bool HasRadio => _radioPresent;

    public bool ShowsNoAdapter => _radioRead && !_radioPresent;

    public bool CanToggleBluetooth => CanChange && _radioPresent && _radioCanChange;

    public string BluetoothSubtitle => _radioCanChange
        ? "Turn the Bluetooth radio on or off."
        : "Bluetooth is turned off by a hardware switch, Airplane Mode or policy.";

    /// <summary>
    /// The Bluetooth switch. Setting it asks Windows to switch the radio and then shows what the radio reports; the
    /// field is only set from a read, so a refused change snaps back.
    /// </summary>
    public bool BluetoothOn
    {
        get => _bluetoothOn;
        set
        {
            if (value == _bluetoothOn || !CanToggleBluetooth)
            {
                OnPropertyChanged();
                return;
            }

            SetRadio(value);
        }
    }

    public bool HasDevices => Devices.Count > 0;

    public bool ShowsEmptyNote => _devicesRead && Devices.Count == 0 && _radioPresent;

    protected override void Open()
    {
        Interlocked.Increment(ref _open);
        Devices.Clear();
        _devices.Clear();
        _radioRead = false;
        _devicesRead = false;
        NotifyState();

        if (Context.Options.SelfTest)
        {
            // An unattended run must not start device watchers or touch a radio.
            Log.Info("Bluetooth panel: self-test, not reading radios or devices");
            _radioRead = true;
            _devicesRead = true;
            NotifyState();
            return;
        }

        _radio = new RadioClient(RadioKind.Bluetooth);
        _radio.Changed += OnRadioChanged;
        _client = new BluetoothClient();
        _client.Changed += OnDevicesChanged;
        ReadRadio();
        var client = _client;
        LoadAsync(() =>
        {
            client.Start();
            return client.Snapshot();
        }, Show, onFailed: () =>
        {
            _devicesRead = true;
            NotifyState();
        }, longRunning: true, channel: "devices");
    }

    protected override void Close()
    {
        Interlocked.Increment(ref _open);
        if (_client is { } client)
        {
            client.Changed -= OnDevicesChanged;
            client.Dispose();
            _client = null;
        }

        if (_radio is { } radio)
        {
            radio.Dispose();
            _radio = null;
        }

        Devices.Clear();
        _devices.Clear();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasRadio));
        OnPropertyChanged(nameof(ShowsNoAdapter));
        OnPropertyChanged(nameof(CanToggleBluetooth));
        OnPropertyChanged(nameof(BluetoothSubtitle));
        OnPropertyChanged(nameof(BluetoothOn));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(ShowsEmptyNote));
    }

    private void ReadRadio()
    {
        if (_radio is not { } radio)
        {
            return;
        }

        LoadAsync(radio.Read, reading =>
        {
            _radioRead = true;
            _radioPresent = reading.Present;
            _radioCanChange = reading.CanChange;
            _bluetoothOn = reading.IsOn;
            IsBusy = false;
            NotifyState();
        }, onFailed: () =>
        {
            _radioRead = true;
            IsBusy = false;
            NotifyState();
        }, longRunning: true, channel: "radio");
    }

    private void Show(IReadOnlyList<BluetoothEndpoint> endpoints)
    {
        _devices.Clear();
        foreach (var endpoint in endpoints)
        {
            _devices.Upsert(endpoint);
        }

        Devices.Clear();
        foreach (var row in _devices.Rows())
        {
            Devices.Add(new BluetoothDeviceItem(row));
        }

        _devicesRead = true;
        NotifyState();
    }

    private void OnDevicesChanged()
    {
        // A thread-pool thread, one event per device while enumerating: queue a single re-read.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        var generation = Volatile.Read(ref _open);
        Context.Dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _refreshQueued, 0);
            if (generation != Volatile.Read(ref _open) || _client is not { } client)
            {
                return;
            }

            LoadAsync(client.Snapshot, Show, channel: "devices");
        });
    }

    private void OnRadioChanged()
    {
        var generation = Volatile.Read(ref _open);
        Context.Dispatcher.BeginInvoke(() =>
        {
            if (generation == Volatile.Read(ref _open))
            {
                ReadRadio();
            }
        });
    }

    private void SetRadio(bool on)
    {
        if (_radio is not { } radio)
        {
            return;
        }

        Problem = null;
        IsBusy = true;
        _writer.Run($"turn Bluetooth {(on ? "on" : "off")}", () =>
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
        }, () => ReportWriteFailure("the Bluetooth switch"));
    }

    private void Remove(BluetoothDeviceItem? item)
    {
        if (item is null || _client is null)
        {
            return;
        }

        if (!Context.Services.Dialogs.Confirm(
                $"Remove \"{item.Name}\"?",
                "The device will be unpaired. You will need to pair it again to use it with this PC.",
                "Remove",
                isDestructive: true))
        {
            return;
        }

        Problem = null;
        IsBusy = true;
        var ids = item.Row.EndpointIds;
        _writer.Run($"remove the Bluetooth device \"{item.Name}\"", () =>
        {
            try
            {
                return BluetoothClient.Unpair(ids);
            }
            finally
            {
                Context.Dispatcher.BeginInvoke(() => IsBusy = false);
            }
        }, () => ReportWriteFailure("removing the device"));
    }
}
