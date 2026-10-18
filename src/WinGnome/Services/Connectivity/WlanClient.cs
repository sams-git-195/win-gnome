using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Networking.Connectivity;
using WinGnome.Core.Connectivity;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services.Connectivity;

/// <summary>What one read of the first Wi-Fi adapter found.</summary>
/// <param name="HasAdapter">False when there is no Wi-Fi adapter or the WLAN service isn't running.</param>
/// <param name="ListRead">True when the network list was asked for and Windows gave it.</param>
/// <param name="ListRefused">True when the network list was asked for and Windows refused it (location access, policy).</param>
/// <param name="Networks">The scan results, one entry per profile and security setup.</param>
/// <param name="ProfilesRead">False when Windows refused the saved profile list, so an empty list means "unknown", not "none".</param>
/// <param name="Profiles">The names of every saved profile.</param>
/// <param name="SavedProfiles">Every saved profile with its parsed XML (never the key) and flags, in Windows' order.</param>
/// <param name="CurrentSsid">The connected network's name, or null when not connected or it can't be read.</param>
/// <param name="CurrentProfileName">The profile Windows connected with, or null when not connected or it can't be read.</param>
/// <param name="IsConnected">Whether the adapter reports a connection.</param>
/// <param name="SignalBars">Windows' 0 to 5 bars for the Wi-Fi connection, or null when not connected or unknown.</param>
internal sealed record WlanSnapshot(
    bool HasAdapter,
    bool ListRead,
    bool ListRefused,
    IReadOnlyList<WifiAvailableNetwork> Networks,
    bool ProfilesRead,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<WifiSavedProfile> SavedProfiles,
    byte[]? CurrentSsid,
    string? CurrentProfileName,
    bool IsConnected,
    int? SignalBars)
{
    public static WlanSnapshot NoAdapter { get; } = new(false, false, false, [], false, [], [], null, null, false, null);
}

