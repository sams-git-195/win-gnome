using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WinGnome.Core.Connectivity;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services.Connectivity;

/// <summary>What one read of the first Wi-Fi adapter found.</summary>
/// <param name="HasAdapter">False when there is no Wi-Fi adapter or the WLAN service isn't running.</param>
/// <param name="AccessDenied">True when Windows refused the network list (location access off or denied, or policy).</param>
/// <param name="Networks">The scan results, one entry per profile and security setup.</param>
/// <param name="Profiles">The names of every saved profile.</param>
/// <param name="CurrentSsid">The connected network's name, or null when not connected or it can't be read.</param>
/// <param name="IsConnected">Whether the adapter reports a connection.</param>
internal sealed record WlanSnapshot(
    bool HasAdapter,
    bool AccessDenied,
    IReadOnlyList<WifiAvailableNetwork> Networks,
    IReadOnlyList<string> Profiles,
    byte[]? CurrentSsid,
    bool IsConnected)
{
    public static WlanSnapshot NoAdapter { get; } = new(false, false, [], [], null, false);
}

/// <summary>An event from the WLAN service, parsed inside the callback (the native data is gone afterwards).</summary>
/// <param name="Kind">What happened.</param>
/// <param name="ReasonCode">For a finished connection attempt, its <c>WLAN_REASON_CODE</c>.</param>
/// <param name="ProfileName">For a finished connection attempt, the profile it used.</param>
internal readonly record struct WlanChange(WlanChangeKind Kind, uint ReasonCode, string? ProfileName);

internal enum WlanChangeKind
{
    /// <summary>A scan finished or Windows refreshed its list: re-read the networks.</summary>
    ListChanged,
    /// <summary>The connection, an adapter or a profile changed: re-read.</summary>
    StateChanged,
    /// <summary>A connection attempt finished (successfully or not).</summary>
    ConnectionFinished,
}

/// <summary>
/// One WLAN client handle on the first Wi-Fi adapter, with every call on its own worker thread so none of them can
/// block the UI. Created when the Wi-Fi panel opens and disposed when it closes. The notification callback runs on a
/// WLAN thread: it only parses the data and raises <see cref="Changed"/>, whose subscribers must hand over to the
/// dispatcher with <c>BeginInvoke</c>. (Never <c>Invoke</c>: <c>WlanCloseHandle</c> waits for running callbacks.)
/// </summary>
internal sealed class WlanClient : IDisposable
{
    // Offsets into the structures of wlanapi.h / wlantypes.h (x86 and x64 alike: no pointers before these fields).
    private const int NameChars = 256;
    private const int ListHeader = 8;
    private const int InterfaceInfoSize = 16 + NameChars * 2 + 4;
    private const int ProfileInfoSize = NameChars * 2 + 4;
    private const int NetworkSize = 628;
    private const int NetworkSsidLength = 512;
    private const int NetworkSsid = 516;
    private const int NetworkSignal = 604;
    private const int NetworkSecurity = 608;
    private const int NetworkAuth = 612;
    private const int NetworkCipher = 616;
    private const int NetworkFlags = 620;
    private const uint NetworkConnected = 0x1;
    private const uint NetworkHasProfile = 0x2;
    private const int ConnectionAttributesSsidLength = 520;
    private const int ConnectionAttributesSsid = 524;
    private const int InterfaceStateConnected = 1;
    private const int NotificationConnectionReason = 560;

    private const uint AcmScanComplete = 7;
    private const uint AcmScanFail = 8;
    private const uint AcmConnectionComplete = 10;
    private const uint AcmConnectionAttemptFail = 11;
    private const uint AcmInterfaceArrival = 13;
    private const uint AcmInterfaceRemoval = 14;
    private const uint AcmProfileChange = 15;
    private const uint AcmDisconnected = 21;
    private const uint AcmScanListRefresh = 26;

    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _worker;
    private readonly WlanNotificationCallback _callback;
    private nint _handle;
    private Guid _interface;
    private bool _disposed;

    public WlanClient()
    {
        // Held in a field for the client's whole life: native code keeps calling it, and a collected delegate would crash the process.
        _callback = OnNotification;
        _worker = new Thread(Work) { IsBackground = true, Name = "WinGnome Wi-Fi" };
        _worker.Start();
    }

