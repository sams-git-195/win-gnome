using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>
/// <c>WLAN_CONNECTION_PARAMETERS</c> (wlanapi.h). Only profile mode is used, so the SSID and BSSID list stay null.
/// <paramref name="strProfile"/> points at a profile name the caller keeps allocated for the call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_CONNECTION_PARAMETERS
{
    public int wlanConnectionMode;
    public nint strProfile;
    public nint pDot11Ssid;
    public nint pDesiredBssidList;
    public int dot11BssType;
    public uint dwFlags;
}

/// <summary><c>WLAN_NOTIFICATION_DATA</c> (wlanapi.h); <c>pData</c> is valid only while the callback runs.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_NOTIFICATION_DATA
{
    public uint NotificationSource;
    public uint NotificationCode;
    public Guid InterfaceGuid;
    public uint dwDataSize;
    public nint pData;
}

/// <summary>Called on a WLAN service thread. Must not block and must not call back into the WLAN API.</summary>
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void WlanNotificationCallback(nint data, nint context);

/// <summary>
/// Native Wifi API (wlanapi.dll) for the Wi-Fi panel. Lists returned by the service are read with fixed offsets from
/// the documented structures (see <c>WlanClient</c>) and freed with <see cref="WlanFreeMemory"/>.
/// </summary>
internal static partial class NativeMethods
{
    public const uint WLAN_CLIENT_VERSION_VISTA = 2;
    public const int ERROR_SUCCESS = 0;
    public const uint ERROR_INVALID_HANDLE = 6;
    public const uint ERROR_INVALID_STATE = 5023;
    public const uint ERROR_SERVICE_NOT_ACTIVE = 1062;
    public const uint ERROR_NOT_FOUND = 1168;
    public const uint WLAN_NOTIFICATION_SOURCE_NONE = 0;
    public const uint WLAN_NOTIFICATION_SOURCE_ACM = 0x8;
    public const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;
    public const uint WLAN_PROFILE_USER = 0x2;

    [DllImport("wlanapi.dll")]
    public static extern uint WlanOpenHandle(uint dwClientVersion, nint pReserved, out uint pdwNegotiatedVersion, out nint phClientHandle);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanCloseHandle(nint hClientHandle, nint pReserved);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanEnumInterfaces(nint hClientHandle, nint pReserved, out nint ppInterfaceList);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanGetAvailableNetworkList(nint hClientHandle, in Guid pInterfaceGuid, uint dwFlags, nint pReserved, out nint ppAvailableNetworkList);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanGetProfileList(nint hClientHandle, in Guid pInterfaceGuid, nint pReserved, out nint ppProfileList);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanQueryInterface(nint hClientHandle, in Guid pInterfaceGuid, int opCode, nint pReserved, out uint pdwDataSize, out nint ppData, out int pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanScan(nint hClientHandle, in Guid pInterfaceGuid, nint pDot11Ssid, nint pIeData, nint pReserved);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanConnect(nint hClientHandle, in Guid pInterfaceGuid, in WLAN_CONNECTION_PARAMETERS pConnectionParameters, nint pReserved);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanDisconnect(nint hClientHandle, in Guid pInterfaceGuid, nint pReserved);

    /// <summary>The profile XML is passed as a pinned <c>char*</c>, never a managed string, so the key can be cleared afterwards.</summary>
    [DllImport("wlanapi.dll")]
    public static extern unsafe uint WlanSetProfile(nint hClientHandle, in Guid pInterfaceGuid, uint dwFlags, char* strProfileXml, nint strAllUserProfileSecurity, [MarshalAs(UnmanagedType.Bool)] bool bOverwrite, nint pReserved, out uint pdwReasonCode);

    /// <summary>Gets a profile's XML (the key stays encrypted without the plain-text flag); free it with <see cref="WlanFreeMemory"/>.</summary>
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    public static extern uint WlanGetProfile(nint hClientHandle, in Guid pInterfaceGuid, string strProfileName, nint pReserved, out nint pstrProfileXml, nint pdwFlags, out uint pdwGrantedAccess);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    public static extern uint WlanDeleteProfile(nint hClientHandle, in Guid pInterfaceGuid, string strProfileName, nint pReserved);

    [DllImport("wlanapi.dll")]
    public static extern uint WlanRegisterNotification(nint hClientHandle, uint dwNotifSource, [MarshalAs(UnmanagedType.Bool)] bool bIgnoreDuplicate, WlanNotificationCallback? funcCallback, nint pCallbackContext, nint pReserved, out uint pdwPrevNotifSource);

    [DllImport("wlanapi.dll")]
    public static extern void WlanFreeMemory(nint pMemory);
}
