# 0025 — A usable Wi-Fi panel when Windows withholds the network list

Status: Draft — planned 2026-10-18 from read-only probes on the user's machine (Windows 11 26H2, build 26300.9711).
Extends spec 0019 part (b); closes the *Saved Networks* part of KI-109 and revises KI-108.

## Problem
The user opens Settings → Wi-Fi and sees only the location notice and nothing else: no networks, no connected network,
no saved networks. They can't see, choose, connect to, forget or change a network from WinGnome.

Evidence (read-only probes, unpackaged .NET 8 process with the app's TFM, same user, not elevated):

| Read | Result |
|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors` `DisableLocation` | `1` (the *Turn off location* policy; written 30 Apr 2026 beside other privacy policies such as `WindowsCopilot`, `WindowsAI`, `DataCollection`, so most likely a privacy/debloat tool; not WinGnome — no WinGnome code mentions it) |
| Consent store `location` `Value`: HKLM (device) / HKCU (user) / HKCU `NonPackaged` (desktop apps) | `Deny` / `Deny` / `Allow` |
| `AppCapability("wiFiControl").CheckAccess()` and `("location")` | `UserPromptRequired` (**wrong**: no prompt can be shown while the policy is set) |
| `WiFiAdapter.RequestAccessAsync()` | `DeniedBySystem` (correct) |
| `Geolocator.RequestAccessAsync()` | `Denied` |
| `WiFiAdapter.FindAllAdaptersAsync` + `NetworkReport` | `UnauthorizedAccessException` 0x80070005 |
| `WlanGetAvailableNetworkList`, `WlanQueryInterface(current_connection)` | Win32 error 5 |
| `netsh wlan show networks` / `show interfaces` | refused, "need location permission" |
| `WlanEnumInterfaces`, `WlanQueryInterface(interface_state)` | OK, state 1 (connected) |
| `WlanGetProfileList` / `netsh wlan show profiles` | OK, 11 all-user profiles |
| `WlanGetProfile` (no plain-text flag) | OK: `connectionMode`, authentication, key `protected=true`; granted access `0x70023` (`WLAN_WRITE_ACCESS`) |
| `ConnectionProfile.WlanConnectionProfileDetails.GetConnectedSsid()`, `GetSignalBars()` | `Netllar_Fibra_5G_78E66A`, 4 bars |
| `NetworkInformation.GetConnectionProfiles()` | 11 WLAN profiles by name |

Microsoft's [Wi-Fi access and location changes](https://learn.microsoft.com/windows/win32/nativewifi/wi-fi-access-location-changes)
lists the gated calls: `WlanGetAvailableNetworkList(2)`, `WlanGetNetworkBssList`, `WlanQueryInterface(current_connection)`,
`WlanScan`, MSM notifications, and everything in `Windows.Devices.WiFi`. Profile, connect, disconnect, delete and ACM
notifications are not gated. System components (Windows' own Wi-Fi flyout and Settings) are exempt.

Root cause, in three parts:
1. **Nothing WinGnome calls can list nearby networks on this PC.** The policy denies location device-wide; no consent
   prompt can appear. WinRT `WiFiAdapter`, `Geolocator` and `netsh` are gated the same way, so they are no fallback.
2. **The panel misreads the state.** `wiFiControl.CheckAccess()` says `UserPromptRequired`, so the panel offers *Show
   nearby networks*. The click makes a refused call. The notice then switches to *Open Location Settings*, where the
   switches are greyed out by the policy. There's no way forward from either button.
3. **The panel hides everything that does work.** While location is denied, `ShowsList` is false, so the
   saved networks and the connected network are hidden. The "Connected" row is hidden too, because the name *was* read
   through `ConnectionProfile` but no list row exists to mark (`_connectedWithoutName` is false).

## Behaviour
The panel stops depending on location for anything that doesn't need it. Top to bottom:

1. **Wi-Fi** switch and **Airplane Mode** row: unchanged.
2. **Current network** (shown when the adapter reports connected): the network name (from
   `WlanConnectionProfileDetails.GetConnectedSsid`, else "Connected" without a name), signal bars from
   `ConnectionProfile.GetSignalBars`, security from the saved profile ("Connected · WPA3"). Details: *Disconnect*,
   *Connect automatically* switch, *Forget*.
3. **Visible Networks**: only when location access allows it. Otherwise this group shows the **location notice**,
   with wording and buttons matching the real reason (Core `WifiLocationGate`):

   | Reason (first match wins) | Title | Buttons |
   |---|---|---|
   | Policy (`DisableLocation=1`, MDM `AllowLocation=0`, or `AppPrivacy\LetAppsAccessLocation=2`) | "Location is turned off by a policy on this PC" — subtitle: "Windows won't tell apps which networks are nearby. Your saved networks below still work. To list nearby networks here, an administrator must remove the policy, then turn on location for desktop apps." | *Show networks in Windows* |
   | Device location off (HKLM `Deny`) | "Location services are off" | *Open Location Settings*, *Show networks in Windows* |
   | User "Let apps access your location" off, or desktop apps off | "Location access is off for desktop apps" | *Open Location Settings*, *Show networks in Windows* |
   | WinGnome's own desktop-app entry `Deny` | "WinGnome isn't allowed to use your location" | *Open Location Settings*, *Show networks in Windows* |
   | Not asked yet (capability `UserPromptRequired`, nothing above) | "Windows needs your permission to show nearby networks" | *Show nearby networks* (as today: the first gated call raises Windows' prompt) |
   | A gated call returns `ERROR_ACCESS_DENIED` despite the above | hedged text as today | *Open Location Settings*, *Show networks in Windows* |

   *Show networks in Windows* opens `ms-settings:network-wifi` (Windows' page lists and connects to networks itself
   and works with the taskbar hidden; `ms-availablenetworks:` is not used, see Risks).
4. **Saved Networks** (always, when there is an adapter): every saved profile from `WlanGetProfileList`, connected one
   first, then by name. Each row: name, subtitle ("Connected · WPA2", "Saved · WPA3", "Saved · Open",
   "Managed by your organisation" for group-policy profiles, "This user only" for per-user profiles), and in range
   signal bars when the visible list is available. Row actions:
   - *Connect* (not shown on the connected row): `WlanConnect` with the profile, through `WifiConnectFlow` as a saved
     network (a rejected key re-prompts for the password exactly as today). Out of range → "\"X\" isn't reachable
     right now." (reason class `NetworkNotAvailable`).
   - *Connect automatically* switch: the profile's `connectionMode` `auto`/`manual` (what Windows Settings'
     *Connect automatically when in range* writes).
   - *Forget* (asks first, as today).
   - Group-policy profiles: no switch, no *Forget* (Windows refuses both).
5. Footer: *Refresh*, *Wi-Fi Hotspot…*, *Connect to Hidden Network…*, *Open in Windows Settings* — unchanged.

No settings are added. Safe mode: everything read-only as today (switch and buttons disabled). `--selftest`: as
today, no WLAN, NLM, location or registry reads.

## Non-goals
- Listing nearby networks while Windows denies location (impossible by design for a non-system app).
- Changing location consent or the policy from WinGnome: HKLM, needs elevation (AGENTS.md least privilege). The
  Privacy panel's HKCU switches are not driven from the Wi-Fi panel.
- Adding a network by typing its name and security (Windows' *Connect to Hidden Network* covers it).
- Other profile properties: metered, random hardware address, network category (private/public needs admin), IP/DNS,
  proxy, profile priority/order. Captive-portal *Sign In*, WLAN handle reopen after a WlanSvc restart, a native
  Airplane Mode switch (all stay in KI-109).
- Quick settings network submenu (spec 0019 follow-up).

## Design

### Options considered (ranked)
1. **Chosen — location-independent panel plus an accurate gate.** Saved profiles, the current network and all
   writes use calls that aren't gated (proved above). The location notice explains the real reason from registry
   reads that never raise a prompt, and hands nearby-network selection to Windows' own page when it can't be shown.
2. Rejected — `WiFiAdapter` (WinRT) instead of or as a fallback to wlanapi: same gate (`DeniedBySystem`, access
   denied on `FindAllAdaptersAsync`). It also can't list, edit or delete profiles.
3. Rejected — `Geolocator.RequestAccessAsync` to raise the prompt: returns `Denied` under the policy, and
   Microsoft names `WiFiAdapter.RequestAccessAsync`, not Geolocator, as the trigger for Wi-Fi. The existing trigger
   (the first gated call after the user's click) stays for the genuine not-asked case.
4. Rejected — `netsh` fallback: `show networks`/`show interfaces` are gated too; `show profiles` gives only what
   `WlanGetProfileList` already gives, through a new process and localised text parsing.
5. Rejected — using `wiFiControl.CheckAccess()` alone (today's design): it reports `UserPromptRequired` under the
   policy. It stays as the last input of the gate (and for `AccessChanged` events).

### Core (`src/WinGnome.Core/Connectivity/`, tests in `tests/WinGnome.Core.Tests/Connectivity/`)
- **New `WifiLocationGate.cs`**: `WifiLocationInputs(bool PolicyOff, ConsentState Device, ConsentState User,
  ConsentState DesktopApps, ConsentState? ThisApp, WifiLocationAccess Capability)` →
  `WifiLocationBlock` {`None`, `Policy`, `DeviceOff`, `UserOff`, `DesktopAppsOff`, `AppDenied`, `NotAsked`}.
  Order: policy, device, user, desktop apps, this app, capability (`Allowed`/`Unknown` → `None`,
  `UserPromptRequired` → `NotAsked`, `DeniedByUser` → `AppDenied`, `DeniedBySystem` → `DeviceOff`). Uses
  `ConsentValue.IsAllowed` (absent = allowed). Also `WifiLocationGate.AppKeyFor(string processPath)`: the consent
  store's `NonPackaged` key name for a path (`\` → `#`), compared case-insensitively.
- **New `WifiLocationNotice.cs`**: `For(WifiLocationBlock, bool callRefused)` → `(Title, Subtitle, bool
  OfferShowNearby, bool OfferLocationSettings, bool OfferWindowsList)` with the texts in Behaviour.
- **Changed `WifiLocationPolicy.cs`**: `Decide(WifiLocationBlock block, bool userAskedForNetworks)` replaces the
  `WifiLocationAccess` overload (`None` → call; `NotAsked` → call only after the click; others → never call).
  `WifiLocationAccess` stays (input of the gate).
- **Changed `WifiProfileXml.cs`**:
  - `ReadSummary(string xml)` → `WifiProfileSummary(string Name, byte[] Ssid, string Authentication, string
    Encryption, bool AutoConnect, bool UsesOneX)` or null for malformed XML. Parsed with `XmlReader`
    (`DtdProcessing.Prohibit`, no resolver), SSID from `<hex>` (falls back to `<name>` UTF-8), never reads or returns
    `keyMaterial`.
  - `WithConnectionMode(string existingXml, bool auto, string profileName)` → `WifiProfileDocument`, changing only the
    `<connectionMode>` text (adds the element after `<connectionType>` if missing, as `ReplaceKey` keeps the rest).
    Throws `ArgumentException` (message never contains XML) if the XML has no `<connectionType>`.
- **Changed `WifiSecurity.cs`**: `FromProfile(string authentication, string encryption, bool usesOneX)` →
  `WifiProfileKind` (`open`/`none` → Open, `OWE` → Owe, `open`+`WEP` → Wep, `WPAPSK` → WpaPsk, `WPA2PSK` → Wpa2Psk,
  `WPA3SAE` → Wpa3Sae, `WPA`/`WPA2`/`WPA3`/`WPA3ENT*`/OneX → HandOff, unknown → HandOff), case-insensitive.
- **New `WifiSavedNetworkList.cs`**: `WifiSavedProfile(string ProfileName, WifiProfileSummary? Summary, bool
  IsGroupPolicy, bool IsPerUser)`; `Build(saved, byte[]? currentSsid, IReadOnlyList<WifiNetworkRow>? visible)` →
  `WifiSavedNetworkRow(ProfileName, Name, Ssid, Kind, AutoConnect, IsConnected, CanEdit, int? SignalLevel, string
  Subtitle)`. Connected = summary SSID bytes equal `currentSsid` (first match only, by profile order); signal from the
  visible row with the same SSID; order connected, then name (ordinal-ignore-case, then ordinal); a profile whose XML
  couldn't be read still lists with its name, kind HandOff and no switch.

### App
- **`Interop/NativeMethods.Wlan.cs`**: `WlanGetProfile`'s `pdwFlags` becomes `ref uint` (in: 0; out:
  `WLAN_PROFILE_GROUP_POLICY` 0x1 / `WLAN_PROFILE_USER` 0x2); add `WLAN_PROFILE_GROUP_POLICY`. No new functions.
