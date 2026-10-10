using Windows.Security.Authorization.AppCapabilityAccess;
using WinGnome.Core.Connectivity;
using WinGnome.Infrastructure;

namespace WinGnome.Services.Connectivity;

/// <summary>
/// Reads, without calling any location-gated Wi-Fi API, whether Windows lets this app use them
/// (<c>AppCapability("wiFiControl").CheckAccess()</c>), and reports when that changes. The panel feeds the answer to
/// Core's <see cref="WifiLocationPolicy"/> so a denied state is never probed again. Created when the Wi-Fi panel
/// opens and disposed when it closes; <see cref="Check"/> may block briefly (it loads WinRT), so call it from a worker.
/// </summary>
internal sealed class WifiLocationProbe : IDisposable
{
    private readonly object _gate = new();
    private AppCapability? _capability;
    private bool _disposed;
    private bool _logged;

    /// <summary>Raised on a thread-pool thread when Windows reports the access changed; subscribers marshal to the UI themselves.</summary>
    public event Action? Changed;

    /// <summary>The current access. <see cref="WifiLocationAccess.Unknown"/> when it can't be read, so the calls are tried as before.</summary>
    public WifiLocationAccess Check()
    {
        try
        {
            AppCapability capability;
            lock (_gate)
            {
                if (_disposed)
                {
                    return WifiLocationAccess.Unknown;
                }

                if (_capability is null)
                {
                    _capability = AppCapability.Create("wiFiControl");
                    _capability.AccessChanged += OnAccessChanged;
                }

                capability = _capability;
            }

            var status = capability.CheckAccess();
            if (!_logged)
            {
                _logged = true;
                Log.Info($"Wi-Fi: location access for Wi-Fi calls is {status}");
            }

            return status switch
            {
                AppCapabilityAccessStatus.Allowed => WifiLocationAccess.Allowed,
                AppCapabilityAccessStatus.DeniedByUser => WifiLocationAccess.DeniedByUser,
                AppCapabilityAccessStatus.DeniedBySystem => WifiLocationAccess.DeniedBySystem,
                AppCapabilityAccessStatus.UserPromptRequired => WifiLocationAccess.UserPromptRequired,
                _ => WifiLocationAccess.Unknown,
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Wi-Fi: could not read the location access state", ex);
            return WifiLocationAccess.Unknown;
        }
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
            if (_capability is not null)
            {
                _capability.AccessChanged -= OnAccessChanged;
                _capability = null;
            }
        }

        Changed = null;
    }

    private void OnAccessChanged(AppCapability sender, AppCapabilityAccessChangedEventArgs args) => Changed?.Invoke();
}