    /// <summary>Raised on a WLAN thread for each event worth reacting to.</summary>
    public event Action<WlanChange>? Changed;

    /// <summary>Opens the handle (if needed) and reads adapter, networks, profiles and the current connection.</summary>
    public Task<WlanSnapshot> ReadAsync() => Enqueue(Read);

    /// <summary>Asks the adapter to scan; the result arrives as a <see cref="WlanChangeKind.ListChanged"/> event.</summary>
    public Task<bool> ScanAsync() => Enqueue(() => Succeeded("WlanScan", NativeMethods.WlanScan(_handle, _interface, 0, 0, 0)));

    /// <summary>Starts connecting with the saved profile; the outcome arrives as a <see cref="WlanChangeKind.ConnectionFinished"/> event.</summary>
    public Task<bool> ConnectAsync(string profileName) => Enqueue(() => Connect(profileName));

    public Task<bool> DisconnectAsync() => Enqueue(() => Succeeded("WlanDisconnect", NativeMethods.WlanDisconnect(_handle, _interface, 0)));

    public Task<bool> DeleteProfileAsync(string profileName) =>
        Enqueue(() => Succeeded($"WlanDeleteProfile \"{profileName}\"", NativeMethods.WlanDeleteProfile(_handle, _interface, profileName, 0)));

    /// <summary>
    /// Saves the profile as an all-user profile (as Windows Settings does), or as a per-user one when that is denied.
    /// The document's buffer is cleared before this returns, whatever happens.
    /// </summary>
    public Task<bool> SetProfileAsync(WifiProfileDocument document, bool overwrite) => Enqueue(() => SetProfile(document, overwrite));

