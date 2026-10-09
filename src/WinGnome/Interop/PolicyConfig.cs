using System.Runtime.InteropServices;

namespace WinGnome.Interop;

// IPolicyConfig is the UNDOCUMENTED interface Windows' own sound settings use to change the default audio device;
// there is no documented API for it. The class id and the Windows 7+ interface id below have been stable from
// Windows 7 to Windows 11 25H2, but a Windows update could change or remove them, so callers must treat a failed
// QueryInterface or call as "not supported" and fall back to opening Windows Settings (see KI-060).
// Only SetDefaultEndpoint is called; the slots before it are placeholders that keep the vtable positions right.

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClientComObject
{
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    void GetMixFormat();

    void GetDeviceFormat();

    void ResetDeviceFormat();

    void SetDeviceFormat();

    void GetProcessingPeriod();

    void SetProcessingPeriod();

    void GetShareMode();

    void SetShareMode();

    void GetPropertyValue();

    void SetPropertyValue();

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
}