/// <summary>A saved profile's XML (key encrypted) and who it belongs to. The XML is never logged.</summary>
internal sealed record WlanProfileText(string Xml, WifiProfileScope Scope);

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
    private const int ProfileInfoFlags = NameChars * 2;
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
    private const int ConnectionAttributesProfileName = 8;
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

    /// <summary>
    /// Opens the handle (if needed) and reads adapter, saved profiles and the current connection, and, when
    /// <paramref name="callGated"/>, the network list too. The gated calls (list, current connection) are the ones
    /// Windows ties to location access, so the caller passes false while access is denied or not yet asked for; the
    /// profiles and the connection's name and bars come from calls that aren't gated.
    /// </summary>
    public Task<WlanSnapshot> ReadAsync(bool callGated) => Enqueue(() => Read(callGated));

    /// <summary>The saved profile's XML, with its key still encrypted, and its scope; null when Windows has no such profile.</summary>
    public Task<WlanProfileText?> GetProfileTextAsync(string profileName) =>
        Enqueue(() => GetProfile(profileName, out var flags) is { } xml ? new WlanProfileText(xml, WifiProfileScopes.FromFlags(flags)) : null);

    /// <summary>Asks the adapter to scan; the result arrives as a <see cref="WlanChangeKind.ListChanged"/> event.</summary>
    public Task<bool> ScanAsync() => Enqueue(() => Succeeded("WlanScan", NativeMethods.WlanScan(_handle, _interface, 0, 0, 0)));

    /// <summary>Starts connecting with the saved profile; the outcome arrives as a <see cref="WlanChangeKind.ConnectionFinished"/> event.</summary>
    public Task<bool> ConnectAsync(string profileName) => Enqueue(() => Connect(profileName));

    public Task<bool> DisconnectAsync() => Enqueue(() => Succeeded("WlanDisconnect", NativeMethods.WlanDisconnect(_handle, _interface, 0)));

    public Task<bool> DeleteProfileAsync(string profileName) =>
        Enqueue(() => Succeeded($"WlanDeleteProfile \"{profileName}\"", NativeMethods.WlanDeleteProfile(_handle, _interface, profileName, 0)));

    /// <summary>
    /// Saves a new profile as an all-user profile (as Windows Settings does), or as a per-user one when that is denied.
    /// The document's buffer is cleared before this returns, whatever happens.
    /// </summary>
    public Task<bool> SaveNewProfileAsync(WifiProfileDocument document) => Enqueue(() => SetProfile(document, 0, overwrite: false));

    /// <summary>
    /// Replaces a saved profile in place, keeping it for all users or for this user only as <paramref name="scope"/>
    /// says, so no second profile of the same name appears. Refuses a Group Policy profile. The document's buffer is
    /// cleared before this returns, whatever happens.
    /// </summary>
    public Task<bool> OverwriteProfileAsync(WifiProfileDocument document, WifiProfileScope scope) => Enqueue(() => Overwrite(document, scope));

    /// <summary>
    /// Turns the saved profile's "Connect automatically" on or off by rewriting its <c>connectionMode</c> and nothing
    /// else (the key stays encrypted as Windows stored it). False, after logging why, when the profile is gone, is set
    /// by Group Policy (Windows refuses to change it) or Windows refuses the overwrite, which leaves it unchanged.
    /// </summary>
    public Task<bool> SetConnectionModeAsync(string profileName, bool auto) => Enqueue(() => SetConnectionMode(profileName, auto));

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

    /// <summary>Opens the handle once. False when the WLAN service isn't running (no Wi-Fi to show).</summary>
    private bool EnsureOpen()
    {
        if (_handle != 0)
        {
            return true;
        }

        var result = NativeMethods.WlanOpenHandle(NativeMethods.WLAN_CLIENT_VERSION_VISTA, 0, out _, out var handle);
        if (result == NativeMethods.ERROR_SERVICE_NOT_ACTIVE)
        {
            Log.Info("Wi-Fi: the WLAN service isn't running");
            return false;
        }

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

        return true;
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

    private WlanSnapshot Read(bool callGated)
    {
        if (!EnsureOpen() || !FindInterface(out var interfaceState))
        {
            return WlanSnapshot.NoAdapter;
        }

        var profiles = ReadProfiles();
        var saved = ReadSavedProfiles(profiles ?? []);
        var refused = false;
        var networks = new List<WifiAvailableNetwork>();
        nint list = 0;
        var listResult = callGated ? NativeMethods.WlanGetAvailableNetworkList(_handle, _interface, 0, 0, out list) : 0u;
        if (!callGated)
        {
            // Skipped on purpose: every refused call can re-raise Windows' location dialog.
        }
        else if (listResult == 0)
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
            refused = true;
        }
        else
        {
            throw new InvalidOperationException($"WlanGetAvailableNetworkList failed with Win32 error {listResult}.");
        }

        var listRead = callGated && !refused;
        var connected = interfaceState == InterfaceStateConnected;
        byte[]? current = null;
        string? currentProfile = null;
        int? bars = null;
        if (connected)
        {
            // The current connection is gated like the list, so it is only asked for when the list was given.
            (current, currentProfile) = listRead ? ReadCurrentConnection() : (null, null);
            var other = ReadConnectionWithoutWlanApi();
            current ??= other.Ssid;
            currentProfile ??= other.ProfileName;
            bars = other.Bars;
        }

        return new WlanSnapshot(true, listRead, refused, networks, profiles is not null, (profiles ?? []).ConvertAll(p => p.Name), saved,
            current, currentProfile, connected, bars);
    }

    /// <summary>
    /// The connected network's name, WLAN profile name and Windows' signal bars from the network list manager's
    /// connection profile, which works where the WLAN API is refused. Each is null when it doesn't say, or when the
    /// internet connection isn't Wi-Fi.
    /// </summary>
    private static (byte[]? Ssid, string? ProfileName, int? Bars) ReadConnectionWithoutWlanApi()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null || !profile.IsWlanConnectionProfile)
            {
                return (null, null, null);
            }

            int? bars = profile.GetSignalBars() is { } value ? value : null;
            var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            // For a Wi-Fi connection this is the WLAN profile's name, which tells two profiles for one network apart.
            var name = profile.ProfileName;
            return (string.IsNullOrEmpty(ssid) ? null : Encoding.UTF8.GetBytes(ssid), string.IsNullOrEmpty(name) ? null : name, bars);
        }
        catch (Exception ex)
        {
            Log.Warn("Wi-Fi: could not read the connected network's name and signal without the WLAN API", ex);
            return (null, null, null);
        }
    }

    /// <summary>
    /// The saved profile's XML, with its key encrypted (the plain-text flag is never passed), and its
    /// <c>WLAN_PROFILE_GROUP_POLICY</c> / <c>WLAN_PROFILE_USER</c> flags (see <see cref="WifiProfileScopes"/>); null, after
    /// logging, when Windows refuses.
    /// The XML is never logged.
    /// </summary>
    private string? GetProfile(string profileName, out uint flags)
    {
        flags = 0;
        var result = NativeMethods.WlanGetProfile(_handle, _interface, profileName, 0, out var xml, ref flags, out _);
        if (result != 0)
        {
            Log.Warn($"Wi-Fi: WlanGetProfile \"{profileName}\" failed with Win32 error {result}");
            flags = 0;
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(xml);
        }
        finally
        {
            NativeMethods.WlanFreeMemory(xml);
        }
    }

    /// <summary>One <c>WlanGetProfile</c> per saved profile, parsed at once: the XML goes no further than the parser.</summary>
    private List<WifiSavedProfile> ReadSavedProfiles(List<(string Name, uint Flags)> profiles)
    {
        var saved = new List<WifiSavedProfile>(profiles.Count);
        foreach (var (name, listFlags) in profiles)
        {
            var summary = WifiProfileXml.ReadSummary(GetProfile(name, out var profileFlags));
            if (summary is null)
            {
                Log.Warn($"Wi-Fi: the saved profile \"{name}\" could not be read; it is listed without its settings");
            }

            // The list's flags are known even when the profile itself can't be read.
            var scope = WifiProfileScopes.FromFlags(listFlags | profileFlags);
            saved.Add(new WifiSavedProfile(name, summary, scope == WifiProfileScope.GroupPolicy, scope == WifiProfileScope.ThisUser));
        }

        return saved;
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

    /// <summary>Every saved profile's name and flags; null, after logging, when Windows refuses the list.</summary>
    private List<(string Name, uint Flags)>? ReadProfiles()
    {
        var names = new List<(string Name, uint Flags)>();
        var result = NativeMethods.WlanGetProfileList(_handle, _interface, 0, out var list);
        if (result != 0)
        {
            Log.Warn($"Wi-Fi: WlanGetProfileList failed with Win32 error {result}");
            return null;
        }

        try
        {
            var count = Marshal.ReadInt32(list);
            for (var i = 0; i < count; i++)
            {
                var item = list + ListHeader + i * ProfileInfoSize;
                names.Add((Marshal.PtrToStringUni(item) ?? "", (uint)Marshal.ReadInt32(item + ProfileInfoFlags)));
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
    /// <summary>
    /// The connected network's name and the profile Windows connected with; nulls when Windows refuses to say (location
    /// access) or they can't be read.
    /// </summary>
    private (byte[]? Ssid, string? ProfileName) ReadCurrentConnection()
    {
        var result = NativeMethods.WlanQueryInterface(_handle, _interface, NativeMethods.WLAN_INTF_OPCODE_CURRENT_CONNECTION, 0, out _, out var data, out _);
        if (result != 0)
        {
            if (result != (uint)NativeMethods.ERROR_ACCESS_DENIED && result != NativeMethods.ERROR_INVALID_STATE)
            {
                Log.Warn($"Wi-Fi: WlanQueryInterface(current connection) failed with Win32 error {result}");
            }

            return (null, null);
        }

        try
        {
            var length = (int)Math.Min((uint)Marshal.ReadInt32(data + ConnectionAttributesSsidLength), SsidText.MaxLength);
            var ssid = new byte[length];
            Marshal.Copy(data + ConnectionAttributesSsid, ssid, 0, length);
            var profile = Marshal.PtrToStringUni(data + ConnectionAttributesProfileName);
            return (ssid, string.IsNullOrEmpty(profile) ? null : profile);
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

    private bool SetConnectionMode(string profileName, bool auto)
    {
        var xml = GetProfile(profileName, out var flags);
        if (xml is null)
        {
            return false;
        }

        var scope = WifiProfileScopes.FromFlags(flags);
        if (scope == WifiProfileScope.GroupPolicy)
        {
            Log.Warn($"Wi-Fi: \"{profileName}\" is set by Group Policy; Windows doesn't let its connection mode change");
            return false;
        }

        WifiProfileDocument document;
        try
        {
            document = WifiProfileXml.WithConnectionMode(xml, auto, profileName);
        }
        catch (ArgumentException ex)
        {
            // The message never contains the XML.
            Log.Warn($"Wi-Fi: could not change the connection mode of \"{profileName}\": {ex.Message}");
            return false;
        }

        // Windows replaces the profile in one step, so a refusal leaves the old one.
        return Overwrite(document, scope);
    }

    /// <summary>
    /// Saved back where it was (all users or this user only). No per-user fallback, which would add a second profile of
    /// that name beside the one it should replace.
    /// </summary>
    private bool Overwrite(WifiProfileDocument document, WifiProfileScope scope)
    {
        if (WifiProfileScopes.OverwriteFlags(scope) is not { } flags)
        {
            document.Clear();
            Log.Warn($"Wi-Fi: {document} is set by Group Policy; Windows doesn't let it change");
            return false;
        }

        return SetProfile(document, flags, overwrite: true);
    }

    /// <summary>Writes the profile from its pinned buffer, which is cleared before this returns, whatever happens.</summary>
    /// <param name="document">The profile.</param>
    /// <param name="flags">0 for all users, <see cref="NativeMethods.WLAN_PROFILE_USER"/> for this user only.</param>
    /// <param name="overwrite">True to replace a saved profile of the same name.</param>
    private unsafe bool SetProfile(WifiProfileDocument document, uint flags, bool overwrite)
    {
        try
        {
            fixed (char* xml = document.Buffer)
            {
                var result = NativeMethods.WlanSetProfile(_handle, _interface, flags, xml, 0, overwrite, 0, out var reason);
                // Never for an overwrite: a per-user profile of the same name would sit beside the all-user one it should replace.
                if (result == (uint)NativeMethods.ERROR_ACCESS_DENIED && !overwrite && flags == 0)
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
