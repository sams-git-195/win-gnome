using Windows.Devices.Enumeration;
using WinGnome.Core.Connectivity;
using WinGnome.Infrastructure;

namespace WinGnome.Services.Connectivity;

/// <summary>
/// The paired Bluetooth devices through two <see cref="DeviceWatcher"/>s (Bluetooth Classic and Low Energy endpoints
/// that are paired), and removing a pairing. Created when the Bluetooth panel opens; <see cref="Dispose"/> stops the
/// watchers in the background and releases them once they have reported <c>Stopped</c> (or after five seconds), so
/// the UI thread never waits for them.
/// </summary>
internal sealed class BluetoothClient : IDisposable
{
    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LowEnergyProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string IsPaired = "System.Devices.Aep.IsPaired";
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private const string IsPresent = "System.Devices.Aep.IsPresent";
    private const string ContainerId = "System.Devices.Aep.ContainerId";

    private const int ElementNotFound = unchecked((int)0x80070490);

    private static readonly string[] RequestedProperties = [IsPaired, IsConnected, IsPresent, ContainerId];
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnpairTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.Ordinal);
    private readonly List<Watching> _watchers = [];
    private bool _disposed;

    /// <summary>Raised on a thread-pool thread whenever the device set changes; subscribers coalesce and marshal to the UI themselves.</summary>
    public event Action? Changed;

    /// <summary>Creates and starts the watchers. Blocking (loads WinRT on first use): call it from a worker thread.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var protocol in new[] { ClassicProtocol, LowEnergyProtocol })
            {
                var filter = $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND {IsPaired}:=System.StructuredQueryType.Boolean#True";
                var watcher = DeviceInformation.CreateWatcher(filter, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
                var watching = new Watching(watcher);
                watcher.Added += OnAdded;
                watcher.Updated += OnUpdated;
                watcher.Removed += OnRemoved;
                watcher.Stopped += watching.OnStopped;
                _watchers.Add(watching);

                // Start only from a state that allows it; a watcher that was already started would throw.
                if (watcher.Status is DeviceWatcherStatus.Created or DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted)
                {
                    watcher.Start();
                }
            }
        }
    }

    /// <summary>The endpoints known right now.</summary>
    public IReadOnlyList<BluetoothEndpoint> Snapshot()
    {
        lock (_gate)
        {
            return _devices.Values.Select(ToEndpoint).ToList();
        }
    }

    /// <summary>Unpairs every endpoint. Blocking. False if Windows refused any of them.</summary>
    public static bool Unpair(IReadOnlyList<string> endpointIds)
    {
        var succeeded = true;
        foreach (var id in endpointIds)
        {
            // Unpairing the Classic endpoint of a dual-mode device usually takes its LE endpoint with it, so by the
            // time that one is reached it may no longer exist: that is the outcome we wanted, not a failure.
            DeviceInformation? info;
            try
            {
                info = Await(DeviceInformation.CreateFromIdAsync(id, RequestedProperties, DeviceInformationKind.AssociationEndpoint), UnpairTimeout);
            }
            catch (Exception ex) when (ex is not TimeoutException && ex.HResult == ElementNotFound)
            {
                info = null;
            }

            if (info is null)
            {
                Log.Info("Bluetooth: an endpoint was already gone when it was reached; counted as unpaired");
                continue;
            }

            var result = Await(info.Pairing.UnpairAsync(), UnpairTimeout);
            if (result.Status is not (DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired))
            {
                Log.Warn($"Bluetooth: removing a device was refused ({result.Status})");
                succeeded = false;
            }
        }

        return succeeded;
    }

    public void Dispose()
    {
        List<Watching> watchers;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watchers = [.. _watchers];
            _watchers.Clear();
            _devices.Clear();
        }

        Changed = null;
        if (watchers.Count > 0)
        {
            // Stopping is asynchronous and the handlers may only be detached after Stopped; never wait on the UI thread.
            _ = Task.Run(() => Teardown(watchers));
        }
    }

    private void Teardown(List<Watching> watchers)
    {
        foreach (var watching in watchers)
        {
            try
            {
                var watcher = watching.Watcher;
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }

                if (watcher.Status is not (DeviceWatcherStatus.Created or DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted)
                    && !watching.StoppedEvent.Wait(StopTimeout))
                {
                    Log.Warn($"Bluetooth: a device watcher did not report Stopped within {StopTimeout.TotalSeconds:0} s (status {watcher.Status})");
                }

                watcher.Added -= OnAdded;
                watcher.Updated -= OnUpdated;
                watcher.Removed -= OnRemoved;
                watcher.Stopped -= watching.OnStopped;
            }
            catch (Exception ex)
            {
                Log.Warn("Bluetooth: could not stop a device watcher", ex);
            }
            finally
            {
                watching.StoppedEvent.Dispose();
            }
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _devices[info.Id] = info;
        }

        Changed?.Invoke();
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (_disposed || !_devices.TryGetValue(update.Id, out var info))
            {
                return;
            }

            info.Update(update);
        }

        Changed?.Invoke();
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (_disposed || !_devices.Remove(update.Id))
            {
                return;
            }
        }

        Changed?.Invoke();
    }

    private static BluetoothEndpoint ToEndpoint(DeviceInformation info) => new(
        info.Id,
        info.Properties.TryGetValue(ContainerId, out var container) && container is Guid guid ? guid.ToString("D") : "",
        info.Name ?? "",
        Flag(info, IsPaired),
        Flag(info, IsConnected));

    private static bool Flag(DeviceInformation info, string key) => info.Properties.TryGetValue(key, out var value) && value is true;

    private static T Await<T>(Windows.Foundation.IAsyncOperation<T> operation, TimeSpan timeout)
    {
        var task = operation.AsTask();
        if (!task.Wait(timeout))
        {
            operation.Cancel();
            throw new TimeoutException("A Windows Bluetooth call did not finish in time.");
        }

        return task.Result;
    }

    /// <summary>A watcher with the event that says it has stopped.</summary>
    private sealed class Watching(DeviceWatcher watcher)
    {
        public DeviceWatcher Watcher { get; } = watcher;

        public ManualResetEventSlim StoppedEvent { get; } = new(false);

        public void OnStopped(DeviceWatcher sender, object args) => StoppedEvent.Set();
    }
}
