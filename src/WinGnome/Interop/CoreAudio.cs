using System.Runtime.InteropServices;

namespace WinGnome.Interop;

// Core Audio (MMDevice API + endpoint volume) COM declarations. Only the members WinGnome calls are declared
// by name; vtable slots before them are kept (as placeholders where unused) because COM dispatch is by position.
// Every Core Audio object is free-threaded, so the UI thread may call them without apartment marshalling.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

internal static class CoreAudio
{
    public const uint CLSCTX_ALL = 0x17;

    /// <summary>HRESULT_FROM_WIN32(ERROR_NOT_FOUND): there is no default endpoint (no audio device).</summary>
    public const int E_NOTFOUND = unchecked((int)0x80070490);

    /// <summary>EnumAudioEndpoints state mask for devices that are present and enabled.</summary>
    public const uint DEVICE_STATE_ACTIVE = 0x1;

    /// <summary>PKEY_Device_FriendlyName, e.g. "Speakers (Realtek(R) Audio)".</summary>
    public static readonly WindowProperties.PROPERTYKEY DeviceFriendlyNameKey = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out nint devices);

    IMMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out nint device);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object Activate(ref Guid iid, uint clsCtx, nint activationParams);

    /// <summary><paramref name="access"/> is an STGM value; 0 (STGM_READ) is all WinGnome needs.</summary>
    WindowProperties.IPropertyStore OpenPropertyStore(uint access);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetId();

    uint GetState();
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    uint GetCount();

    IMMDevice Item(uint index);
}

/// <summary>Lists the audio sessions (one per app stream) on an endpoint.</summary>
[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    void GetAudioSessionControl(nint sessionGuid, uint flags, out nint control);

    void GetSimpleAudioVolume(nint sessionGuid, uint flags, out nint volume);

    // IAudioSessionManager2
    IAudioSessionEnumerator GetSessionEnumerator();
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount();

    IAudioSessionControl2 GetSession(int index);
}

/// <summary>IAudioSessionControl2 with its IAudioSessionControl base methods repeated (COM dispatch is by vtable slot).</summary>
[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    AudioSessionState GetState();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetDisplayName();

    void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetIconPath();

    void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);

    Guid GetGroupingParam();

    void SetGroupingParam(ref Guid grouping, ref Guid eventContext);

    void RegisterAudioSessionNotification(nint events);

    void UnregisterAudioSessionNotification(nint events);

    // IAudioSessionControl2
    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetSessionIdentifier();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetSessionInstanceIdentifier();

    uint GetProcessId();

    /// <summary>S_OK (0) for the system sounds session, S_FALSE (1) otherwise.</summary>
    [PreserveSig]
    int IsSystemSoundsSession();
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

/// <summary>Volume of one audio session; obtained by QueryInterface on the session control.</summary>
[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);

    float GetMasterVolume();

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetMute();
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    uint GetChannelCount();

    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    float GetMasterVolumeLevel();

    float GetMasterVolumeLevelScalar();

    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

    float GetChannelVolumeLevel(uint channel);

    float GetChannelVolumeLevelScalar(uint channel);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetMute();
}

/// <summary>
/// Implemented by WinGnome; Core Audio calls it on its own MTA worker thread. <c>notifyData</c> points to an
/// AUDIO_VOLUME_NOTIFICATION_DATA, whose first field is the GUID event context passed by whoever made the change.
/// </summary>
[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(nint notifyData);
}

/// <summary>Implemented by WinGnome; Core Audio calls it on its own MTA worker thread.</summary>
[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, WindowProperties.PROPERTYKEY key);
}
