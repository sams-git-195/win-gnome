# 0019 — Native Wi-Fi, Network and Bluetooth panels, and the Settings redirect

Status: Agreed — user decisions 2026-10-09; advisor review (Fable) applied 2026-10-09. Delivered in three parts; part (a) after spec 0016 WP2.
Implemented 2026-10-10 on branch `feature/0019-wifi-bluetooth`: part (b) Wi-Fi and the Bluetooth panel of part (c). **Deferred:** part (a)
(Win+I redirect, depends on spec 0016) and the Network panel (wired, VPN, proxy) of part (c); `Network` stays a link.
Live write checks (Wi-Fi and Bluetooth switches, connect, forget, remove device) are pending (KI-108 to KI-111).

### Implementation notes (parts b and c, as built)
Deviations from the design below, all deliberate to keep v1 small and safe:
- **Radios**: spike item 2 (`WlanSetInterface` as a standard user) can't be checked without toggling the radio, so the fallback
  decision was taken: **both** Wi-Fi and Bluetooth use `Windows.Devices.Radios` through `RadioClient`; the Wi-Fi panel therefore
  loads WinRT. The Wi-Fi list itself is still the Native Wifi API.
- **Airplane Mode** is the link row to `ms-settings:network-airplanemode` (no `IRadioManager`).
- **Wi-Fi not built**: the *Saved Networks…* dialog (*Forget* is on each saved row), captive-portal detection and *Sign In*
  (`INetworkListManager`), reopening the WLAN handle after a WlanSvc restart or resume (the panel shows the problem banner; reopen
  the panel), the 802.1X hand-off for *saved* enterprise networks (they only show as "Enterprise").
- **Bluetooth not built**: discovery and pairing dialogs (*Add Device…* opens `ms-settings:bluetooth`), battery levels, device
  category icons, `DeviceWatcherLifecycle` and `PairingPrompt` in Core; only the two paired watchers exist (stopped in the
  background, then released). A paired device that isn't connected offers *Connect in Windows Settings*.
- **Profile ownership**: `WifiConnectFlow` owns a profile only after `ProfileWritten` (Windows confirmed `WlanSetProfile`);
  `ProfileWriteFailed` fails the attempt and deletes nothing. Closing or leaving the panel mid-attempt is `Abandon()`: no
  delete (Windows keeps connecting), results ignored. An overwrite after an authentication failure reads the saved XML with
  `WlanGetProfile` and replaces only the `sharedKey` (`WifiProfileXml.ReplaceKey`), never falls back to a per-user profile,
  and a saved enterprise network connects from the panel (only unsaved ones hand off).
- **Location**: `AppCapability("wiFiControl").CheckAccess()` decides (Core `WifiLocationPolicy`) whether the list, current
  connection and scans may be called at all; see KI-108.
- Reason codes: the values in `WlanReasons` were checked against `wlanapi.h`/`l2cmn.h` of SDK 10.0.26100 (AC 0x20000, MSM 0x30000,
  MSMSEC 0x40000, 802.1X 0x50000, profile 0x80000).
- Spike item 1 (read-only, this machine): `WlanGetAvailableNetworkList` returned `ERROR_ACCESS_DENIED` because location services are
  switched off by the device administrator, and Windows showed its "Location has been turned off" dialog naming WinGnome; the panel
  showed the location notice as designed.

## Problem
Spec 0015 left the Connectivity group (Wi-Fi, Network, Bluetooth) as links to Windows Settings, so the most-used
quick settings tiles still throw the user into Windows Settings, and Win+I always opens Windows Settings even when
WinGnome replaces the taskbar. The user wants GNOME-style native panels for the three and, in WinGnome-dock mode,
WinGnome Settings to be *the* Settings app. Spec 0015 listed these as phase 2.

## Deliverables
This is an umbrella spec delivered as three independently shippable parts, each with its own QA pass and review:

| Part | Contents | Depends on |
|---|---|---|
| **(a) Redirect, catalogue, top-bar tiles** | Win+I redirect through spec 0016's input host, `WindowsSettingsRedirect`, the new setting and its General row, dock/app grid/overview launch redirect, the catalogue mechanism that makes quick settings tiles open Native panels | Spec 0016 WP1a (`ShortcutRouter`/`ShortcutConfig`) and WP2 (`InputHookHost`) |
| **(b) Wi-Fi** | Wi-Fi panel and airplane mode; flips `PanelIds.Wifi` to Native | Part (a) not required; WP0 spike items 1–4, 10, 11 |
| **(c) Network + Bluetooth** | Network panel (wired, VPN, proxy) and Bluetooth panel; flips `PanelIds.Network` and `PanelIds.Bluetooth` to Native | WP0 spike items 5–7, 9 |

Part (a) ships with the mechanism in place but no Connectivity panel Native yet; the redirect and tiles only ever
target Native panels, so (b) and (c) light up their pages simply by flipping their catalogue entry.

## Behaviour
Decisions by the user on 2026-10-09, in substance:

