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
