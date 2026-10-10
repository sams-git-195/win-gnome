using Windows.Devices.Radios;
using WinGnome.Infrastructure;

namespace WinGnome.Services.Connectivity;

/// <summary>What the machine's radios of one kind report.</summary>
/// <param name="Present">False when there is no radio of this kind.</param>
/// <param name="IsOn">True when a radio of this kind is on.</param>
/// <param name="CanChange">False when Windows reports the radio disabled (hardware switch, airplane mode or policy).</param>
internal readonly record struct RadioReading(bool Present, bool IsOn, bool CanChange)
{
    public static RadioReading None => new(false, false, false);
}

/// <summary>
/// The Wi-Fi or Bluetooth radio switch through <c>Windows.Devices.Radios</c> (documented; works unpackaged). Its calls
/// block, so they run on worker threads, each capped at <see cref="Timeout"/>. Created when a panel opens and
/// disposed when it closes; the first use loads the WinRT projection, which stays loaded for the process.
/// </summary>
internal sealed class RadioClient(RadioKind kind) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly List<Radio> _watched = [];
    private bool _disposed;

    /// <summary>Raised on a thread-pool thread when a radio of this kind changes state; subscribers marshal to the UI themselves.</summary>
    public event Action? Changed;

    /// <summary>Reads the radios and starts watching them. Blocking: call it from a worker thread.</summary>
    public RadioReading Read()
    {
        var radios = Await(Radio.GetRadiosAsync()).Where(r => r.Kind == kind).ToList();
        lock (_gate)
        {
            if (_disposed)
            {
                return RadioReading.None;
            }

            // Every read returns fresh Radio objects, so the previous ones are released before these are watched.
            foreach (var old in _watched)
            {
                old.StateChanged -= OnStateChanged;
            }

            _watched.Clear();
            foreach (var radio in radios)
            {
                radio.StateChanged += OnStateChanged;
                _watched.Add(radio);
            }
        }

        if (radios.Count == 0)
        {
            return RadioReading.None;
        }

        return new RadioReading(true, radios.Any(r => r.State == RadioState.On), radios.Any(r => r.State != RadioState.Disabled));
    }

    /// <summary>Turns the radios of this kind on or off. Blocking. False when Windows refused (access, hardware, policy).</summary>
    public bool Set(bool on)
    {
        var access = Await(Radio.RequestAccessAsync());
        if (access != RadioAccessStatus.Allowed)
        {
            Log.Warn($"Radios: access to the {kind} radio was not allowed ({access})");
            return false;
        }

        var succeeded = true;
        foreach (var radio in Await(Radio.GetRadiosAsync()).Where(r => r.Kind == kind))
        {
            var status = Await(radio.SetStateAsync(on ? RadioState.On : RadioState.Off));
            if (status != RadioAccessStatus.Allowed)
            {
                Log.Warn($"Radios: turning the {kind} radio {(on ? "on" : "off")} was refused ({status})");
                succeeded = false;
            }
        }

        return succeeded;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var radio in _watched)
            {
                radio.StateChanged -= OnStateChanged;
            }

            _watched.Clear();
        }

        Changed = null;
    }

    private void OnStateChanged(Radio sender, object args) => Changed?.Invoke();

    private static T Await<T>(Windows.Foundation.IAsyncOperation<T> operation)
    {
        var task = operation.AsTask();
        if (!task.Wait(Timeout))
        {
            operation.Cancel();
            throw new TimeoutException("A Windows radio call did not finish in time.");
        }

        return task.Result;
    }
}
