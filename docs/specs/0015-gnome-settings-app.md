# 0015 — A GNOME Settings app for Windows

Status: Implemented (MVP), 2026-10-09. Scope chosen by the user on 2026-10-09: native panels for daily settings, links for the rest. Live write checks are pending (KI-063).

## Problem
Windows Settings looks and works nothing like GNOME, and in shell mode (spec 0013) it is one of the Windows
elements the user wants replaced. WinGnome's own Settings window covers only WinGnome.

## Behaviour
- WinGnome's Settings window becomes a GNOME Settings (gnome-control-center) style app: header bar with search, a
  sidebar of panels grouped like GNOME, and WinGnome's own pages (Top Bar, Dock, Window Buttons, Activities,
  Streamline) under a "WinGnome" group.
- **Native panels (MVP):**
  - **Sound**: output and input device lists, set default device, volumes, per-app volumes.
  - **Displays**: resolution, refresh rate, arrangement, primary display, with GNOME's 15-second
    *Keep changes?* auto-revert. Scale behind a guard (undocumented API).
  - **Power**: power mode, screen-off and sleep timeouts, battery status.
  - **Appearance**: light/dark, accent colour, wallpaper.
  - **Mouse & Touchpad**: speed, acceleration, scroll lines, primary button.
  - **Keyboard**: repeat delay and rate, input sources (list and switch).
  - **Date & Time**: time zone, clock format.
  - **About**: Windows version, CPU, memory, GPU, disk.
  - **Multitasking**: hot corner, workspaces, snap settings.
- **Phase 2 native:** Wi-Fi (Native Wifi API; needs location permission on 24H2+ for scan results), Bluetooth
  (WinRT radios/pairing), Network/VPN status, Startup apps, Printers.
- **Linked panels:** every other GNOME panel (Notifications, Privacy, Online Accounts, Sharing, Apps/default apps,
  Region & Language, Accessibility, Users, Windows Update, Colour, Removable media) appears in the sidebar with an
  "opens in Windows Settings" arrow and opens the matching `ms-settings:` page.
- Search filters panels by title and keywords (`FuzzyMatcher`).
- Quick settings rows and the gear open the matching native panel. In shell mode Win+I opens this app.
- `--safe` opens system panels read-only.

## Non-goals
- Re-implementing settings that have no API (default apps, privacy, accounts) or need admin (network IP, other
  users, Windows Update installs): those stay links.

## Design
- Core: `SettingsPanelCatalog` (`Id`, `Title`, `Icon`, `Group`, `Keywords`, `Kind` = Native | Link, `LinkUri`),
  display-arrangement geometry and the keep-or-revert state machine, power timeout presets, time zone list model;
  all tested.
- App: `Features/Settings/Panels/<Panel>/` with one service per panel, created when the panel opens and disposed
  when it closes. Interop in `NativeMethods.DisplayConfig.cs`, `.Power.cs`, `.Input.cs`; `IPolicyConfig` beside
  `CoreAudio.cs`.
- APIs: `QueryDisplayConfig`/`SetDisplayConfig` (validate first); `IMMDeviceEnumerator`, `IPolicyConfig`
  (undocumented, guarded), `IAudioSessionManager2`; `PowerGet/SetActiveScheme`, `PowerWrite*ValueIndex`,
  `PowerSetActiveOverlayScheme` (undocumented, guarded); `IDesktopWallpaper`; existing accent/theme code;
  `SystemParametersInfo`; `ITfInputProcessorProfileMgr`; `SetDynamicTimeZoneInformation` with
  `SeTimeZonePrivilege` enabled; `RtlGetVersion`, `GlobalMemoryStatusEx`, DXGI.
- Same process, loaded on demand like today's Settings window. WinRT (Bluetooth, radios) is phase 2 and loaded only
  by those panels (the projection costs ~15–30 MB once loaded).
- Every undocumented call: feature-checked, failure logged, panel falls back to its link.

## Safety and recovery
These panels change system settings on purpose, so they don't go through the tweak backup. Displays always uses the
auto-revert timer; nothing else needs restoring. WinGnome writes no HKLM keys itself and never elevates; the time zone and the power plan's timeouts and mode are machine-wide, changed through the documented (or, for the power mode, undocumented) APIs that standard users may call, exactly as Windows Settings does.

## Footprint
Nothing while the window is closed. Services exist only for the open panel.