- **`Services/Connectivity/WlanClient.cs`**:
  - `ReadProfiles` also reads `WLAN_PROFILE_INFO.dwFlags` (offset 512).
  - `ReadAsync(callGated)` returns, in `WlanSnapshot`, `SavedProfiles` (`IReadOnlyList<WifiSavedProfile>`: one
    `WlanGetProfile` per profile, summary parsed with `WifiProfileXml.ReadSummary`; the XML string is dropped right
    after parsing and never logged) and `SignalBars` (`int?`, `ConnectionProfile.GetSignalBars` of the internet
    profile when it is WLAN). The SSID comes from `ReadSsidWithoutWlanApi` whenever `current_connection` is skipped or
    refused (already so; keep).
  - New `SetConnectionModeAsync(string profileName, bool auto)`: `WlanGetProfile` (flags out) → refuse if group
    policy → `WifiProfileXml.WithConnectionMode` → `WlanSetProfile(flags = original user/all-user flag, overwrite:
    true)` with the pinned buffer, cleared in `finally`. No per-user fallback (it would create a duplicate).
- **New `Features/Settings/Panels/Wifi/WifiLocationGateReader.cs`** (HKLM and HKCU **reads only**, off the UI
  thread): `DisableLocation` under `HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors`,
  `LetAppsAccessLocation` under `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy`, `AllowLocation` under
  `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\System`; consent values through the existing
  `Privacy/ConsentStore.Read(PrivacyCapability.Location)` (still the only class touching consent keys) and the app
  key from `WifiLocationGate.AppKeyFor(Environment.ProcessPath)`; the capability from `WifiLocationProbe.Check()`.
  Returns `WifiLocationInputs`. Missing keys → not set; read exceptions logged with `Log.Warn`, treated as not set.
