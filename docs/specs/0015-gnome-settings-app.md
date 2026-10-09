# 0015 — A GNOME Settings app for Windows

Status: Agreed (MVP scope chosen by the user on 2026-10-09: native panels for daily settings, links for the rest)

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
auto-revert timer; nothing else needs restoring. No HKLM writes, no elevation.

## Footprint
Nothing while the window is closed. Services exist only for the open panel.

## Acceptance criteria
1. Core tests for the catalogue, arrangement geometry, keep/revert state machine and presets.
2. Manual per panel: read matches Windows Settings; a change shows up in Windows Settings; Displays reverts after
   15 s without confirmation; undocumented-API failure falls back to the link.
3. Every linked panel opens the right `ms-settings:` page.
4. Light/dark, 100 % and 125 %.

## Risks and open questions
- Undocumented APIs (`IPolicyConfig`, display scale, power mode) can break with Windows updates.
- A bad `SetDisplayConfig` could blank a screen: validate first, auto-revert always.
- Policy-managed machines may block changes: show the error state.
