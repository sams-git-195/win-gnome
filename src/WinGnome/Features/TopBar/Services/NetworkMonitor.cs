using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Services;

/// <summary>Tracks whether the machine is online over Wi-Fi, a wire, or not at all.</summary>
internal sealed class NetworkMonitor : IDisposable
{
    // Address changes arrive in bursts (DHCP, IPv6 privacy addresses, adapters coming up); coalesce them.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private bool _disposed;
    private int _probeCount;

    public NetworkMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _debounce = new DispatcherTimer(Debounce, DispatcherPriority.Background, OnDebounceElapsed, dispatcher) { IsEnabled = false };
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _ = RefreshAsync();
    }

    public NetworkConnection Connection { get; private set; }

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

    private async Task RefreshAsync()
    {
        // Enumerating adapters and their IP properties can take tens of milliseconds; keep it off the UI thread.
        // Probes may overlap; only the latest one may publish, or a slow stale probe could overwrite a newer state.
        var probe = ++_probeCount;
        var connection = await Task.Run(Probe).ConfigureAwait(true);
        if (_disposed || probe != _probeCount || connection is not { } value || value == Connection)
        {
            return;
        }

        Connection = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static NetworkConnection? Probe()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Select(Describe).ToList();
            return NetworkStatus.Evaluate(adapters);
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn("Could not enumerate network adapters", ex);
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
    }
}