- **`Features/Settings/Panels/Wifi/WifiPanelViewModel.cs`**: `Reload` asks the gate reader, then
  `WifiLocationPolicy.Decide`, then `client.ReadAsync`. New `SavedNetworks` collection (`WifiSavedNetworkItem`),
  `CurrentNetwork` properties, `ShowsCurrentNetwork`, `ShowsSavedNetworks` (adapter present), notice properties from
  `WifiLocationNotice`. New commands `ConnectSavedCommand` (builds a `WifiNetworkRow` from the saved row with
  `IsSaved = true` and enters the existing `Connect` path), `AutoConnectCommand` (through `SystemSettingWriter`,
  re-read after, the switch shows the re-read value like `VerifiedSwitch`), `ShowWindowsListCommand`
  (`ms-settings:network-wifi`). Forget and Disconnect reuse today's code with a profile name parameter. Fixes the
  "connected but nothing shown" case (current network group shows whenever `IsConnected`).
- **`Features/Settings/Panels/Wifi/WifiPanel.xaml`**: Current network group, notice buttons per flags, Saved
  Networks group (row: name, subtitle, bars when known, *Connect*; details: switch, *Forget*).
- No change to `WifiConnectFlow`, the password dialog, `RadioClient`, `ConsentStore`, catalogue or settings.

