using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Master volume and mute of the default playback device through Core Audio, with live updates when the
/// volume is changed elsewhere (keyboard keys, Windows flyout, apps) or the default device changes.
/// </summary>
/// <remarks>
/// Core Audio calls our sinks on its own MTA worker threads and documents that calling back into the API
/// from inside a notification can deadlock, so every notification is only marshalled to the dispatcher,
/// where the state is re-read from the current endpoint.
/// </remarks>
internal sealed class AudioVolumeController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly VolumeSink _volumeSink;
    private readonly DeviceSink _deviceSink;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private bool _deviceSinkRegistered;
    private bool _disposed;

    // Tags our own volume changes so their echo notifications can be ignored (avoids slider jitter while dragging).
    private Guid _eventContext = Guid.NewGuid();

    public AudioVolumeController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _volumeSink = new VolumeSink(this);
        _deviceSink = new DeviceSink(this);

        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(_deviceSink);
            _deviceSinkRegistered = true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Core Audio is unavailable; the volume indicator is disabled", ex);
        }

        BindDefaultDevice();
    }

    /// <summary>True while a default playback device is bound.</summary>
    public bool IsAvailable => _endpoint is not null;

    /// <summary>Master volume, 0..1.</summary>
    public double Level { get; private set; }

    public bool IsMuted { get; private set; }

    /// <summary>Raised on the UI thread when availability, level or mute state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Sets the master volume; a non-zero level also unmutes, as GNOME and the Windows flyout do.</summary>
    public void SetLevel(double level)
    {
        if (_endpoint is null)
        {
            return;
        }

        level = VolumeLevel.Clamp(level);
        try
        {
            _endpoint.SetMasterVolumeLevelScalar((float)level, ref _eventContext);
            if (IsMuted && level > 0)
            {
                _endpoint.SetMute(false, ref _eventContext);
                IsMuted = false;
            }

            Level = level;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (COMException ex)
        {
            Log.Warn("Could not set the master volume", ex);
        }
    }

    public void SetMuted(bool muted)
    {
        if (_endpoint is null)
        {
            return;
        }

        try
        {
            _endpoint.SetMute(muted, ref _eventContext);
            IsMuted = muted;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (COMException ex)
        {
            Log.Warn("Could not change the mute state", ex);
        }
    }

    /// <summary>Applies a mouse-wheel delta (2 % per notch).</summary>
    public void Nudge(int wheelDelta) => SetLevel(VolumeLevel.Nudge(Level, wheelDelta));

    private void BindDefaultDevice()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseEndpoint();
        if (_enumerator is not null)
        {
            IMMDevice? device = null;
            object? activated = null;
            try
            {
                device = _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia);
                var iid = typeof(IAudioEndpointVolume).GUID;
                activated = device.Activate(ref iid, CoreAudio.CLSCTX_ALL, 0);
                var endpoint = (IAudioEndpointVolume)activated;
                endpoint.RegisterControlChangeNotify(_volumeSink);
                _endpoint = endpoint;
                activated = null;
                ReadState();
            }
            catch (COMException ex) when (ex.HResult == CoreAudio.E_NOTFOUND)
            {
                Log.Info("No default playback device; the volume indicator is hidden");
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Log.Warn("Could not bind the default playback device", ex);
            }
            finally
            {
                // Only set when activation succeeded but registering the callback failed.
                if (activated is not null)
                {
                    Marshal.ReleaseComObject(activated);
                }

                if (device is not null)
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReadState()
    {
        if (_endpoint is null)
        {
            return;
        }

        try
        {
            Level = VolumeLevel.Clamp(_endpoint.GetMasterVolumeLevelScalar());
            IsMuted = _endpoint.GetMute();
        }
        catch (COMException ex)
        {
            // The device was most likely unplugged; the default-device notification will rebind.
            Log.Warn("Could not read the master volume", ex);
        }
    }

    private void OnVolumeNotification(Guid context)
    {
        if (context == _eventContext)
        {
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            ReadState();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnDefaultDeviceChanged() => _dispatcher.BeginInvoke(BindDefaultDevice);

    private void ReleaseEndpoint()
    {
        if (_endpoint is null)
        {
            return;
        }

        try
        {
            _endpoint.UnregisterControlChangeNotify(_volumeSink);
        }
        catch (COMException ex)
        {
            Log.Warn("Could not unregister the volume callback", ex);
        }

        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseEndpoint();
        if (_enumerator is not null)
        {
            if (_deviceSinkRegistered)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_deviceSink);
                }
                catch (COMException ex)
                {
                    Log.Warn("Could not unregister the audio device callback", ex);
                }
            }

            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    /// <summary>Receives volume/mute changes of the bound endpoint.</summary>
    private sealed class VolumeSink(AudioVolumeController owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notifyData)
        {
            // Exceptions must never cross back into Core Audio.
            try
            {
                if (notifyData != 0)
                {
                    owner.OnVolumeNotification(Marshal.PtrToStructure<Guid>(notifyData));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Volume notification failed", ex);
            }

            return 0;
        }
    }

    /// <summary>Receives default-device changes so the controller follows the user's output device.</summary>
    private sealed class DeviceSink(AudioVolumeController owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia)
            {
                owner.OnDefaultDeviceChanged();
            }

            return 0;
        }

        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnPropertyValueChanged(string deviceId, WindowProperties.PROPERTYKEY key) => 0;
    }
}