    public void Dispose()
    {
        lock (_work)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // Last job: unregister, then close (which waits for running callbacks, hence on the worker), then end the thread.
            _work.Add(Close);
            _work.CompleteAdding();
        }
    }

    private Task<T> Enqueue<T>(Func<T> job)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_work)
        {
            if (_disposed)
            {
                source.SetException(new ObjectDisposedException(nameof(WlanClient)));
                return source.Task;
            }

            _work.Add(() =>
            {
                try
                {
                    source.SetResult(job());
                }
                catch (Exception ex)
                {
                    source.SetException(ex);
                }
            });
        }

        return source.Task;
    }

    private void Work()
    {
        foreach (var job in _work.GetConsumingEnumerable())
        {
            try
            {
                job();
            }
            catch (Exception ex)
            {
                // The job's own wrapper reports to its task; this only guards the thread itself.
                Log.Warn("Wi-Fi: a queued job failed", ex);
            }
        }
    }

    private void EnsureOpen()
    {
        if (_handle != 0)
        {
            return;
        }

        var result = NativeMethods.WlanOpenHandle(NativeMethods.WLAN_CLIENT_VERSION_VISTA, 0, out _, out var handle);
        if (result != 0)
        {
            throw new InvalidOperationException($"WlanOpenHandle failed with Win32 error {result}.");
        }

        _handle = handle;
        var registered = NativeMethods.WlanRegisterNotification(_handle, NativeMethods.WLAN_NOTIFICATION_SOURCE_ACM, false, _callback, 0, 0, out _);
        if (registered != 0)
        {
            Log.Warn($"Wi-Fi: WlanRegisterNotification failed with Win32 error {registered}; the list refreshes only when asked");
        }
    }

    private void Close()
    {
        if (_handle == 0)
        {
            return;
        }

        var unregistered = NativeMethods.WlanRegisterNotification(_handle, NativeMethods.WLAN_NOTIFICATION_SOURCE_NONE, false, null, 0, 0, out _);
        if (unregistered != 0)
        {
            Log.Warn($"Wi-Fi: unregistering the WLAN notifications failed with Win32 error {unregistered}");
        }

        var result = NativeMethods.WlanCloseHandle(_handle, 0);
        if (result != 0)
        {
            Log.Warn($"Wi-Fi: WlanCloseHandle failed with Win32 error {result}");
        }

        _handle = 0;
    }

    private WlanSnapshot Read()
    {
        EnsureOpen();
        if (!FindInterface(out var interfaceState))
        {
            return WlanSnapshot.NoAdapter;
        }

        var profiles = ReadProfiles();
        var denied = false;
        var networks = new List<WifiAvailableNetwork>();
        var listResult = NativeMethods.WlanGetAvailableNetworkList(_handle, _interface, 0, 0, out var list);
        if (listResult == 0)
        {
            try
            {
                ReadNetworks(list, networks);
            }
            finally
            {
                NativeMethods.WlanFreeMemory(list);
            }
        }
        else if (listResult == (uint)NativeMethods.ERROR_ACCESS_DENIED)
        {
            // Windows 11 24H2+ gates the list on location access (and policy can too); the call is what raises the consent prompt.
            Log.Warn("Wi-Fi: WlanGetAvailableNetworkList was refused (Win32 error 5); location access is probably off for WinGnome");
            denied = true;
        }
        else
        {
            throw new InvalidOperationException($"WlanGetAvailableNetworkList failed with Win32 error {listResult}.");
        }

        var connected = interfaceState == InterfaceStateConnected;
        return new WlanSnapshot(true, denied, networks, profiles, connected ? ReadCurrentSsid() : null, connected);
    }

    /// <summary>Picks the first Wi-Fi interface. False when there is none or the service isn't running.</summary>
    private bool FindInterface(out int state)
    {
        state = 0;
        var result = NativeMethods.WlanEnumInterfaces(_handle, 0, out var list);
        if (result == NativeMethods.ERROR_SERVICE_NOT_ACTIVE)
        {
            return false;
        }

        if (result != 0)
        {
            throw new InvalidOperationException($"WlanEnumInterfaces failed with Win32 error {result}.");
        }

        try
        {
            var count = Marshal.ReadInt32(list);
            if (count == 0)
            {
                    return false;
            }

            var item = list + ListHeader;
            var bytes = new byte[16];
            Marshal.Copy(item, bytes, 0, 16);
            _interface = new Guid(bytes);
            state = Marshal.ReadInt32(item + 16 + NameChars * 2);
            return true;
        }
        finally
        {
            NativeMethods.WlanFreeMemory(list);
        }
    }

    private List<string> ReadProfiles()
    {
        var names = new List<string>();
        var result = NativeMethods.WlanGetProfileList(_handle, _interface, 0, out var list);
        if (result != 0)
        {
            Log.Warn($"Wi-Fi: WlanGetProfileList failed with Win32 error {result}");
            return names;
        }

        try
        {
            var count = Marshal.ReadInt32(list);
            for (var i = 0; i < count; i++)
            {
                names.Add(Marshal.PtrToStringUni(list + ListHeader + i * ProfileInfoSize) ?? "");
            }
        }
        finally
        {
            NativeMethods.WlanFreeMemory(list);
        }

        return names;
    }

    private static void ReadNetworks(nint list, List<WifiAvailableNetwork> networks)
    {
        var count = Marshal.ReadInt32(list);
        for (var i = 0; i < count; i++)
        {
            var item = list + ListHeader + i * NetworkSize;
            var length = (int)Math.Min((uint)Marshal.ReadInt32(item + NetworkSsidLength), SsidText.MaxLength);
            var ssid = new byte[length];
            Marshal.Copy(item + NetworkSsid, ssid, 0, length);
            var flags = (uint)Marshal.ReadInt32(item + NetworkFlags);
            networks.Add(new WifiAvailableNetwork(
                ssid,
                Marshal.PtrToStringUni(item) ?? "",
                Marshal.ReadInt32(item + NetworkSignal),
                Marshal.ReadInt32(item + NetworkSecurity) != 0,
                Marshal.ReadInt32(item + NetworkAuth),
                Marshal.ReadInt32(item + NetworkCipher),
                (flags & NetworkConnected) != 0,
                (flags & NetworkHasProfile) != 0));
        }
    }

    /// <summary>The connected network's name; null when Windows refuses to say (location access) or it can't be read.</summary>
    private byte[]? ReadCurrentSsid()
    {
        var result = NativeMethods.WlanQueryInterface(_handle, _interface, NativeMethods.WLAN_INTF_OPCODE_CURRENT_CONNECTION, 0, out _, out var data, out _);
        if (result != 0)
        {
            if (result != (uint)NativeMethods.ERROR_ACCESS_DENIED && result != NativeMethods.ERROR_INVALID_STATE)
            {
                Log.Warn($"Wi-Fi: WlanQueryInterface(current connection) failed with Win32 error {result}");
            }

            return null;
        }

        try
        {
            var length = (int)Math.Min((uint)Marshal.ReadInt32(data + ConnectionAttributesSsidLength), SsidText.MaxLength);
            var ssid = new byte[length];
            Marshal.Copy(data + ConnectionAttributesSsid, ssid, 0, length);
            return ssid;
        }
        finally
        {
            NativeMethods.WlanFreeMemory(data);
        }
    }

    private bool Connect(string profileName)
    {
        var name = Marshal.StringToHGlobalUni(profileName);
        try
        {
            var parameters = new WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = 0, // wlan_connection_mode_profile
                strProfile = name,
                dot11BssType = 1,       // dot11_BSS_type_infrastructure
            };
            return Succeeded($"WlanConnect \"{profileName}\"", NativeMethods.WlanConnect(_handle, _interface, parameters, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(name);
        }
    }

    private unsafe bool SetProfile(WifiProfileDocument document, bool overwrite)
    {
        try
        {
            fixed (char* xml = document.Buffer)
            {
                var result = NativeMethods.WlanSetProfile(_handle, _interface, 0, xml, 0, overwrite, 0, out var reason);
                if (result == (uint)NativeMethods.ERROR_ACCESS_DENIED)
                {
                    Log.Warn($"Wi-Fi: an all-user profile for {document} was refused; saving it for this user only");
                    result = NativeMethods.WlanSetProfile(_handle, _interface, NativeMethods.WLAN_PROFILE_USER, xml, 0, overwrite, 0, out reason);
                }

                return Succeeded($"WlanSetProfile {document} (reason {reason})", result);
            }
        }
        finally
        {
            document.Clear();
        }
    }

    private static bool Succeeded(string call, uint result)
    {
        if (result == 0)
        {
            return true;
        }

        Log.Warn($"Wi-Fi: {call} failed with Win32 error {result}");
        return false;
    }

    private void OnNotification(nint data, nint context)
    {
        try
        {
            var notification = Marshal.PtrToStructure<WLAN_NOTIFICATION_DATA>(data);
            if (notification.NotificationSource != NativeMethods.WLAN_NOTIFICATION_SOURCE_ACM)
            {
                return;
            }

            var change = notification.NotificationCode switch
            {
                AcmScanComplete or AcmScanFail or AcmScanListRefresh => new WlanChange(WlanChangeKind.ListChanged, 0, null),
                AcmConnectionComplete or AcmConnectionAttemptFail => ReadConnectionResult(notification),
                AcmInterfaceArrival or AcmInterfaceRemoval or AcmProfileChange or AcmDisconnected => new WlanChange(WlanChangeKind.StateChanged, 0, null),
                _ => (WlanChange?)null,
            };
            if (change is { } value)
            {
                Changed?.Invoke(value);
            }
        }
        catch (Exception ex)
        {
            // Native-callback boundary: an exception here would end the process.
            Log.Warn("Wi-Fi: a WLAN notification could not be handled", ex);
        }
    }

    private static WlanChange ReadConnectionResult(WLAN_NOTIFICATION_DATA notification)
    {
        if (notification.pData == 0 || notification.dwDataSize < NotificationConnectionReason + 4)
        {
            return new WlanChange(WlanChangeKind.ConnectionFinished, 0, null);
        }

        // WLAN_CONNECTION_NOTIFICATION_DATA: the profile name follows the 4-byte connection mode.
        var profile = Marshal.PtrToStringUni(notification.pData + 4);
        var reason = (uint)Marshal.ReadInt32(notification.pData + NotificationConnectionReason);
        // A failed attempt that carries no reason is still a failure: 0x10001 is WLAN_REASON_CODE_UNKNOWN.
        const uint ReasonUnknown = 0x10001;
        return new WlanChange(WlanChangeKind.ConnectionFinished, notification.NotificationCode == AcmConnectionAttemptFail && reason == 0 ? ReasonUnknown : reason, profile);
    }
}