Threading: all WLAN calls stay on `WlanClient`'s worker; registry reads in `LoadAsync` (`longRunning`). DPI and
multi-monitor: only WPF rows inside the existing panel. No other processes' windows, no elevation.

## Safety and recovery
- Reads only: the policy and consent keys are read, never written. No prompt is raised except by the user's
  *Show nearby networks* click in the genuine not-asked state; under a policy or a denial no gated call is made, so
  Windows' "Location has been turned off" dialog and the location icon are not triggered.
- Writes (all through `SystemSettingWriter`, none in `--safe`): `WlanConnect`, `WlanDisconnect`, `WlanDeleteProfile`
  (confirmed first) and the connect-mode overwrite. Like spec 0019 these are deliberate user changes, persisted by
  Windows, not backed up and not reverted on exit. The connect-mode overwrite keeps the profile's encrypted key and
  every other element; if `WlanSetProfile` fails Windows keeps the old profile unchanged (overwrite is atomic), the
  switch re-reads and snaps back, and the banner shows. A crash mid-write leaves either the old or the new profile.
- No secrets: the profile XML read for parsing holds only the encrypted key; it is not logged, not kept, not put in
  exception text. Passwords follow spec 0019 unchanged.
- `--selftest`: the panel opens and renders without any WLAN, NLM, location or registry reads (as today).