## Acceptance criteria
1. Core tests for the catalogue, arrangement geometry, keep/revert state machine and presets.
2. Manual per panel: read matches Windows Settings; a change shows up in Windows Settings; Displays: apply, then
   `taskkill /f` WinGnome during the countdown, sign out and in, and the original settings are back (the change was
   never saved); apply, kill, restart WinGnome without signing out, and it reverts the change; Displays reverts after
   15 s without confirmation; undocumented-API failure falls back to the link.
3. Every linked panel opens the right `ms-settings:` page.
4. Light/dark, 100 % and 125 %.

## Risks and open questions
- Undocumented APIs (`IPolicyConfig`, display scale, power mode) can break with Windows updates.
- A bad `SetDisplayConfig` could blank a screen: validate first, auto-revert always.
- Policy-managed machines may block changes: show the error state.

## Implementation notes (MVP)

What is native and what is linked, as built:

| Panel | Native | Linked or left to Windows Settings |
|---|---|---|
| Displays | Arrangement (drag, edge snapping), primary display, resolution, refresh rate, 15 s keep-or-revert with a crash-safe revert record (`display-revert.json`) | Scale (shown read-only, KI-062), rotation, HDR, mirroring, turning displays on/off |
| Sound | Output/input device (via `IPolicyConfig`, KI-060), output volume and mute, input volume, per-app volume | Per-app devices (volume mixer link), sound effects |
| Power | Battery state, power mode (KI-061), screen blank and automatic suspend per power source | Lid and button actions, plans |
| Mouse & Touchpad | Primary button, pointer speed, acceleration, scroll lines | Touchpad gestures and tapping (link) |
| Keyboard | Repeat delay and rate, input sources (listed, KI-066) | Adding/removing sources, typing settings (links) |
| Appearance | Style, accent (GNOME's nine colours or automatic), wallpaper (Windows' pictures or a file) | Themes, contrast |
| Multitasking | Hot corner, workspace indicator (WinGnome), snap, snap layouts, snap suggestions, Alt+Tab scope (KI-064) | — |
| Date & Time | Time zone (searchable; `SetDynamicTimeZoneInformation` with `SeTimeZonePrivilege`), top bar clock format | Automatic date, time and time zone (link) |
| About | Device name, hardware model, memory, processor, graphics, disk, Windows version | Rename, product key (link) |
| Wi-Fi, Network, Bluetooth, Printers, Removable Media, Colour, Notifications, Apps, Default Apps, Online Accounts, Sharing, Privacy & Security, Region & Language, Users, Accessibility, Windows Update | — | Matching `ms-settings:` page (Colour opens `colorcpl.exe`) |

Design decisions taken while building:
- Display changes use the documented CCD API: `QueryDisplayConfig`, then the source modes (size, position) and, for a
  new refresh rate, the path's refresh rate are rewritten, and `SetDisplayConfig` with the supplied configuration
  validates (`SDC_VALIDATE`) and applies (`SDC_APPLY | SDC_ALLOW_CHANGES`) all displays at once, so a resize that moves
  a neighbour or a change of primary lands atomically. The apply leaves out `SDC_SAVE_TO_DATABASE`, so an unconfirmed
  change lasts for the session only; *Keep Changes* saves it. Mode lists, names and the cheap current-settings read
  still use `EnumDisplaySettingsEx`/`QueryDisplayConfig`. Changes are staged in the panel and applied with *Apply*.
- The safety order lives in Core (`DisplayChangeFlow`, tested with injected system calls): at *Apply* the current
  settings are re-read and the configuration validated, the original and target are written to `display-revert.json`,
  then the target is applied for the session; a failed apply reverts at once. *Keep Changes* deletes the record on the
  UI thread before the save is queued. Reverting applies the database's configuration (`SDC_USE_DATABASE_CURRENT`),
  then the original explicitly if needed, and deletes the record only when the original shows again. The countdown
  reverts on timeout, on *Revert*, and when the panel is left or the window closed (on the display queue); at WinGnome
  shutdown it reverts before returning. Start-up recovery runs on the same queue (never interleaving with the panel),
  reverts only if the recorded target is still showing, and keeps the record for the next start if the revert fails.
- Every write runs on `SystemSettingWriter` (ordered, off the UI thread, logged, nothing in safe mode), except the
  sound volume sliders, which call Core Audio directly like the top bar does.
- Rows whose value a Streamline tweak owns (dark mode, accent, snap layouts) are read-only while that tweak is on
  (KI-067), so reverting a tweak never silently undoes a panel change.
- Quick settings rows and Win+I still open Windows Settings pages (KI-065); `ShellCommands.ShowSettings(panelId)`
  and `--settings-panel <id>` are ready for them.