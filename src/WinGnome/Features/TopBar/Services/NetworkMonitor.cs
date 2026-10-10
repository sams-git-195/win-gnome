using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using WinRtNetworkInformation = Windows.Networking.Connectivity.NetworkInformation;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Tracks whether the machine is online over Wi-Fi, a wire, or not at all, and how strong the Wi-Fi signal is. The
/// signal comes from <c>ConnectionProfile.GetSignalBars</c>, which unlike the WLAN API does not need location access.
/// Windows raises no event when only the signal changes, so while on Wi-Fi a slow one-shot timer re-reads it.
/// </summary>
internal sealed class NetworkMonitor : IDisposable
{
    // Address changes arrive in bursts (DHCP, IPv6 privacy addresses, adapters coming up); coalesce them.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly Dispatcher _dispatcher;
    private static readonly TimeSpan SignalInterval = TimeSpan.FromSeconds(45);

    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _signalTimer;
    private bool _disposed;
    private int _probeCount;

    public NetworkMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _debounce = new DispatcherTimer(Debounce, DispatcherPriority.Background, OnDebounceElapsed, dispatcher) { IsEnabled = false };
        _signalTimer = new DispatcherTimer(SignalInterval, DispatcherPriority.Background, OnSignalElapsed, dispatcher) { IsEnabled = false };
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _ = RefreshAsync();
    }

    public NetworkConnection Connection { get; private set; }

    /// <summary>Windows' 0 to 5 signal bars for the Wi-Fi connection, or null when not on Wi-Fi or unreadable.</summary>
    public int? SignalBars { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Connection"/> changes.</summary>
    public event EventHandler? Changed;

    // NetworkChange raises its events on thread-pool threads.
    private void OnNetworkChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        if (_disposed)
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    });

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _ = RefreshAsync();
    }

    private void OnSignalElapsed(object? sender, EventArgs e)
    {
        _signalTimer.Stop();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        // Enumerating adapters and their IP properties can take tens of milliseconds; keep it off the UI thread.
        // Probes may overlap; only the latest one may publish, or a slow stale probe could overwrite a newer state.
        var probe = ++_probeCount;
        var result = await Task.Run(Probe).ConfigureAwait(true);
        if (_disposed || probe != _probeCount)
        {
            return;
        }

        // Re-arm only while on Wi-Fi; a failed probe keeps whatever was last known.
        var connection = result?.Connection ?? Connection;
        if (connection == NetworkConnection.Wireless)
        {
            _signalTimer.Start();
        }

        if (result is not { } probed || (probed.Connection == Connection && probed.SignalBars == SignalBars))
        {
            return;
        }

        Connection = probed.Connection;
        SignalBars = probed.SignalBars;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private readonly record struct ProbeResult(NetworkConnection Connection, int? SignalBars);

    private static ProbeResult? Probe()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Select(Describe).ToList();
            var connection = NetworkStatus.Evaluate(adapters);
            return new ProbeResult(connection, connection == NetworkConnection.Wireless ? ReadSignalBars() : null);
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn("Could not enumerate network adapters", ex);
            return null;
        }
    }

    private static int _signalReadFailing;

    private static int? ReadSignalBars()
    {
        try
        {
            var profile = WinRtNetworkInformation.GetInternetConnectionProfile();
            var bars = profile is { IsWlanConnectionProfile: true } && profile.GetSignalBars() is { } value ? (int)value : (int?)null;
            if (Interlocked.Exchange(ref _signalReadFailing, 0) == 1)
            {
                Log.Info("Reading the Wi-Fi signal bars works again");
            }

            return bars;
        }
        catch (Exception ex)
        {
            // Feature boundary: WinRT can throw COMException or UnauthorizedAccessException; the icon falls back to the
            // full wedge. The read repeats every 45 s, so only the first failure of a run is logged.
            if (Interlocked.Exchange(ref _signalReadFailing, 1) == 0)
            {
                Log.Warn("Could not read the Wi-Fi signal bars", ex);
            }

            return null;
        }
    }

    private static NetworkAdapter Describe(NetworkInterface nic)
    {
        var kind = nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => NetworkAdapterKind.Wireless,
            NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel => NetworkAdapterKind.Ignored,
            _ => NetworkStatus.IsVirtualAdapter(nic.Description) ? NetworkAdapterKind.Ignored : NetworkAdapterKind.Wired,
        };

        var isUp = nic.OperationalStatus == OperationalStatus.Up;
        var hasGateway = isUp && kind != NetworkAdapterKind.Ignored && nic.GetIPProperties().GatewayAddresses
            .Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
        return new NetworkAdapter(kind, isUp, hasGateway);
    }

    public void Dispose()
    {
        _disposed = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _debounce.Stop();
        _signalTimer.Stop();
    }
}