## Footprint
Nothing at idle (panel closed): no handles, timers or watchers, unchanged. Panel open: per reload (open, Refresh,
debounced WLAN events) one `WlanGetProfile` per saved profile (11 here, sub-millisecond each on the worker), four
registry value reads, one `ConnectionProfile` read. No new timers, polling, hooks or processes. WinRT is already
loaded by `RadioClient`.

## Acceptance criteria
1. `WifiLocationGate`: theory rows for each reason, including **policy on + capability `UserPromptRequired` →
   `Policy`** (this machine), device `Deny` + user `Allow` → `DeviceOff`, all absent + `Allowed` → `None`, all absent +
   `Unknown` → `None`, desktop `Deny` → `DesktopAppsOff`, app `Deny` → `AppDenied`, `UserPromptRequired` → `NotAsked`;
   `AppKeyFor` (`C:\Program Files\WinGnome\WinGnome.exe` → `C:#Program Files#WinGnome#WinGnome.exe`). → Core tests.
2. `WifiLocationNotice` rows: `Policy` offers only *Show networks in Windows*; `NotAsked` only *Show nearby
   networks*; `callRefused` gives the hedged text. → Core tests.
3. `WifiLocationPolicy.Decide` rows: only `None`, or `NotAsked` after the click, call gated APIs. → Core tests
   (existing tests rewritten to the new signature, each seen failing against a flipped condition).
4. `WifiProfileXml.ReadSummary`: real WPA2-PSK, WPA3-SAE, open, OWE, WEP and 802.1X profile XML (as Windows exports,
   key encrypted) → exact summaries; `<hex>` absent → name bytes; malformed XML, DTD → null; result never contains the
   key material. → Core tests.
5. `WifiProfileXml.WithConnectionMode`: `auto`→`manual` and back give the exact expected XML with every other byte
   unchanged (including `<keyMaterial>`); missing `<connectionMode>` inserted after `<connectionType>`; no
   `<connectionType>` throws without XML in the message; `Clear()` zeroes. → Core tests.