**Wi-Fi panel** (GNOME layout, `PanelIds.Wifi`, now Native — part (b)):
- Header switch *Wi-Fi* (on/off) and an *Airplane Mode* row ("Disables Wi-Fi, Bluetooth and mobile broadband"). The
  airplane row is hidden when the machine has no radios. In v1 it is a real switch only if the spike shows
  `IRadioManager` is clean (reads and writes Windows' airplane state as a standard user, quick settings agrees);
  otherwise the row is a link to `ms-settings:network-airplanemode`.
- *Visible Networks*: one row per SSID with signal bars (4 levels), a padlock when secured, a tick and "Connected"
  on the current one. Order: connected, then saved, then by signal, then name. Hidden SSIDs are not listed.
  *Refresh* triggers at most one `WlanScan` per 5 s (later presses within the window are ignored).
- Clicking a row connects. Saved or open networks connect at once; a secured unsaved network shows a GNOME
  *Authentication required* dialog (password, *Show password*, Connect/Cancel). A wrong password re-opens the
  dialog with "The password was not accepted"; the profile created for that attempt is deleted, so a bad password is
  never left saved. **A saved network whose stored key is now wrong** (the router's password changed) fails with an
  authentication reason: the same dialog opens; *Connect* overwrites the saved profile with the new key, and from
  then on that attempt owns the profile for cleanup (a wrong retry deletes it; *Cancel* after the overwrite deletes
  it too, since the old key was already known bad). WPA/WPA2/WPA3-Enterprise (802.1X), and any network whose
  security WinGnome can't map, hand off to `ms-settings:network-wifi` (primary: it works with the taskbar hidden);
  `ms-availablenetworks:` is not used as the first choice because Windows' flyout likely needs Explorer's taskbar
  (spike item 9). Captive portals: when connected with limited connectivity the row says "Sign in required" and a
  *Sign In* button opens `http://www.msftconnecttest.com/redirect` (what Windows itself opens).
- The connected row's menu: *Disconnect*, *Forget*. *Saved Networks…* dialog lists every saved profile with *Forget*.
  Forgetting an all-user profile another account created may be refused by Windows; the panel then shows the
  problem banner with *Open in Windows Settings*.
- Footer: *Wi-Fi Hotspot…* (`ms-settings:network-mobilehotspot`), *Connect to Hidden Network…*
  (`ms-settings:network-wifi`) and *Open in Windows Settings*.
- Location: on Windows 11 24H2+ listing networks and reading the connected network's name need location access.
  WinGnome's first `WlanGetAvailableNetworkList` call **is** what makes Windows show its location consent prompt
  (attributed to WinGnome). When access is denied — by the user, by the location master switch, or by policy — the
  list is replaced by "Windows needs location access to show nearby networks" with *Open Location Settings*
  (`ms-settings:privacy-location`), and the connected row shows "Connected" without the network name. On/off,
  airplane mode, saved networks and disconnect still work.
- No Wi-Fi adapter or WLAN service stopped: an empty state "No Wi-Fi adapter found" (not a problem banner).

**Network panel** (`PanelIds.Network`, now Native; airplane mode lives in Wi-Fi — part (c)):
- *Wired*: one row per physical wired adapter: "Connected — 1000 Mb/s", "Connected — Sign in required" /
  "Connected — No internet" (from Windows' connectivity state), "Cable unplugged" or "Disabled". A gear opens
  *Details*: IPv4 and IPv6 addresses, hardware address, default route, DNS servers, DHCP or manual. Editing
  addresses and enabling adapters need admin and stay in Windows Settings (*Edit in Windows Settings*,
  `ms-settings:network-ethernet`).
- *VPN*: every Windows VPN connection (the ones Windows Settings → VPN lists) with a switch and its state
  (Connecting…, Connected, error text). Switching on launches Windows' own dialer for that entry
  (`rasphone.exe -d "<entry>"`), so Windows handles saved credentials, prompts and MFA and no VPN secret ever passes
  through WinGnome; the row follows the state by RAS notifications. Switching off hangs up from WinGnome. *+* opens
  `ms-settings:network-vpn`.
- *Network Proxy* row opens a dialog in Windows' model, styled like GNOME: *Automatically detect settings* switch,
  *Use setup script* switch with URL, *Use a proxy server* switch with HTTP/HTTPS/SOCKS host and port (*Use the same
  proxy for all protocols* by default), *Ignore hosts* and *Don't use the proxy for local addresses*. *Apply* writes.
  The dialog is read-only with "Managed by your organisation" when a policy manages the proxy, and read-only with
  "Set for all users on this PC" when `ProxySettingsPerUser` is 0 (machine-wide proxy, needs admin).

**Bluetooth panel** (`PanelIds.Bluetooth`, now Native — part (c)):
- Header switch *Bluetooth* (radio on/off). "No Bluetooth adapter found" empty state when there's no radio.
- *Devices*: paired devices with a category icon, "Connected"/"Not connected", and battery percentage when
  Windows reports it. A row's menu: *Remove Device* (asks first).
- *Nearby devices*: while the panel is open and Bluetooth is on, discovery runs and unpaired devices appear below
  ("Searching for devices…" spinner). Clicking one pairs: confirm-only, PIN display, PIN compare (*Does this PIN
  match?*) and PIN entry are shown in WinGnome dialogs; anything else (password credential, unknown kinds) hands off
  to `ms-settings:bluetooth`. A pairing dialog left unanswered for 60 s, or interrupted by the panel or window
  closing or minimising, cancels the pairing. Discovery stops when the panel closes, the window closes or minimises,
  or the radio turns off.
- Connecting or disconnecting an already-paired device (e.g. headphones) is not offered (no documented API); the
  menu has *Connect in Windows Settings*.

**Redirect** (WinGnome-dock taskbar mode, `General.HideWindowsTaskbar = true` — part (a)):
- Win+I opens WinGnome Settings exactly as `ShellCommands.ShowSettings()` does today: if the window is open it is
  brought to front on its **current page**; if it is closed it opens on its default page (the window is recreated on
  each open and doesn't remember the last page; remembering it is out of scope). WinGnome's own shortcuts to Windows
  Settings open WinGnome Settings at the matching native panel: the dock's and app grid's *Settings* app, overview
  search launching it, and `ms-settings:` launches from WinGnome surfaces whose page maps to a native panel. A page
  with no native panel still opens Windows Settings. Every panel keeps *Open in Windows Settings*, which always
  opens Windows Settings.
- New setting **`General.UseWinGnomeSettingsForWindowsSettings`** (bool, default `true`), Settings → General, under
  *Taskbar mode*: "Open WinGnome Settings for Win+I and Settings shortcuts". Disabled (greyed, with a note) in native
  taskbar mode. Old settings files without it load with the default.
- Win+I is only redirected by the WinGnome instance holding the input role (spec 0016,
  `Local\WinGnome-InputShortcuts`). If the input hook (or hotkey) can't be installed, WinGnome logs it and Win+I
  stays Windows'. Off in `--safe`/`--selftest`; in native-taskbar mode or with the setting off, Win+I and the
  Settings app behave exactly as today. Win+I with an elevated window in front opens Windows Settings (KI-004).

**Top bar** (part (a)): the quick settings Wi-Fi and Bluetooth tiles open the native panels once those are Native
(falls out of the catalogue: `SettingsPanelCatalog.DirectLinkFor` returns null for Native panels). Real toggles with
a GNOME network submenu are **deferred** to a follow-up spec (see Risks): they would load WinRT and a WLAN session
into the always-running top bar. The services below are built UI-independent so that spec can reuse them.

All three panels: read-only in safe mode (switches and buttons disabled, `ReadOnlyNote` shown); every failure shows
`Problem` with *Open in Windows Settings*; all OS calls run off the UI thread; updates arrive by events only while the
panel is open.

## Non-goals
- Metered connections, MAC randomisation, per-network details/IP editing, adapter enable/disable, static IPs (admin).
- Showing or exporting saved Wi-Fi passwords. Hidden-network entry (linked). Mobile broadband and hotspot setup (linked).
- Third-party VPN clients that don't create Windows VPN profiles (WireGuard, OpenVPN GUI, corporate agents);
  adding or editing VPN profiles (linked); handling VPN credentials in WinGnome.
- WinHTTP machine proxy (`netsh winhttp`, admin), per-connection (dial-up) proxies, editing a machine-wide proxy.
- Connecting/disconnecting paired Bluetooth devices, making the PC discoverable, Bluetooth file transfer.
- Quick settings toggles and network submenu (follow-up spec). Shell-mode hotkeys (spec 0013 wires its own map;
  this spec doesn't change `ShellHotkeyMap`).
- Remembering the last Settings page across window closes.

## Design

### API choices
- **Wi-Fi: Native Wifi API (`wlanapi.dll`)** — `WlanOpenHandle`, `WlanEnumInterfaces`, `WlanScan`,
  `WlanGetAvailableNetworkList`, `WlanQueryInterface` (current connection), `WlanGetProfileList`, `WlanSetProfile`,
  `WlanConnect` (profile mode), `WlanDisconnect`, `WlanDeleteProfile`, `WlanRegisterNotification` (ACM + MSM),
  `WlanReasonCodeToString`. Why: documented, works unpackaged with no capability or manifest (`wiFiControl` only gates
  packaged apps), covers saved profiles and *Forget* (WinRT `WiFiAdapter` can't list or delete profiles), Windows' own
  background scans arrive as `scan_list_refresh` notifications (no timer).
- **Radios (Wi-Fi and Bluetooth on/off): decided once by spike item 2.** If `WlanSetInterface`
  (`wlan_intf_opcode_radio_state`) works as a standard user, Wi-Fi on/off uses it and the Wi-Fi panel loads no WinRT.
  If it needs admin, **both** Wi-Fi and Bluetooth use `Windows.Devices.Radios` (`Radio.GetRadiosAsync`,
  `RequestAccessAsync`, `SetStateAsync`, `StateChanged`) through one shared `RadioClient`, and the Wi-Fi panel then
  pays the WinRT load too (see Footprint). No mixed per-machine fallback.
- **Airplane mode: no documented API.** `Windows.Devices.Radios` switches radios one by one but doesn't set
  Windows' airplane state. If spike item 3 is clean, use the undocumented `IRadioManager` (`RadioManagementAPI.dll`,
  `Get/SetSystemRadioState`) isolated in `AirplaneModeApi` like `PowerModeApi`: resolved at panel open, failure →
  link row. Otherwise v1 ships the link to `ms-settings:network-airplanemode`. **KI-109** (S4).
- **Location (24H2+)**: per Microsoft's "Changes to API behavior for Wi-Fi access and location", desktop apps calling
  `WlanGetAvailableNetworkList`/`WlanGetNetworkBssList`/`WlanQueryInterface(current_connection)` need location
  permission. The first such call from WinGnome is what makes Windows raise its consent prompt (once); WinGnome does
  nothing else to request it (no `Geolocator`). Afterwards a denial returns `ERROR_ACCESS_DENIED`. That code can also
  come from policy or other access checks, so the panel maps it to the location notice with hedged wording ("Windows
  needs location access…" plus *Open Location Settings*) and logs the call and code. The connected network's SSID is
  gated the same way: on denial the connected row omits the name. **KI-108** (S4).
- **Connectivity (captive / limited)**: `INetworkListManager.GetConnectivity` and per-network connectivity
  (documented COM, `NLM_CONNECTIVITY_IPV4_INTERNET` vs `…_LOCALNETWORK`, and the `NA_InternetConnectivityV4/V6`
  property's `NLM_INTERNET_CONNECTIVITY_WEBHIJACK` flag for "Sign in required"), declared in
  `Interop/NetworkListManager.cs`. Re-read on WLAN connect notifications and `NetworkChange` events; no NLM event sink.
- **Network**: adapters from `System.Net.NetworkInformation` (as `NetworkMonitor`; no P/Invoke) with
  `NetworkChange` events.
- **VPN: dial through Windows, observe through RAS.** Connect = `Process.Start("rasphone.exe", "-d \"<entry>\"")`
  (entry name quoted; names containing `"` are refused and handed off to `ms-settings:network-vpn`), so Windows'
  own dialer handles saved credentials, prompts, certificates and MFA and no VPN secret touches WinGnome. RAS
  (`rasapi32.dll`) is used only for `RasEnumEntries` with a null phonebook (user + all-users),
  `RasGetEntryProperties` (keep `RASET_Vpn`), `RasEnumConnections`, `RasGetConnectStatus`,
  `RasConnectionNotification` with a wait handle, and `RasHangUp` for disconnect. `RasHangUp` is never called from a
  RAS notifier or the notification wait callback; it runs on the RAS worker. No `RasDial`, no
  `RasGetEntryDialParams`. `Windows.Networking.Vpn.VpnManagementAgent` is rejected: it requires the restricted
  `networkingVpnProvider` capability and its unpackaged behaviour is undocumented.
- **Proxy: WinINet** `InternetQueryOption`/`InternetSetOption` with `INTERNET_OPTION_PER_CONNECTION_OPTION` (LAN
  connection, `PROXY_TYPE_*`, `PROXY_SERVER`, `PROXY_BYPASS`, `AUTOCONFIG_URL`). Writes always include
  `PROXY_TYPE_DIRECT` in the flags (OR'd with `AUTO_DETECT`/`AUTO_PROXY_URL`/`PROXY` as chosen), as Windows Settings
  does, so a failed proxy or script falls back to direct. Strings returned by the query are freed with `GlobalFree`
  in `finally`. After the write: `INTERNET_OPTION_PROXY_SETTINGS_CHANGED`, `INTERNET_OPTION_SETTINGS_CHANGED` and
  `INTERNET_OPTION_REFRESH` — the documented path that also updates `DefaultConnectionSettings`; no raw registry
  writes. Read-only detection (registry reads only): policy `HKCU`/`HKLM\Software\Policies\Microsoft\Internet
  Explorer\Control Panel` (`Proxy`, `Autoconfig`) → "Managed by your organisation"; `HKLM\Software\Policies\Microsoft\
  Windows\CurrentVersion\Internet Settings\ProxySettingsPerUser` = 0 → machine-wide → read-only.
- **Bluetooth: WinRT through the TFM** (`net8.0-windows10.0.19041.0` already projects `Windows.*`; no NuGet).
  Radio: see *Radios*. Devices: **four `DeviceWatcher`s** over `DeviceInformationKind.AssociationEndpoint` — paired ×
  unpaired, each for Bluetooth Classic and BLE protocol ids — requesting `System.Devices.Aep.IsPaired/IsConnected/
  IsPresent/Category/ContainerId`. One AQS per protocol keeps the filters documented (`Aep.ProtocolId` equality) and
  lets the two unpaired (discovery) watchers start and stop without touching the paired ones; a combined OR filter
  would save one watcher pair but makes discovery inseparable from the paired list. Paired watchers run while the
  panel is open; unpaired watchers only under the rules in Behaviour.
  **Watcher lifecycle** (`BluetoothWatcherSet`): `Start` only when `Status` is `Created`, `Stopped` or `Aborted`;
  `Stop` then await the `Stopped` event (with a 5 s cap, logged if exceeded) before any restart or dispose;
  unsubscribe `Added/Updated/Removed/EnumerationCompleted/Stopped` only after `Stopped` has fired.
  **Pairing**: `DeviceInformation.Pairing.Custom.PairAsync(ConfirmOnly | DisplayPin | ConfirmPinMatch | ProvidePin)`;
  `PairingRequested` takes a deferral and hands the request to our dialog; the deferral is completed on **every**
  path — accept, reject, *Cancel*, panel closed, window closed or minimised, and a 60 s timeout — in a `finally`, with
  `Accept` called only on the accept path. Removal `Pairing.UnpairAsync`.
  **Battery**: the property Windows uses (`{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`) lives on the PnP **device
  node**, not the association endpoint, so `BluetoothBattery` maps the AEP's `ContainerId` to device nodes
  (`DeviceInformation.FindAllAsync` with `DeviceInformationKind.Device` and an AQS on
  `System.Devices.ContainerId`, requesting that key) and takes the first present value. Undocumented, isolated,
  missing → no battery shown. **KI-110** (S4).
- **Win+I: through spec 0016's shared input host.** Explorer owns Win+I, so `RegisterHotKey` normally fails while
  Explorer runs. Win+I is one more entry in 0016's Core `ShortcutRouter`: `ShortcutConfig` gains a single flag
  **`RedirectWinI`**; when set, the router swallows I (down and its matching up) with Win held and no
  Shift/Ctrl/Alt, taps the Start mask, and returns a Settings action that `InputHookHost` posts to the UI thread as
  `ShellCommands.ShowSettings()`. Everything else (masking, swallowed-key bookkeeping, injected-input skip, role,
  reinstall on resume, fail-open) is 0016's. If 0016's WP0 spike shows Win+I *can* be registered with
  `RegisterHotKey` while Explorer runs, the flag is instead served by a hotkey owned by the same host (same flag,
  same role, no hook change). This spec adds no hook, no hotkey map subset and no chord helper of its own.

### Core (`src/WinGnome.Core/Connectivity/`, new namespace `WinGnome.Core.Connectivity`; tests mirror it)
- `SsidText` — `DOT11_SSID` bytes → display text (UTF-8, invalid → escaped hex), and hex for profiles.
- `WifiSecurity` — `(authAlgorithm, cipher, securityEnabled)` → `WifiProfileKind` {Open, Owe, Wep, WpaPsk,
  Wpa2Psk, Wpa3Sae, HandOff}; enterprise and unknown → HandOff; WPA2/WPA3 transition → Wpa2Psk.
- `WifiPassphrase.Validate(kind, ReadOnlySpan<char>)` — WPA: 8–63 printable ASCII or 64 hex; WEP: 5/13 ASCII or
  10/26 hex; SAE: 1–128 chars. Returns a result with a user message, never echoes the key.
- `WifiProfileXml.Build(ssidBytes, kind, cipher, ReadOnlySpan<char> key, string profileName)` — WLANProfile v1 XML
  written into a **`char[]`** that the caller owns (escaping by construction; no intermediate `string` holds the key),
  SSID as `<hex>` plus `<name>`, `connectionMode` auto, `keyMaterial` unprotected (Windows encrypts it on store).
  Rejects XML-invalid characters. The result `WifiProfileDocument` wraps the buffer and its length; it exposes the
  XML only as `ReadOnlySpan<char>`/the buffer for the P/Invoke, never as a `string`; `ToString()` returns only the
  profile name and kind; `Clear()` zeroes the buffer (`Array.Clear`) and is idempotent.
- `WifiProfileName.Choose(ssidText, existingNames)` — the profile `<name>`: the SSID text, or with a ` (xxxx)` hex
  suffix from the SSID bytes' hash when another profile with different SSID bytes already has that name.
- `WifiNetworkList.Build(available, profiles, currentSsid?)` — merges the API's per-profile duplicates, drops hidden
  SSIDs, sorts (connected, saved, signal, name), marks saved/secured; `currentSsid` null (location denied) → no row
  marked by name; `WifiSignal.Level(quality)` → 0–4.
- `WifiConnectFlow` — state machine: Idle → Connecting(profileOwnership) → Connected | AuthFailed | Failed |
  TimedOut (30 s one-shot, injected clock). `profileOwnership` is `Created` (new profile), `Overwritten` (saved
  profile replaced after a failed key) or `None` (saved profile used as is). Outputs commands: `SetProfile(overwrite)`,
  `DeleteProfile` (only when ownership is Created or Overwritten, on AuthFailed/TimedOut/Cancel),
  `PromptPassword(retry)`, `HandOff`. **AuthFailed on a saved profile used as is** → `PromptPassword(retry)`; the
  retry's `SetProfile(overwrite: true)` moves ownership to Overwritten. A newer attempt supersedes an older one.
- `WlanReasons.Classify(code)` — table-driven over the documented `WLAN_REASON_CODE` ranges in `wlanapi.h`:
  `WLAN_REASON_CODE_SUCCESS`; general (`0x10000`–), profile (`0x40000`–, profile-missing/invalid → Failed),
  AC/MSM (`0x30000`/`0x50000`–, security failures such as `MSMSEC` key/PSK mismatch → AuthFailure), network not
  available (`0x20000`–, e.g. `NETWORK_NOT_AVAILABLE` → NetworkNotAvailable), and Other. Exact codes listed in the
  test theory rows, cited from the header.
- `ProxyConfig` (AutoDetect, ScriptUrl?, Manual servers per scheme, Bypass list, BypassLocal) with
  `ProxyServerString.Parse/Format` (`host:port` and `http=…;https=…;socks=…`) and `ProxyBypass.Parse/Format`
  (`;`-separated, `<local>` ↔ BypassLocal), `ProxyConfig.Validate` (host, port 1–65535, `http(s)` script URL),
  `ProxyFlags.For(config)` → `PROXY_TYPE_*` with `DIRECT` always set.
- `ProxyEditability.Decide(policyProxy, policyAutoconfig, perUser)` → Editable | ManagedByPolicy | MachineWide.
- `WiredAdapterSummary` — status text (incl. connectivity: internet, no internet, sign in required), speed text
  ("1000 Mb/s", "2.5 Gb/s"), address ordering (IPv4 first, then global IPv6, link-local last); reuses
  `NetworkStatus.IsVirtualAdapter` from `Core/TopBar`.
- `VpnState` — RAS connection state + error code → row state and text.
- `RasphoneArgs.For(entryName)` — the argument string for `rasphone.exe -d`, or null for names it can't quote safely.
- `BluetoothDeviceList` — merges watcher add/update/remove by id from four watchers, splits paired vs nearby, drops
  nameless nearby devices, sorts (connected, name); `BluetoothCategory` from class-of-device/appearance → icon.
- `DeviceWatcherLifecycle` — pure state table for `Start/Stop/OnStopped/Dispose` against watcher status (which calls
  are allowed when, when to unsubscribe), driven by the app's `BluetoothWatcherSet`.
- `PairingPrompt.For(kind, pin)` — `DevicePairingKinds` (as flags int) → Confirm / ShowPin / ComparePin / EnterPin /
  HandOff, PIN zero-padded to 6 digits.
- `WindowsSettingsRedirect.Resolve(launchId)` — the Settings app AUMID → open window (current page); `ms-settings:`
  URIs → panel id when that panel is Native (from catalogue `LinkUri`s plus aliases: `network-wifi`,
  `network-wifisettings`, `network-ethernet`, `network-vpn`, `network-proxy`, `network-status`,
  `network-airplanemode`, `bluetooth`, `connecteddevices`, …), else null (launch normally). Case-insensitive; query
  strings ignored.
- `SettingsPanelCatalog`: Wifi (part b), Network and Bluetooth (part c) become `Native(...)` with their current URIs
  as fallback; keywords gain "airplane", "proxy", "vpn", "pair". `AppSettings`:
  `GeneralSettings.UseWinGnomeSettingsForWindowsSettings` (part a).
- `ShortcutConfig.RedirectWinI` and the router row for it (part a, added to spec 0016's Core input types once WP1a
  has landed; coordinated with 0016's owner, no parallel edit).

### App
- **Shared services** in `src/WinGnome/Services/Connectivity/` (not under `Panels/`, so the later top-bar spec
  can reuse them without a feature reference):
  - `WlanClient` — owns one WLAN handle on a dedicated serial worker (all WLAN calls in order, never on the UI
    thread). The notification delegate is held in a field for the client's lifetime (never a lambda passed inline,
    so the GC can't collect it while native code holds it). Callbacks arrive on a WLAN thread and only `BeginInvoke`
    to the dispatcher — never `Invoke`, because `WlanCloseHandle` waits for running callbacks and would deadlock.
    `Dispose` on the worker: unregister (`WlanRegisterNotification` with `WLAN_NOTIFICATION_SOURCE_NONE`), then
    `WlanCloseHandle`, then release the delegate. WLAN memory (`WlanFreeMemory`) freed in `finally`.
    **Profile writes**: `WlanSetProfile` is called with the `WifiProfileDocument` buffer pinned (`fixed`) and passed
    as `LPWStr` (`char*`), never marshalled from a `string`; the buffer is cleared in `finally`. All-user profile
    first (as Windows Settings creates); on `ERROR_ACCESS_DENIED` it retries once as `WLAN_PROFILE_USER` (per-user
    profile), logging the fallback; a second failure → banner. `bOverwrite` is `false` for new profiles and `true`
    only for the flow's `SetProfile(overwrite: true)`.
  - `RadioClient` (only if spike item 2 selects Radios), `AirplaneModeApi`, `RasClient` (VPN status + hang-up;
    launches `rasphone.exe`), `ProxySettingsClient` (WinINet), `ConnectivityClient` (`INetworkListManager`),
    `BluetoothClient` (radio + `BluetoothWatcherSet` + pairing; WinRT `IAsyncOperation` awaited with `AsTask()` on the
    thread pool, events marshalled with `BeginInvoke`), `BluetoothBattery`.
- **Interop**: new `NativeMethods.Wlan.cs`, `NativeMethods.Ras.cs`, `NativeMethods.WinInet.cs` (none exist yet);
  COM declarations `Interop/RadioManager.cs` (`IRadioManager`) and `Interop/NetworkListManager.cs`
  (`INetworkListManager`, `INetwork`, `INetworkConnection` as needed) beside `PolicyConfig.cs`.
- **Panels** `Features/Settings/Panels/{Wifi,Network,Bluetooth}/` — `*Panel.xaml`, `*PanelViewModel.cs`
  (derive from `SystemPanelViewModel`; `Open` creates the client and starts reads with `Task.Run`, `Close`
  disposes in reverse order), dialogs (`WifiPasswordDialog`, `SavedNetworksDialog`, `ProxyDialog`,
  `NetworkDetailsDialog`, `BluetoothPairingDialog`) owned by the settings window.
- **Secrets**: the password is read from `PasswordBox.SecurePassword` only at *Connect*, copied into a `char[]` via
  `Marshal.SecureStringToGlobalAllocUnicode` (freed with `ZeroFreeGlobalAllocUnicode`), validated as a span,
  written into the profile buffer, and every `char[]` holding it is `Array.Clear`ed in `finally`. *Show password*
  toggles a `TextBox` bound one-way from the dialog only while shown and cleared on close (documented as the one
  place the key is a managed string; see Risks). The key, the profile XML and the PIN are never passed to `Log`,
  exception messages, `SystemSettingWriter` failure reasons or banner text; log lines name the profile name and kind
  only.
- Every write (radio, airplane, profile set and delete, connect, disconnect, VPN launch and hang-up, proxy, pair,
  unpair) goes through `SystemSettingWriter` (ordered, off the UI thread, nothing in read-only mode); results that
  need follow-up (connect, pair, VPN state) report back through events, not the writer's failure callback.
- **Self-test**: `SystemPanelContext` gains the `CommandLineOptions` (`Options`), and panels read
  `Options.SelfTest` directly (no parallel `IsSelfTest` flag): in `--selftest` the panels open and render but don't
  call `WlanScan`/`WlanGetAvailableNetworkList`/current-connection queries, don't start any watcher and don't touch
  radios, so an unattended run can't raise a location or Bluetooth prompt. Each skip is logged once.
- `SettingsWindowViewModel` registers each view model in the part that delivers it.
- **Redirect (part a)**: the input feature (spec 0016's `InputFeature`) builds `ShortcutConfig` with
  `RedirectWinI = !IsSafeMode && General.HideWindowsTaskbar && General.UseWinGnomeSettingsForWindowsSettings`, and
  maps the router's Settings action to `ShellCommands.ShowSettings()`. It logs the outcome at start and on every
  settings change: "Win+I redirect: on (hook installed)", "off (setting / native taskbar / safe mode)", "off (input
  role held by another instance)" or "off (hook install failed, Win32 error N) — Win+I stays Windows'".
  `DockActions` and `OverviewWindow` launches ask `WindowsSettingsRedirect.Resolve` first when the setting is on and
  the taskbar mode is dock, then call `ShowSettings(panelId)` instead of `Launcher.Launch`.
  `SettingsWindowViewModel.OpenLink` and `TopBarActions` never redirect, so *Open in Windows Settings* always reaches
  Windows. `GeneralPageViewModel`/`GeneralPage.xaml` get the switch.

### Hostile cases
- **Threading**: the Win+I row is a table lookup in 0016's router (no extra allocation or lock). WLAN, RAS, WinINet
  and NLM calls can block for seconds (`RasHangUp`, `WlanConnect` on a busy driver): all on workers; `RasHangUp`
  waits for `RasGetConnectStatus` = `ERROR_INVALID_HANDLE` up to 3 s on the RAS worker, never on a notifier thread.
- **Sleep/resume and service restarts**: on `SystemEvents.PowerModeChanged` (Resume) an open panel re-reads; a
  WLAN handle invalidated by a WlanSvc restart (`ERROR_INVALID_HANDLE`) is reopened once, else the problem banner.
  Bluetooth watchers that stop with `Aborted` are restarted once (through the lifecycle rules), then the banner.
- **Pairing interrupted**: panel switch, window close or minimise, radio off and the 60 s timeout all cancel the
  dialog and complete the deferral; the late `PairAsync` result is ignored if the panel has closed.
- **Process launch**: `rasphone.exe` is started from `%SystemRoot%\System32` by full path with `UseShellExecute =
  false`; a missing binary → hand-off to `ms-settings:network-vpn`.
- **DPI/multi-monitor**: dialogs are WPF windows owned by the settings window (Per-Monitor-V2, centred on the owner,
  so negative coordinates and mixed DPI are WPF's). No screen geometry or HWNDs of other processes are touched,
  so elevated windows and HWND reuse don't apply except Win+I (above). Explorer restarts don't affect these APIs;
  hand-offs use `ms-settings:` pages, which work with the taskbar hidden.

### Work packages
| WP | Part | Owns | Depends on | Notes |
|---|---|---|---|---|
| 0 Spike | — | nothing committed | — | Verify on 25H2 as a standard user (list in Risks). Blocks radio, airplane, profile-scope and battery choices. |
| 1a Core Wi-Fi | b | `Core/Connectivity/` Wi-Fi types + tests | — | Can start now. |
| 1b Core Network/Proxy | c | `Core/Connectivity/` proxy, wired, VPN types + tests | — | Can start now. |
| 1c Core Bluetooth | c | `Core/Connectivity/` Bluetooth types + tests | — | Can start now. |
| 1d Core redirect | a | `WindowsSettingsRedirect`, `SettingsPanelCatalog.cs` keywords/aliases, `AppSettings.cs` field + tests; `ShortcutConfig.RedirectWinI` + router row + tests | 0016 WP1a (for the router row) | The router row lands after 0016 WP1a, coordinated with its owner. |
| 2 Wi-Fi | b | `NativeMethods.Wlan.cs`, `Interop/RadioManager.cs`, `Interop/NetworkListManager.cs`, `Services/Connectivity/{WlanClient,AirplaneModeApi,ConnectivityClient,RadioClient?}.cs`, `Panels/Wifi/`, catalogue flip | 0, 1a | **Safety-critical** (password handling, profile overwrite and deletion). |
| 3 Network | c | `NativeMethods.Ras.cs`, `NativeMethods.WinInet.cs`, `Services/Connectivity/{RasClient,ProxySettingsClient}.cs`, `Panels/Network/`, catalogue flip | 0, 1b; `ConnectivityClient` from 2 (or creates it if 2 hasn't) | Parallel with 2 and 4. Proxy write is user-visible system state. |
| 4 Bluetooth | c | `Services/Connectivity/{BluetoothClient,BluetoothWatcherSet,BluetoothBattery}.cs`, `Panels/Bluetooth/`, catalogue flip | 0, 1c; `RadioClient` if 2 created it | Parallel with 2 and 3. |
| 5 Redirect + integration | a | `InputFeature` config line (in 0016's `Features/Input/`, after 0016 WP2), `DockActions.cs`, `OverviewWindow.xaml.cs`, `GeneralPage*`, `SystemPanelViewModel.cs` (`Options`), `SettingsWindowViewModel.cs` | 1d, **0016 WP1a and WP2** | **Safety-critical** (keyboard input). Docs (README, PLAN, KNOWN_ISSUES) for each part ship with that part. |

## Safety and recovery
- Like spec 0015's panels, these change Windows settings on purpose and are not backed up: radio states, airplane
  mode, Wi-Fi profiles, VPN connections, the user's proxy and Bluetooth pairings persist after WinGnome exits, as they
  would from Windows Settings. Nothing session-scoped is changed, so nothing needs restoring on exit, crash or
  force-kill. Profile writes are all-user profiles (as Windows Settings creates), falling back to a per-user profile
  when the all-user write is denied; never elevation. The only cleanup is `WifiConnectFlow`'s deletion of a profile
  it created or overwrote for a failed attempt; a crash in between leaves that profile saved (harmless and
  forgettable; **KI-111**). Closing the panel while connecting abandons the attempt without deleting (KI-111). Overwriting a saved profile after an authentication failure loses the old (already
  rejected) key; that is the intended outcome.
- Proxy: a wrong manual proxy can break browsing. The dialog validates before *Apply*, always keeps
  `PROXY_TYPE_DIRECT` as fallback, reads back after the write (mismatch → banner), and the README notes
  *Network → Network Proxy* or Windows Settings to undo. No auto-revert (unlike Displays, the user can still reach
  Settings to fix it). Machine-wide and policy-managed proxies are never written.
- VPN: WinGnome starts Windows' dialer and hangs up; it never sees or stores VPN credentials.
- Keyboard: no hook of this spec's own. The Win+I row inherits 0016's guarantees (installed only when wanted and
  holding the input role, never in `--safe`/`--selftest`, matched up/down swallowing, injected input ignored,
  exceptions pass the key, fail open to Windows). It swallows only I with Win held and no Shift/Ctrl/Alt.
- `--safe`: panels read-only, no writes, `RedirectWinI` false. `--selftest`: as `--safe` plus no scans,
  location-gated calls, watchers or radio access; the panels still open and render so the self-test covers their
  views, and the log records the redirect decision.
- WinGnome never stores or logs a Wi-Fi password, VPN credential or PIN; key material lives only in `char[]`/unmanaged
  buffers that are cleared in `finally` (the *Show password* text box is the documented exception, see Risks).

## Footprint
- Idle (window closed or another panel showing): nothing — no handles, watchers, notifications or WinRT objects.
  The Win+I redirect adds one row to 0016's router; for users who have every other 0016 shortcut and both Super
  options off it is the only reason the shared keyboard hook is installed (one table lookup per keystroke on the
  hook thread).
- Wi-Fi panel open: one WLAN handle and notification registration; one `WlanScan` on open and at most one per 5 s
  on *Refresh*; further updates from Windows' own scans. No timers except the 30 s connect timeout (one-shot). One
  `INetworkListManager` instance while open. **If spike item 2 selects `Windows.Devices.Radios`**, opening the
  Wi-Fi panel also loads the WinRT projection (~15–30 MB, spec 0015), which stays loaded for the process lifetime;
  otherwise the Wi-Fi panel loads no WinRT.
- Network panel open: `NetworkChange` subscription (debounced 500 ms, as `NetworkMonitor`), one RAS notification
  wait. Bluetooth panel open: two paired watchers; two discovery watchers (radio inquiry, the costly part) only while
  visible. First use of the Bluetooth panel (or the Wi-Fi panel, above) loads the WinRT projection; measure it in Task
  Manager (QA step 6) and record it here.

## Acceptance criteria
Part (b) — Wi-Fi:
1. Core tests: `SsidText` (ASCII, UTF-8, invalid bytes, 0 and 32 bytes); `WifiSecurity` rows for every auth/cipher
   incl. enterprise and transition → HandOff/Wpa2Psk; `WifiPassphrase` boundaries (7/8/63/64 chars, 63 hex vs 64
   hex, WEP lengths, non-ASCII).
2. Core tests: `WifiProfileXml` exact expected XML for open, WPA2, WPA3, WEP; SSID/key containing `& < > " '`
   round-trip through an XML parser to the original bytes; XML-invalid characters rejected; `ToString()` omits the
   key and XML; `Clear()` zeroes the buffer; `WifiProfileName` collision gets a hex suffix, same SSID reuses the name.
3. Core tests: `WifiNetworkList` dedupe/order/hidden/no current SSID; `WifiSignal` thresholds ±1; `WifiConnectFlow`
   deletes only a profile it created or overwrote, re-prompts on AuthFailed, **AuthFailed on a saved profile →
   PromptPassword, retry emits `SetProfile(overwrite: true)` and a further failure or cancel deletes it**, times out
   at exactly 30 s, ignores a superseded attempt's result; `WlanReasons` theory rows per range.
4. Manual: list matches Windows' flyout; connect to a WPA2 network with a wrong then right password (wrong one not
   left in *Saved Networks*); change a saved network's key on the router → prompt → new key works; disconnect;
   forget; Wi-Fi off/on and airplane on/off (or the airplane link) reflected in Windows quick settings within 2 s; an
   enterprise network hands off to `ms-settings:network-wifi`; first open on a clean 24H2 profile raises Windows'
   location prompt, deny → notice and connected row without name, allow → list; captive portal shows *Sign In*.

Part (c) — Network and Bluetooth:
5. Core tests: `ProxyServerString`/`ProxyBypass` parse and format (single, per-scheme, `<local>`, IPv6 literal, bad
   port), `ProxyConfig.Validate`, `ProxyFlags` always includes DIRECT, `ProxyEditability` rows; `WiredAdapterSummary`
   speeds, connectivity text and ordering; `VpnState`; `RasphoneArgs` (plain, spaces, quote refused).
6. Core tests: `BluetoothDeviceList` add/update/remove/pair transitions across four sources; `DeviceWatcherLifecycle`
   (no Start while Started/Stopping, unsubscribe only after Stopped, dispose waits); `PairingPrompt` per kind and PIN
   padding.
7. Manual (Network): wired details match `ipconfig /all`; a test VPN connects via Windows' dialer and disconnects
   from the switch, Windows Settings agrees; proxy set manual → Edge uses it → disabled again; policy-managed and
   `ProxySettingsPerUser=0` proxies show read-only.
8. Manual (Bluetooth): on/off; a phone pairs with PIN compare in our dialog; a mouse pairs confirm-only; closing the
   panel mid-pairing cancels it and the phone shows the pairing failed; battery shows for a device Windows shows
   battery for; *Remove Device* unpairs; discovery stops on panel change (log line, phone no longer sees an inquiry).

Part (a) — Redirect and catalogue:
9. Core tests: `WindowsSettingsRedirect` (AUMID, mapped pages, aliases, unknown page, Link panel, case, query);
   router: Win+I swallowed (down and up, mask tapped) only with `RedirectWinI`, Win+Shift+I, Ctrl+Win+I, Alt+Win+I and
   I alone never; catalogue: a Native panel's `DirectLinkFor` returns null; settings without the new field load as
   `true`; each new test seen failing.
10. Manual: dock mode, setting on → Win+I opens WinGnome Settings on its current page (or default page if closed),
    Start doesn't open, I isn't typed into the focused app; dock Settings pin opens WinGnome Settings; *Open in
    Windows Settings* opens Windows Settings; setting off / native taskbar / `--safe` / second instance without the
    role → Win+I opens Windows Settings; elevated window focused → Windows Settings.
11. Quick settings Wi-Fi and Bluetooth tiles open the native panels once parts (b) and (c) have flipped them.

All parts:
12. `--selftest --safe` exits 0; the log shows no scan, location, watcher or radio call, and a "Win+I redirect: off
    (safe mode)" line; a normal smoke run logs "Win+I redirect: on (hook installed)" or the failure line; `--safe`
    panels are read-only.
13. Footprint: idle memory and CPU with the window closed unchanged; WinRT one-off cost recorded in the spec;
    light/dark at 100 % and 125 %.

## Risks and open questions
- **Spike (WP 0)**, on Windows 11 25H2 as a standard user: (1) WLAN location behaviour — the first
  `WlanGetAvailableNetworkList` raising the prompt, whether it names WinGnome, and the current-connection SSID gating;
  (2) `WlanSetInterface` radio state without admin (decides Radios for both radios); (3) `IRadioManager` airplane
  state read/write and quick-settings agreement (decides switch vs link); (4) `WlanSetProfile` all-user without admin,
  the `ERROR_ACCESS_DENIED` → `WLAN_PROFILE_USER` fallback, `WlanDeleteProfile` on our own and on another user's
  all-user profile; (5) custom pairing, deferral completion on cancel, and `UnpairAsync` unpackaged; (6) the battery
  key on the device node via `ContainerId`; (7) `rasphone.exe -d` for IKEv2/SSTP/L2TP entries with saved and
  prompted credentials, and RAS notifications while it dials; (8) Win+I: whether 0016's WP0 shows `RegisterHotKey`
  works, else the router row swallows it without Start or Settings opening; (9) whether `ms-availablenetworks:` opens
  while the taskbar is hidden (expected not; `ms-settings:network-wifi` is primary regardless); (10) NLM
  `WEBHIJACK` on a real captive portal; (11) `SecurePassword` → `char[]` path leaves no managed string (memory
  inspection in a debug build).
- **Decided (user, 2026-10-09) — dock's Settings pin redirects** to WinGnome Settings. Its running dots don't light
  for WinGnome Settings; Windows Settings stays reachable from each panel's link and by turning the redirect off.
- **Default-on redirect** follows the user's decision to redirect Win+I (2026-10-09); it installs the shared keyboard
  hook on upgrade for users who had every other shortcut off. Turning the setting off removes it.
- *Show password* necessarily puts the key in a WPF `TextBox` (a managed string) while shown; it is cleared on hide
  and on close. Accepted residual risk.
- Per-user fallback profiles are invisible to other accounts on the PC (unlike Windows Settings' all-user profiles);
  logged and listed in KI-111.
- VPN via `rasphone.exe` shows Windows' classic dialer window for entries that prompt; that is the price of never
  handling VPN secrets. A VPN type `rasphone` can't dial falls back to `ms-settings:network-vpn`.
- KNOWN_ISSUES entries: **KI-109** (S4) airplane mode via undocumented `IRadioManager` (or link-only in v1);
  **KI-108** (S4) Wi-Fi list and connected SSID need location access on 24H2+, WinGnome's first list call raises
  Windows' prompt and then appears in Windows' location activity list, `ERROR_ACCESS_DENIED` may also be policy;
  **KI-110** (S4) undocumented Bluetooth battery key on the device node; **KI-111** (S4) Wi-Fi profile leftovers and
  scope: a crash mid-connect can leave a just-created or overwritten profile saved, a denied all-user write falls back
  to a per-user profile, and *Forget* of another user's all-user profile may be refused.
- Part (a) cannot start its integration WP until spec 0016 WP1a and WP2 have landed; if 0016 slips, part (a) slips
  with it (there is deliberately no interim hook).
- Follow-up spec: quick settings Wi-Fi/Bluetooth toggles with a network submenu, reusing `Services/Connectivity`;
  it must decide whether the top bar may keep WinRT loaded and whether opening the popup may scan.
