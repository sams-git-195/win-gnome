# 0017 — GNOME on-screen display and Night Light / Do Not Disturb toggles

Status: Agreed — user decisions 2026-10-09; advisor review (Fable) applied 2026-10-09. Ready for implementation after spec 0010; spike first.

## Problem
Volume and brightness changes show Windows' own flyout (a Windows 11 pill with a media card), which looks nothing
like GNOME and sits where the dock is. WinGnome shows nothing for these changes. Quick settings has no Night Light
or Do Not Disturb toggle (README roadmap item 8), so both need a trip to Windows Settings or Windows' quick settings.
Spec 0013 lists the OSD and Do Not Disturb as things shell mode must provide anyway; this spec builds them for
companion mode first.

## Behaviour
User decisions (2026-10-09), in substance:
1. **GNOME OSD** for volume (level and mute) and screen brightness: a rounded pill near the bottom centre of one
   monitor with a symbolic icon and a level bar, fading out about 1.5 s after the last change. It reacts to the
   actual change (any source: keys, hardware buttons, headset controls, other apps, Windows' flyout), not to keys.
   It does **not** show for changes made with WinGnome's own sliders (quick settings, Settings → Sound). Caps Lock
   is included if the spike confirms the cheap raw-input path (see below); the input-source OSD is not (Non-goals).
2. **Windows' volume/brightness flyout is hidden** while WinGnome runs, so only the GNOME OSD shows. One mechanism
   on every machine (minimising Explorer's flyout host, Design), isolated, off in `--safe`, failure detected with
   fallback to both visible, restored on exit, crash and force-kill.
3. **Quick settings gains Night Light and Do Not Disturb toggles** that really change the setting, read it back to
   verify, and otherwise open `ms-settings:nightlight` / `ms-settings:notifications` and log. They are the user's own
   choices: **not restored on exit** (like the Settings panels, PLAN.md safety rules).

### OSD
- Look (GNOME 46): a pill 300 × 48 DIPs (`CornerRadius` 24), `CardBackgroundBrush` at 95 % opacity, a 16 DIP
  symbolic icon (`audio-volume-*`, `display-brightness-symbolic`, `caps-lock-symbolic` added to
  `Theme/SymbolicIcons.xaml`), and a 4 DIP rounded level bar in `AccentBrush` on `BorderBrush`. No percentage text
  (GNOME has none). Muted: muted icon, bar empty. Caps Lock: icon and the text "Caps Lock on"/"Caps Lock off", no
  bar. Light and dark follow `General.Theme`.
- Position: centred horizontally in the chosen monitor's work area, bottom edge 64 DIPs above the work-area
  bottom (clears a reserved dock; an intellihide/autohide dock may be overlapped while the OSD is up).
- Monitor (decided by the user 2026-10-09): **every monitor**, as in GNOME. One pill per monitor, each centred in
  that monitor's work area at its own DPI; all show, update and fade together. Pill windows are created lazily per
  monitor and dropped when a monitor goes away (`OsdMonitorChoice` returns the list of monitors).
- Timing: appears at once (no fade-in), each further change updates it in place and restarts the timer; after
  1500 ms without a change it fades out over 200 ms. Never takes focus or clicks (no-activate, click-through,
  topmost tool window, not in Alt+Tab).
- Changes that do **not** show it: a WinGnome slider or Sound-panel change; the first reading after a device
  (re)bind or default-device switch; a "change" to the same level and mute state; brightness changes within 5 s of
  resume, display-on, or a power-source change; **any** brightness change while Windows' adaptive or
  content-adaptive brightness is on (automatic changes can't be told apart from key presses without watching keys,
  so the brightness OSD is off in that mode; logged once, and the Settings row subtitle says so).
- Caps Lock: shown on each Caps Lock toggle when `Osd.ShowCapsLock` is on, observed through raw input (Design),
  independent of `HideWindowsOsd`. If the spike shows the toggle state can't be read reliably that way, Caps Lock
  is dropped from v1 together with its setting row (KI-097).

### Hiding Windows' flyout
- **One mechanism:** Explorer's flyout host window is **minimised** (the HideVolumeOSD technique). A minimised host
  hides the flyout for every source at once: volume keys, Fn brightness keys, headset and keyboard knobs, Bluetooth
  AVRCP, `WM_APPCOMMAND` from apps, and keys pressed while an elevated window has focus. Windows itself still
  handles every key (one volume step per press, its own step size, `WM_APPCOMMAND` delivery unchanged); only its
  pop-up stops appearing. WinGnome never intercepts or re-implements volume keys and adds no keyboard hook.
- The window is identified **by behaviour** (Design): Windows has to show its flyout to create it, so after each
  start or Explorer restart Windows' flyout appears alongside the GNOME OSD for the first two changes, then never
  again. WinGnome never synthesises keys to provoke it.
- If identification fails (no match, or two different windows), nothing is touched and both OSDs show (logged once).
- Never in `--safe`/`--selftest`; never while `Osd.Enabled` is off (no feedback at all would be left).
- After "show desktop" twice (Win+D) or Win+Shift+M restores minimised windows, the host may come back; it is
  minimised again on the next volume or brightness change (Windows' flyout may show once then).

### Quick settings toggles
- Two new tiles in the tile grid after Bluetooth: **Night Light** (`night-light-symbolic`) and **Do Not Disturb**
  (`notifications-disabled-symbolic`), styled like the Wi-Fi tile: accent background while on.
- State is read when the card opens (like brightness) and after each own write; nothing is watched while closed.
- Click: the tile shows a busy state, the change is written on a worker thread, then read back. The first
  successful read-back sets the tile to the new state. A second read 1 s later confirms Windows didn't rewrite the
  value; only if it disagrees does the tile change again (to the state read), with `Log.Warn` and the Settings page.
  Not verified within 3 s, or the read/write failed, or the format isn't recognised → the tile shows the state
  actually read, `Log.Warn` records why, and the matching Windows Settings page opens. A tile whose state can't be
  read at all shows "Open settings" as its subtitle and always opens the page.
- Do Not Disturb on = Windows' "Priority only" profile (what Windows 11's own toggle sets; capture confirms);
  off = "Unrestricted". "Alarms only" reads as on; turning it off sets Unrestricted. Automatic DND rules
  (full screen, games, presenting) are Windows' and aren't shown or changed.
- `--safe`: tiles show the state read; a click opens the Windows Settings page without writing ("Safe mode: did
  not …" logged).

### Settings (all new; old settings files load with these defaults)
New `AppSettings.Osd` (`OsdSettings`), shown on **WinGnome → General** in a new group **"On-screen display"**:

| Setting | Type | Default | Row |
|---|---|---|---|
| `Osd.Enabled` | bool | `true` | "Show volume and brightness pop-ups" (subtitle when adaptive brightness is on: "Brightness pop-ups are off while adaptive brightness is on") |
| `Osd.HideWindowsOsd` | bool | `true` | "Hide Windows' volume and brightness pop-up" (subtitle: "Undone when WinGnome exits; after a forced quit, by the next start or `--restore-taskbar`. Not in safe mode.") |
| `Osd.ShowCapsLock` | bool | `true` | "Show Caps Lock changes" (no dependency on `HideWindowsOsd`; row removed if the spike drops Caps Lock) |

`HideWindowsOsd` and `ShowCapsLock` only apply while `Enabled` is on (the row is disabled otherwise).
`Normalize()` adds `Osd ??= new()`; no schema bump. The toggles add no settings.

## Non-goals
- Input-source OSD (no cheap event; Windows' Win+Space switcher stays), keyboard-backlight, microphone OSDs.
- OSD for external-monitor (DDC/CI) brightness or monitor buttons (spec 0011 is a draft; no change event exists).
- Media controls in the OSD (Windows' flyout shows a media card; GNOME's OSD doesn't).
- Intercepting or re-implementing volume keys; changing Windows' volume step.
- Night Light strength or schedule, DND priority list or rules: those stay links (Displays/Notifications pages).
- Restoring Night Light or DND on exit. Changing other WinGnome instances' OSDs.

## Design
### Research: hiding Windows' flyout
| Approach | Verdict |
|---|---|
| Minimise Explorer's flyout host (`ShowWindow(SW_MINIMIZE)`, HideVolumeOSD technique) | **Chosen, the only mechanism.** No injection, no admin, no hook; covers every source, including elevated focus and brightness keys. Undocumented; the minimised state outlives a force-killed WinGnome until restored (marker) or Explorer restarts. |
| Low-level keyboard hook swallows `VK_VOLUME_*`, WinGnome sets the volume | **Rejected (advisor review).** Misses Fn brightness, headset/AVRCP, `WM_APPCOMMAND` and elevated focus, so the minimise is needed anyway; a hook timeout passes the key on after WinGnome may already have applied it (double step); swallowing loses `WM_APPCOMMAND` for apps; and it would be a second `WH_KEYBOARD_LL` (0016 allows only `InputHookHost`). |
| Shell hook / taskman window handling `HSHELL_APPCOMMAND` | Only Explorer as taskman gets it; companion mode can't take it (shell mode, spec 0013). |
| Explorer patching (Windhawk-style), ModernFlyouts-style replacement | Injection — excluded (KI-008). |
| A registry or policy switch | None exists. |

### Core (`WinGnome.Core`, all tested)
- `Osd/OsdKind`, `Osd/OsdContent` (record: kind, level 0..1 or null, muted, caps state; `IconFor`, reusing
  `VolumeLevel.IconFor`).
- `Osd/OsdTimeline`: show/fade state machine on injected `long` milliseconds: `Show(now)` → `Visible`, deadline
  now+1500; `Tick(now)` → `FadingOut` (200 ms) → `Hidden`; a `Show` during the fade returns to `Visible` at full
  opacity. Exposes the next deadline so the app runs one one-shot timer.
- `Osd/OsdTriggerPolicy`: decides show/don't show from (kind, previous and new value, own-change flag, device
  rebind flag, ms since resume/display-on/power-source change, adaptive-brightness flag). Pure; one `[Theory]` row
  per rule in Behaviour.
- `Osd/OsdPlacement.Compute(PixelRect workArea, double scale, LayoutSize sizeDip, double bottomGapDip)` →
  `PixelRect` on whole pixels; negative-coordinate monitors, 100/125/150/175 %, work area smaller than the pill
  (clamped inside), mixed-DPI inputs.
- `Osd/OsdMonitorChoice`: (kind, internal-panel monitor id?, foreground monitor id?, foreground-is-shell flag,
  pointer monitor id) → monitor id.
- `Osd/SelfChangeFilter`: a brightness notification is "own" when it equals the last level this instance wrote
  within 1500 ms of the write, or arrives while a slider drag is marked active. Injected clock. Used inside the one
  shared `BrightnessController` (App, below). Volume uses Core Audio event contexts instead.
- `Osd/FlyoutIdentifier`: pure matcher for behaviour-based identification. Fed `Shown(hwnd, class, ms)` events
  (from Explorer's process only) and `Changed(ms)` notifications; a candidate is a window shown within 300 ms
  either side of a change whose class passes the sanity filter (`XamlExplorerHostIslandWindow`, or `NativeHWNDHost`
  before build 22000). Two changes matching the **same** hwnd → `Identified(hwnd)`; two different hwnds, or more
  than one candidate for one change → `Ambiguous` (stop, log). Shows outside the window and other classes are
  ignored. Bounded history (last 8 shows).
- `ControlCenter/CloudStoreBlob`: parser/serialiser for Windows' CloudStore "CB" values. Outer: magic
  `43 42 01 00`, `0A 02 01 00`, `2A 06` + LEB128 last-write time (Unix seconds), `2A 2B 0E` + LEB128 payload length,
  payload, then trailing `00 00 00`. Payload: magic `43 42 01 00` then tagged fields ending in `00`. The parser
  exposes the fields it knows and keeps every other byte verbatim, so `Serialise(Parse(x)) == x` for every
  fixture. Writing sets the timestamp to `max(old + 1, now)` (Windows ignores a write whose time didn't advance;
  `old + 1` covers a clock behind the stored value) and recomputes the length. Malformed input (bad magic,
  truncated varint, length past the end) returns a failure, never throws.
- `ControlCenter/NightLightState`: from a payload, `Active` = the `10 00` flag directly after the payload magic
  (older builds: inserted on enable, removed on disable, with `D0 0A 02` following). `WithActive(bool, now)`.
- `ControlCenter/QuietHoursState`: decodes `windows.data.donotdisturb.quiethourssettings`: field `D2 14` + LEB128
  char count + UTF-16 profile id (`Microsoft.QuietHoursProfile.Unrestricted | PriorityOnly | AlarmsOnly`);
  `IsOn`, `WithProfile(id, now)` (rebuilding the string field and length).
- `ControlCenter/VerifiedToggleFlow`: Idle → Writing(target) → Verifying → Shown(state) → Confirming →
  Done(state) | Corrected(state, reason); any step can go to Fallback(reason). The first good read-back moves to
  `Shown` (tile updates); the 1 s re-read either confirms (`Done`) or disagrees (`Corrected`: tile changes, page
  opens). Clicks while busy ignored; timeout at 3 s; safe mode goes straight to Fallback(SafeMode). Tested with
  injected results and clock.

Fixtures (`tests/WinGnome.Core.Tests/TestData/CloudStore/`) are hex files named
`<build>_<value-kind>_<state>.hex` (for example `26200_bluelightreductionstate_off.hex`), each with a one-line
note (`#` comment, ignored by the loader) of the captured state and how it was produced (for example "Night Light
on via Windows quick settings, schedule off"). Already captured on 10.0.26200 (both off):
- `default$…bluelightreductionstate`:
  `434201000A0201002A06C8E1AFD2062A2B0E1043420100C6148EB099B7B498B6EE0100000000`
- `{device-guid}$…bluelightreductionstateperdevice`:
  `434201000A0201002A06CE98EECF062A2B0E1143420100C6148EB099B7B498B6EE010100000000`
- `{device-guid}$…quiethourssettings` (Unrestricted):
  `434201000A0201002A06DA96CECF062A2B0E5E43420100C20A01D21428` + UTF-16 "Microsoft.QuietHoursProfile.Unrestricted"
  + `CA280000000000`

The implementer captures the rest with a throwaway PowerShell script outside the repo: export every
`bluelightreduction*` and `donotdisturb*` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current`, toggle the feature in
**Windows'** quick settings, export again, and diff (Night Light on/off; DND on, Priority only, Alarms only, off).
This also records **which** key Windows writes on this build (the classic `default$` key, the newer
`{device-guid}$…perdevice` key, or both). Earlier-build blobs from public write-ups (the `0E 13`/`0E 15` Win10/11
21H2 shape) are added as further fixtures if their source is quotable.

### App
- `Features/Osd/OsdFeature.cs` (`[FeatureOrder(25)]`, `IFeature`, `IEmergencyRestore`): owns everything below,
  starts when `Osd.Enabled`, tears down when off. Holds the new `SessionRole` `Local\WinGnome-Osd`; only the holder
  shows OSDs, observes Caps Lock and minimises the flyout; others wait and take over when it quits.
- `Features/Osd/OsdWindow.xaml(.cs)`: created on the first show and then kept hidden (never rendered while hidden);
  `ShellSurface.MakeNonActivating(topmost: true)`. Click-through is `WS_EX_TRANSPARENT` on the layered
  (`AllowsTransparency`) window; nothing else is needed. Positioned while hidden with `SetWindowPos` (physical pixels
  from `OsdPlacement`), re-applied on `WM_DPICHANGED` (as `TopBarFeature.Dock` does), then shown with
  `SW_SHOWNOACTIVATE`. Fade is a 200 ms `DoubleAnimation`; no per-frame work otherwise.
- `Features/Osd/OsdPresenter.cs`: dispatcher-only; feeds `OsdTimeline`, one one-shot `DispatcherTimer`, resolves
  monitors with `MonitorFromWindow`/`MonitorFromPoint` (reading an elevated window's position is fine; nothing is
  ever done to it). Raises a UIA notification ("Volume 40 %") from the window's peer for screen readers.
- **One shared volume controller:** move `AudioVolumeController` from `Features/TopBar/Services` to
  `Services/Audio/` (pure move, own commit), created once by `App` and exposed as `ShellContext.Audio`, disposed
  after all features. `Changed` becomes `EventHandler<VolumeChange>` with level, mute, the notification's
  `guidEventContext`, and `IsOwn` (the context is one this instance handed out). Each writer gets its own context
  from `CreateWriter()` and passes it to `SetLevel`/`SetMuted`; the top bar ignores only its own context (so it
  still follows Sound-panel changes), the OSD ignores every `IsOwn` change. The Sound panel's `AudioDevices` takes a
  context from the shared controller for its default-device master volume writes (its per-device and per-session
  writes are not the default endpoint's master volume and raise no OSD). `SystemStatusViewModel` stops creating its
  own controller. The Core Audio volume callback is already always on for the top bar, so sharing adds no idle cost.
- **One shared brightness controller:** `BrightnessController` moves to `Services/Brightness/` the same way and is
  exposed as `ShellContext.Brightness`. It owns the one `SelfChangeFilter` (records each level it writes; the
  slider marks drags active from its thumb drag events) and gains `ReportSystemLevel(int percent)`, called by the
  OSD feature's notification window, which raises `Changed` with `IsOwn` from the filter. No
  `ShellContext.OwnChanges` member.
- **Brightness notification:** `RegisterPowerSettingNotification(GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS
  {8FFEE2C6-2D01-46BE-ADB9-398ADDC5B4FF})` on a message-only window owned by the feature (`WM_POWERBROADCAST` /
  `PBT_POWERSETTINGCHANGE` carries the new percent; no WMI at idle). Same registration for
  `GUID_CONSOLE_DISPLAY_STATE` and `GUID_ACDC_POWER_SOURCE` (inputs to the trigger policy), plus
  `PBT_APMRESUMEAUTOMATIC`. Adaptive brightness is read from the active scheme
  (`GUID_VIDEO_ADAPTIVE_DISPLAY_BRIGHTNESS`) once at start and on scheme change. **No WMI fallback:** if the spike
  shows the GUID doesn't fire on Fn keys, v1 ships without the brightness OSD (volume OSD and flyout hiding
  unaffected) and KI-096 records it.
- **Internal panel monitor:** the monitor whose `DISPLAYCONFIG` target has an internal output technology
  (`DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL`/embedded DisplayPort), via the existing `NativeMethods.DisplayConfig`
  declarations; re-read on display change.
- **Caps Lock (raw input, no hook):** if `ShowCapsLock` is on, the feature's message-only window registers
  `RegisterRawInputDevices` for the keyboard usage (page 1, usage 6) with `RIDEV_INPUTSINK`. Raw input is a copy
  of the input delivered as a posted `WM_INPUT`; it can't swallow or delay keys and has no hook timeout. The
  handler reads the `RAWINPUT` header into a stack buffer, ignores everything but `VK_CAPITAL` make codes (no
  repeats), and then reads the toggle state; the spike decides how (`GetKeyState(VK_CAPITAL) & 1` after the event,
  or tracking from an initial read) and checks delivery while an elevated window has focus. If neither is
  reliable, Caps Lock is dropped from v1 and the row removed (KI-097). Unregistered (`RIDEV_REMOVE`) when the
  setting goes off. Raw keyboard registration is process-wide per usage, so nothing else in WinGnome may register
  it (checked; none today). If key observation through a hook is ever needed instead, it goes through 0016's
  `InputHookHost` as one `ShortcutRouter` branch, never a second `WH_KEYBOARD_LL`.
- `Features/Osd/WindowsFlyoutSuppressor.cs` — **the only undocumented code for the OSD.** Runs on every machine
  when `Enabled && HideWindowsOsd && !IsSafeMode` and the session role is held.
  - *Identification by behaviour:* while unidentified, a `SetWinEventHook(EVENT_OBJECT_SHOW, …,
    WINEVENT_OUTOFCONTEXT)` filtered to Explorer's pid (`GetShellWindow` → `GetWindowThreadProcessId`) reports
    top-level shows (`idObject == OBJID_WINDOW`, no parent) with their class to `FlyoutIdentifier`; every non-own
    volume change and every brightness notification is reported as a change. On `Identified`, the WinEvent hook is
    removed. On `Ambiguous`, the hook is removed, nothing is minimised, and it is logged once. The class check is
    only a sanity filter; behaviour (shown with a change, twice, same window) is what identifies it.
  - *Minimise:* before `ShowWindow(SW_MINIMIZE)`, writes `windows-flyout.state` (`{pid, hwnd, class,
    processStartTime, threadId}`) to the profile folder (as `TaskbarController` does with `taskbar.state`; no marker
    → no minimise). Then checks `IsIconic`; not iconic → restore path, log, both OSDs show.
  - *Staying minimised:* on each later change, if the recorded window is no longer iconic (Win+D twice,
    Win+Shift+M) it is minimised again; if it no longer matches the marker (destroyed, reused), the marker is
    cleared and identification starts again.
  - *Restore:* only from this profile's own marker; only if hwnd, class, pid, process start time and thread id all
    still match (HWND reuse). Sequence decided by the spike: `SW_RESTORE` if it doesn't flash an empty pill, else
    `SW_SHOWNOACTIVATE` then `SW_HIDE`; then the marker is deleted.
  - *Explorer restart* (`TaskbarCreatedListener`): the old window is gone, so the marker is cleared (pid/start time
    no longer match) and identification re-arms.
- **Night Light:** `Services/SystemToggles/NightLightService.cs` (shared, not a feature): `ReadAsync`,
  `SetAsync(bool)` on the thread pool, serialised by a `SemaphoreSlim(1)`; HKCU only through `Registry.CurrentUser`;
  parse/serialise in Core. The **only undocumented code for Night Light.** Write sequence:
  1. Read the value(s): the classic `default$…bluelightreductionstate` and the per-device key. Every key that
     exists and parses is written; a key that exists but doesn't parse aborts the whole write (fallback).
  2. Runtime round-trip guard: `Serialise(Parse(current)) == current` byte-for-byte for each, else no write and the
     page opens.
  3. Keep the pre-write bytes in memory and write them to `nightlight.bak` in the profile folder (overwritten each
     time; a failed `.bak` write aborts).
  4. Write, then verify each written key by re-reading and parsing (and the 1 s re-read, Behaviour).
  5. On verification failure, write the original payloads back with a fresh timestamp (`max(written + 1, now)`),
     log what was written, read and restored, and open the page. Not retried.
- **Do Not Disturb:** `Services/Notifications/DoNotDisturbService.cs` (shared; specs 0019/0020 reuse it with no
  change): same shape (`IsAvailable`, `ReadAsync`, `SetAsync(bool)`, `Changed`). The **spike** chooses the write
  channel in this order and the class isolates whichever wins: (1) the in-box quiet-hours COM server in
  `quiethours.dll` (`IQuietHoursSettings::put_UserSelectedProfile`; CLSID/IID taken from the spike, from the
  module's registration, not guessed); (2) the CloudStore blob write (with the Night Light guards above).
  Verification reads back **through the channel that wrote** (COM `get_UserSelectedProfile`, or the blob). The
  other channel is read only as a log-only cross-check in v1 (a mismatch is logged, never acted on). COM calls run
  on the thread pool (MTA), serialised by a `SemaphoreSlim(1)`; `RPC_E_DISCONNECTED`/`RPC_S_SERVER_UNAVAILABLE`
  (Explorer or the service restarted) → release and recreate the object once, then retry the call once. The **only
  undocumented code for DND.**
- `QuickSettingsCard.xaml(.cs)` + `SystemStatusViewModel`: two tiles bound to `NightLightToggle` and
  `DoNotDisturbToggle` view models (`IsOn`, `IsBusy`, `Subtitle`, `ToggleCommand`) driven by `VerifiedToggleFlow`;
  fallbacks open pages through `TopBarActions` (`TopBarAction.NightLightSettings`, `.NotificationSettings`).
  `TopBarWindow` refreshes them when the card opens, beside `RefreshBrightness`.
- Settings: `OsdSettings` in `AppSettings.cs`; rows in `GeneralPage.xaml` / its view model.
- Interop: `RegisterPowerSettingNotification`/`UnregisterPowerSettingNotification` and the GUIDs in
  `NativeMethods.Power.cs`; `RegisterRawInputDevices`/`GetRawInputData` and `RAWINPUT*` structs in
  `NativeMethods.Input.cs`; `SetWinEventHook`/`UnhookWinEvent` (reuse if present), `IsIconic`, `GetShellWindow`,
  `GetWindowThreadProcessId` in `NativeMethods.cs` if absent. Check `Interop/` before adding (0016 edits
  `NativeMethods.Input.cs` too).

### Threading, hostile cases
- UI dispatcher: OSD window, timeline, presenter, toggles' view models, the message-only window (power
  notifications, `WM_INPUT`) and the out-of-context WinEvent callback. Core Audio notifications only marshal to it.
  Registry/COM work for the toggles on the thread pool; results marshalled back; results after `Dispose` ignored.
- Explorer restart: `TaskbarCreated` → marker cleared, identification re-armed. Sleep/resume and display changes:
  placement is computed per show; the internal-panel monitor is re-read on display change; power-setting
  registrations survive. Session end: `Dispose` restores the flyout. Second WinGnome instance: only the
  `Local\WinGnome-Osd` holder shows OSDs and minimises (no double OSDs); each profile restores only its own marker.
  Elevated windows: never touched; their volume keys are handled by Windows once and the flyout stays minimised.

### Work packages
1. **Core** (`src/WinGnome.Core/Osd/` incl. `FlyoutIdentifier`, `SelfChangeFilter`, `ControlCenter/CloudStoreBlob.cs`,
   `NightLightState.cs`, `QuietHoursState.cs`, `VerifiedToggleFlow.cs`, tests, fixtures). Starts now.
2. **Spike** (no repo changes), first: fixture capture; which Night Light key(s) Windows writes; DND write channel;
   does `GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS` fire on Fn keys, adaptive brightness, battery saver; flyout show
   events from Explorer's pid around a change, minimise and restore behaviour (empty-pill flash), Win+D twice and
   Win+Shift+M on 26200; Caps Lock raw input toggle read and elevated focus. Parallel with 1. Gates 3–6.
3. **OSD surface** (`Features/Osd/OsdFeature.cs`, `OsdWindow`, `OsdPresenter`, `Services/Audio/` and
   `Services/Brightness/` moves on `ShellContext`, brightness notification, Caps Lock raw input, settings rows).
   After 1 and spec 0010.
4. **Hiding Windows' flyout** (`WindowsFlyoutSuppressor.cs`, WinEvent hook, marker, emergency restore,
   `--restore-taskbar` hook-up). **Safety-critical.** After 2 and 3.
5. **Night Light** (`Services/SystemToggles/`). **Safety-critical (registry writes).** After 1 and 2; parallel with 6.
6. **Do Not Disturb** (`Services/Notifications/`). **Safety-critical (undocumented).** After 1 and 2.
7. **Quick settings tiles** (`QuickSettingsCard`, `SystemStatusViewModel`, `TopBarActions`). After 5 and 6
   interfaces exist (stubs first).
Interop partials are shared: packages 3–6 add declarations in separate commits after checking `Interop/`.

## Safety and recovery
- **OSD, Caps Lock:** no system state. Raw input registration dies with the process.
- **No keyboard hook** is added by this spec.
- **Flyout minimise:** recorded in `windows-flyout.state` before `ShowWindow`. Restored on: setting off, normal exit
  (`Dispose`), crash (`EmergencyRestore`: plain Win32 + file read, no WPF), next start after a force-kill (marker
  present → restore if the window still matches, then decide afresh), `--restore-taskbar` (also restores the flyout
  from the marker), and implicitly by an Explorer restart or sign-out (a new window). Each profile acts only on its
  own marker. `--safe` and `--selftest` never minimise, but **do** restore from a marker left by an earlier run.
- **Night Light / DND:** user choices, not tweaks: no restore on exit (the user's decision, matching the Settings
  panels). HKCU only. Writes only the value(s) that exist and parse, after the round-trip guard; the pre-write bytes
  are kept in memory and in `nightlight.bak`, and written back (fresh timestamp) if verification fails. A failed
  write is not retried. Nothing is written in `--safe`/`--selftest`.

## Footprint
- At idle: no new Core Audio callback (the top bar's controller is shared), three power-setting registrations, no
  timers, no polling, no keyboard hook. The OSD window exists only after the first OSD and is hidden (no rendering)
  between shows; one one-shot timer and a 200 ms fade while visible.
- Flyout identification: one out-of-context WinEvent hook for Explorer's show events, only until the window is
  identified (normally the first two changes after start or Explorer restart), then removed.
- Caps Lock (if kept): one posted `WM_INPUT` per key event to the UI thread, discarded after a header read unless
  it is Caps Lock. Never blocks input. Measured in QA 6 while typing.
- Toggles: one registry read (and the DND channel) per card open; nothing while closed.
- Check: idle CPU/memory before and after in Task Manager (AGENTS.md QA 6).

## Acceptance criteria
1. `OsdTimeline`: show, update-in-place restarts the 1500 ms timer, fade 200 ms, show during fade returns to
   visible; exact deadlines. (Core)
2. `OsdTriggerPolicy` returns the expected show/don't-show for: own change; first reading after rebind; unchanged
   level and mute; brightness at 4999/5000/5001 ms after resume, display-on and power-source change; brightness with
   adaptive brightness on (never shown) and off (shown); volume with adaptive brightness on (shown). (Core)
3. `OsdPlacement`: centred, 64 DIP gap, whole pixels at 100/125/150/175 %, negative-origin monitor, tiny work area.
   `OsdMonitorChoice`: each fallback. (Core)
4. `FlyoutIdentifier`: two matches of one hwnd within 300 ms (both sides, and at 300/301 ms) identify it; one match
   doesn't; two different hwnds or two candidates for one change → Ambiguous; wrong class or other shows ignored.
   `SelfChangeFilter`: own at 1499/1500 ms, not own at 1501 ms, own during a drag, different level not own. (Core)
5. `CloudStoreBlob`: round-trips every fixture byte-for-byte; timestamp is `max(old + 1, now)` including a clock
   behind the stored time; malformed fixtures (bad magic, truncated varint, overlong length, empty) fail without
   throwing. (Core)
6. `NightLightState` and `QuietHoursState`: decode every captured fixture to the state noted in it; `WithActive`/
   `WithProfile` produce exactly the bytes Windows wrote for the same transition (from the captured pairs,
   timestamp aside). (Core)
7. `VerifiedToggleFlow`: success, failed write, failed read-back, timeout, click while busy, safe mode, 1 s re-read
   agreeing (`Done`) and disagreeing (`Corrected`). (Core)
8. With hiding on, after Windows' flyout has been seen twice: volume keys, Fn brightness keys and a hardware or
   headset volume control each show the GNOME OSD on the documented monitor and never Windows' flyout. Changes made
   with the quick-settings slider or the Sound panel show neither. (QA)
9. Elevated window focused: volume keys show the GNOME OSD only, and the volume moves exactly one step per press. (QA)
10. Flyout restore: quit normally, crash (temporary `throw`), `taskkill /f` + restart with the same profile,
    `--restore-taskbar`, and Explorer restart → Windows' flyout appears again on the next key press, with no empty
    pill flashing at restore. Win+D twice and Win+Shift+M → re-minimised on the next change. A second profile's
    instance never restores the first's marker. `--safe` never hides it. (QA)
11. Night Light and DND tiles: toggle on/off matches Windows' own quick settings and Settings pages; a forced
    verification failure (a test build that corrupts the read-back) writes the original bytes back, opens the right
    page and logs; `nightlight.bak` holds the pre-write bytes. `--safe` opens the page without writing. (QA)
12. Caps Lock (if kept): toggling shows the right state with `HideWindowsOsd` on and off, and with an elevated
    window focused (or documented as not shown, KI-097). (QA)
13. Light/dark, 100 % and 150 %, a second monitor with a negative origin and different DPI. (QA)
14. Settings: an old `settings.json` without `Osd` loads with the defaults. (Core settings test)
15. Build `-warnaserror`, `dotnet test`, `--selftest --safe` exit 0; idle footprint compared. (QA)

## Proposed KNOWN_ISSUES entries
- KI-093 · S3 · OSD: hiding Windows' flyout minimises an undocumented Explorer window, identified by behaviour; it
  can fail on Windows updates (both flyouts show), Windows' flyout shows for the first two changes after each start
  or Explorer restart, and it stays hidden after a force-kill until WinGnome restarts, Explorer restarts, sign-out,
  or `--restore-taskbar`.
- KI-094 · S4 · Quick settings: Night Light uses Windows' undocumented CloudStore blob (guarded, backed up, rolled
  back on failed verification).
- KI-095 · S4 · Quick settings: Do Not Disturb uses an undocumented channel (named after the spike).
- KI-096 · S4 · OSD: no OSD for external-monitor brightness; no brightness OSD while adaptive/content-adaptive
  brightness is on; no brightness OSD at all if `GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS` doesn't fire (spike).
- KI-097 · S4 · OSD: no input-source OSD; Caps Lock OSD limits (dropped from v1, or not shown with an elevated window
  focused — whichever the spike finds).

## Risks and open questions
- **Decided (user, 2026-10-09): every monitor.** Cost is one small layered window per extra monitor, created on the
  first OSD and hidden (not destroyed) between shows.
- Advisor review (Fable) replaced the earlier two-mechanism design (volume-key hook plus minimise). Every advisor
  point was accepted; none was contradicted by the code. Notes from checking the code:
  `AudioVolumeController.Changed` is a plain `EventHandler` today and `AudioDevices` keeps its own two
  `_eventContext` GUIDs, so the move to `Services/Audio/` changes the event signature and `AudioDevices`' default
  master-volume writes (callers updated in the same commit series). `BrightnessController` has no change
  notification today (it only reads on card open), so `ReportSystemLevel` is new.
- Behaviour-based identification could misfire if Explorer shows another top-level XAML island (for example a
  notification toast) within 300 ms of two consecutive volume changes; requiring the same hwnd twice plus the
  class filter makes this unlikely, and a wrongly minimised island is restored by the same marker paths. The spike
  logs every Explorer show around changes on 26200 to check.
- If the host isn't iconic after `SW_MINIMIZE` on some build, hiding is off and KI-093 says Windows' flyout shows.
- `GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS` is in `winnt.h` but not on Microsoft's documented power-setting list
  (no WMI fallback; KI-096).
- Raw keyboard input is process-wide per usage: a future feature registering it would steal Caps Lock delivery.
  Noted in the class comment.
- Night Light read-back proves the registry, not the screen. Optional stronger check: compare the primary display's
  gamma ramp (`GetDeviceGammaRamp`) before and after; unreliable with HDR and calibrated profiles. Open: worth it?
- 25H2 keeps both a classic and a per-device Night Light key; both are written and verified when both exist and
  parse. If Windows only honours one, the other write is harmless (it is the same toggle).
- A topmost OSD over a full-screen game can briefly knock it out of independent flip.
