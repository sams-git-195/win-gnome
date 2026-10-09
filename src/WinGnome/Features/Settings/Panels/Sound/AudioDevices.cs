using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Sound;

/// <summary>An active playback or recording device.</summary>
internal sealed record AudioDevice(string Id, string Name);

/// <summary>
/// Core Audio for the Sound panel while it is open: device lists, the default devices and their volumes, per-app
/// volumes, and changing the default device. Created when the panel opens and disposed when it closes, which also
/// unregisters the device-change callback and releases every COM object.
/// </summary>
/// <remarks>
/// Core Audio objects are free-threaded. Device notifications arrive on Core Audio's own threads and are only
/// marshalled to the dispatcher (calling back into Core Audio from a notification can deadlock).
/// </remarks>
internal sealed class AudioDevices : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DeviceSink _sink;
    private IMMDeviceEnumerator? _enumerator;
    private bool _sinkRegistered;
    private bool _disposed;

    // Tags WinGnome's own volume changes, as AudioVolumeController does.
    private Guid _eventContext = Guid.NewGuid();

    public AudioDevices(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _sink = new DeviceSink(this);
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(_sink);
            _sinkRegistered = true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Core Audio is unavailable; the Sound panel can't list devices", ex);
        }
    }

    /// <summary>True when Core Audio could be reached.</summary>
    public bool IsAvailable => _enumerator is not null;

    /// <summary>Raised on the UI thread when a device is added, removed, enabled, disabled or made default.</summary>
    public event EventHandler? DevicesChanged;

    /// <summary>
    /// True when the undocumented IPolicyConfig interface answers, so the default device can be changed here.
    /// Checked by creating the object and asking for the interface; nothing is called on it.
    /// </summary>
    public static bool CanSetDefault()
    {
        object? client = null;
        try
        {
            client = new PolicyConfigClientComObject();
            return client is IPolicyConfig;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("IPolicyConfig is unavailable; choosing the default sound device links to Windows Settings", ex);
            return false;
        }
        finally
        {
            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="deviceId"/> the default device for every role (console, multimedia, communications), as
    /// Windows' sound settings do. Runs on a worker thread.
    /// </summary>
    public static bool SetDefault(string deviceId)
    {
        object? client = null;
        try
        {
            client = new PolicyConfigClientComObject();
            if (client is not IPolicyConfig policy)
            {
                Log.Warn("IPolicyConfig is unavailable; the default sound device was not changed");
                return false;
            }

            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                var result = policy.SetDefaultEndpoint(deviceId, role);
                if (result != 0)
                {
                    Log.Warn($"IPolicyConfig.SetDefaultEndpoint({role}) failed (HRESULT 0x{result:X8})");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Could not change the default sound device", ex);
            return false;
        }
        finally
        {
            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }
        }
    }

    /// <summary>The active devices of one direction, in Windows' order.</summary>
    public IReadOnlyList<AudioDevice> List(EDataFlow flow)
    {
        if (_enumerator is null)
        {
            return [];
        }

        var devices = new List<AudioDevice>();
        if (_enumerator.EnumAudioEndpoints(flow, CoreAudio.DEVICE_STATE_ACTIVE, out var pointer) != 0 || pointer == 0)
        {
            Log.Warn($"EnumAudioEndpoints({flow}) failed");
            return devices;
        }

        IMMDeviceCollection? collection = null;
        try
        {
            collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pointer);
            var count = collection.GetCount();
            for (uint i = 0; i < count; i++)
            {
                var device = collection.Item(i);
                try
                {
                    devices.Add(new AudioDevice(device.GetId(), FriendlyName(device)));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn($"Could not list the {flow} devices", ex);
        }
        finally
        {
            if (collection is not null)
            {
                Marshal.ReleaseComObject(collection);
            }

            Marshal.Release(pointer);
        }

        return devices;
    }

    /// <summary>The id of the default device for media, or null when there is none.</summary>
    public string? DefaultId(EDataFlow flow) => WithDefault(flow, device => device.GetId());

    /// <summary>Volume 0..1 and mute of the default device, or null when there is none.</summary>
    public (double Level, bool Muted)? ReadVolume(EDataFlow flow) => WithEndpointVolume<(double Level, bool Muted)?>(flow, endpoint =>
        (VolumeLevel.Clamp(endpoint.GetMasterVolumeLevelScalar()), endpoint.GetMute()));

    /// <summary>Sets the default device's volume; a non-zero level also unmutes, as GNOME does.</summary>
    public void SetVolume(EDataFlow flow, double level) => WithEndpointVolume(flow, endpoint =>
    {
        var clamped = VolumeLevel.Clamp(level);
        endpoint.SetMasterVolumeLevelScalar((float)clamped, ref _eventContext);
        if (clamped > 0 && endpoint.GetMute())
        {
            endpoint.SetMute(false, ref _eventContext);
        }

        return true;
    });

    public void SetMuted(EDataFlow flow, bool muted) => WithEndpointVolume(flow, endpoint =>
    {
        endpoint.SetMute(muted, ref _eventContext);
        return true;
    });

    /// <summary>
    /// The apps playing (or able to play) on the default output, each with its own volume. The caller owns the
    /// returned sessions and disposes them.
    /// </summary>
    public IReadOnlyList<AppVolume> ListApps(bool readOnly)
    {
        var apps = new List<AppVolume>();
        WithDefault(EDataFlow.Render, device =>
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            var manager = (IAudioSessionManager2)device.Activate(ref iid, CoreAudio.CLSCTX_ALL, 0);
            IAudioSessionEnumerator? sessions = null;
            try
            {
                sessions = manager.GetSessionEnumerator();
                var count = sessions.GetCount();
                for (var i = 0; i < count; i++)
                {
                    var control = sessions.GetSession(i);
                    if (AppVolume.TryCreate(control, ProcessDescription, readOnly) is { } app)
                    {
                        apps.Add(app);
                    }
                }
            }
            finally
            {
                if (sessions is not null)
                {
                    Marshal.ReleaseComObject(sessions);
                }

                Marshal.ReleaseComObject(manager);
            }

            return true;
        });
        return apps;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_enumerator is null)
        {
            return;
        }

        if (_sinkRegistered)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_sink);
            }
            catch (COMException ex)
            {
                Log.Warn("Could not unregister the Sound panel's device callback", ex);
            }
        }

        Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    private static string FriendlyName(IMMDevice device)
    {
        WindowProperties.IPropertyStore? store = null;
        try
        {
            store = device.OpenPropertyStore(0);
            var key = CoreAudio.DeviceFriendlyNameKey;
            if (store.GetValue(ref key, out var value) != 0)
            {
                return "Unknown device";
            }

            try
            {
                return value.AsString() is { Length: > 0 } name ? name : "Unknown device";
            }
            finally
            {
                WindowProperties.PropVariantClear(ref value);
            }
        }
        catch (COMException ex)
        {
            Log.Warn("Could not read an audio device's name", ex);
            return "Unknown device";
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    /// <summary>The program's description ("Spotify") or file name, for sessions that don't name themselves.</summary>
    private static string? ProcessDescription(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        var path = NativeMethods.GetProcessPath(processId);
        if (path is null)
        {
            return null;
        }

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(path) : description;
        }
        catch (FileNotFoundException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    private T? WithDefault<T>(EDataFlow flow, Func<IMMDevice, T> action)
    {
        if (_enumerator is null)
        {
            return default;
        }

        IMMDevice? device = null;
        try
        {
            device = _enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia);
            return action(device);
        }
        catch (COMException ex) when (ex.HResult == CoreAudio.E_NOTFOUND)
        {
            return default;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn($"Core Audio call on the default {flow} device failed", ex);
            return default;
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }
        }
    }

    private T? WithEndpointVolume<T>(EDataFlow flow, Func<IAudioEndpointVolume, T> action) => WithDefault(flow, device =>
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        var endpoint = (IAudioEndpointVolume)device.Activate(ref iid, CoreAudio.CLSCTX_ALL, 0);
        try
        {
            return action(endpoint);
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    });

    private void OnDevicesChanged() => _dispatcher.BeginInvoke(() =>
    {
        if (!_disposed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    });

    private sealed class DeviceSink(AudioDevices owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => Notify();

        public int OnDeviceAdded(string deviceId) => Notify();

        public int OnDeviceRemoved(string deviceId) => Notify();

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
            role == ERole.Multimedia ? Notify() : 0;

        public int OnPropertyValueChanged(string deviceId, WindowProperties.PROPERTYKEY key) => 0;

        private int Notify()
        {
            // Exceptions must never cross back into Core Audio.
            try
            {
                owner.OnDevicesChanged();
            }
            catch (Exception ex)
            {
                Log.Warn("Sound panel device notification failed", ex);
            }

            return 0;
        }
    }
}

/// <summary>One app's audio session on the default output and its volume. Owns its COM objects.</summary>
internal sealed class AppVolume : ObservableObject, IDisposable
{
    private IAudioSessionControl2? _control;
    private ISimpleAudioVolume? _volume;
    private double _level;
    private readonly bool _readOnly;
    private Guid _eventContext = Guid.NewGuid();

    private AppVolume(IAudioSessionControl2 control, ISimpleAudioVolume volume, string name, double level, bool readOnly)
    {
        _control = control;
        _volume = volume;
        Name = name;
        _level = level;
        _readOnly = readOnly;
    }

    public string Name { get; }

    /// <summary>0..1. Setting it changes the app's volume at once, except in read-only (safe) mode.</summary>
    public double Level
    {
        get => _level;
        set
        {
            if (_readOnly || _volume is null || !SetProperty(ref _level, VolumeLevel.Clamp(value)))
            {
                return;
            }

            try
            {
                _volume.SetMasterVolume((float)_level, ref _eventContext);
            }
            catch (COMException ex)
            {
                // The app most likely closed its stream; the next refresh drops it.
                Log.Warn($"Could not set the volume of {Name}", ex);
            }
        }
    }

    /// <summary>
    /// Wraps a session that is still alive, naming it from its display name or its process. Takes ownership of
    /// <paramref name="control"/> (released when the session is expired or can't be read).
    /// </summary>
    public static AppVolume? TryCreate(IAudioSessionControl2 control, Func<uint, string?> describeProcess, bool readOnly)
    {
        try
        {
            if (control.GetState() == AudioSessionState.Expired)
            {
                Marshal.ReleaseComObject(control);
                return null;
            }

            var name = control.IsSystemSoundsSession() == 0
                ? "System Sounds"
                : SystemInfoText.AudioSessionName(control.GetDisplayName(), describeProcess(control.GetProcessId()));
            var volume = (ISimpleAudioVolume)control;
            return new AppVolume(control, volume, name, VolumeLevel.Clamp(volume.GetMasterVolume()), readOnly);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Could not read an audio session", ex);
            Marshal.ReleaseComObject(control);
            return null;
        }
    }

    public void Dispose()
    {
        // _volume is the same COM object as _control (a QueryInterface of it), so one release frees both.
        _volume = null;
        if (_control is not null)
        {
            Marshal.ReleaseComObject(_control);
            _control = null;
        }
    }
}