6. `WifiSecurity.FromProfile` rows for every string above plus unknown and case variants. → Core tests.
7. `WifiSavedNetworkList.Build`: empty, one, many; connected first by SSID bytes; two profiles with the same SSID →
   only the first connected; signal only from a matching visible row; group policy → `CanEdit` false; unreadable XML
   → HandOff row without switch; ordering ties. → Core tests.
8. Build `-warnaserror` clean, `dotnet test` green, `--selftest --safe` exits 0 and its log shows no Wi-Fi reads.
   → QA 1–3.
9. On this machine (policy set): the panel shows the Netllar_Fibra_5G_78E66A current-network row with bars and
   security, all 11 saved networks (matches `netsh wlan show profiles`), and the *Policy* notice without *Show nearby
   networks*; the log has no `WlanGetAvailableNetworkList` refusal and Windows shows no location dialog. → manual QA.
10. *Connect automatically* off on a saved, not-connected profile → `netsh wlan show profile name="<it>"` shows
    *Connection mode: Connect manually*; on again → *automatically*; the network still connects afterwards (key
    intact). → manual QA (user).
11. *Connect* on an in-range saved network switches to it; on an out-of-range one shows "isn't reachable right now";
    *Disconnect* and *Forget* work from the current-network group. → manual QA (user).
12. *Show networks in Windows* opens Windows' Wi-Fi page with WinGnome's dock mode on (taskbar hidden) and connecting
    to a new network there works; WinGnome's panel then lists it under Saved Networks after the WLAN event. → manual QA
    (user).
13. With location allowed (another machine, or here only if the user chooses to remove the policy), the Visible
    Networks list shows as before and saved rows in range show bars; on a fresh profile *Show nearby networks* raises
    Windows' prompt. → manual QA (user, optional).
14. `--safe`: switch, *Connect*, *Connect automatically*, *Forget* disabled. Light and dark, 100 % and 125 %.
    Idle memory with Settings closed unchanged. → QA 4, 6.
15. Independent Opus review of the diff; README Wi-Fi line, PLAN Core API row, KNOWN_ISSUES (KI-108 revised, KI-109
    Saved Networks done, new KI-117) and specs/README updated. → QA 7–8.

## Risks and open questions
- **Not verified live (needs writes):** that `WlanSetProfile` overwrite of an all-user profile with its protected key
  succeeds as this standard user (granted access `0x70023` says yes) and that `WlanConnect` isn't gated under the
  policy (Microsoft's list says it isn't). AC 10 and 11 decide; if overwrite is refused the switch is hidden and the
  row says "Change in Windows Settings" (KI-117).
- `ms-settings:network-wifi` is assumed to list networks with Explorer's taskbar hidden; `ms-availablenetworks:`
  (Windows' flyout) is not used because it likely needs Explorer's taskbar (spec 0019 spike item 9). AC 12 checks.
- The consent store layout and the MDM/AppPrivacy policy values are undocumented (KI-086); a wrong read only changes
  the notice wording, never what is called — a refused gated call still falls back to the hedged notice.
- `wiFiControl.CheckAccess()` misreporting under the policy is observed on one build (26300); the gate makes the
  panel independent of it.
- Saved profiles on other adapters: the panel still uses the first Wi-Fi interface (three "Intel Wi-Fi 7" entries
  exist here, two *Not Present*; `WlanEnumInterfaces` only returns present ones).
- **Open question for the user:** should the policy notice include the exact registry value and a *Copy* button so
  an administrator can remove it, or only the plain sentence? Assumed: plain sentence plus the value name
  ("Turn off location", `DisableLocation`) in the subtitle, no command.
- KNOWN_ISSUES: revise **KI-108** (gate now from policy and consent reads; nearby list impossible under policy; hand-off
  to Windows); **KI-109** drop *Saved Networks*; new **KI-117** (S4): auto-connect edit rewrites the profile XML,
  group-policy profiles are read-only, the connected row is matched by SSID bytes (two profiles for one SSID mark only
  the first).
