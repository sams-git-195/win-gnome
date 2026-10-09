# 0013 — WinGnome as the Windows shell

Status: Draft — direction agreed with the user on 2026-10-09 (opt-in, with automatic fallback to Explorer). Phase 1
needs the verification spike below before it is agreed for implementation.

## Problem
WinGnome runs on top of Explorer: it hides Explorer's taskbar, forwards tray icons to it and works around it (KI-019,
KI-022, Explorer re-showing its taskbar). The user wants a full GNOME-like shell that replaces as much of the
Windows shell as possible while every Windows app keeps working.

What can and can't be replaced: the *shell* (the process Winlogon starts at sign-in: desktop, taskbar, tray, Start,
hotkeys) can be replaced per user. The *compositor and window manager* (DWM) can't: window frames, animations and
some snapping stay Windows'. So shell mode is "a GNOME-like shell for Windows apps", never GNOME itself.

Most of Windows 11's shell lives in or is started by `explorer.exe`: virtual desktops, toasts, Alt+Tab and Task View,
Snap Layouts, Quick Settings, Start, Win+ hotkeys, OSDs. Running Explorer in the background without it being the
shell does not keep these, so shell mode means rebuilding them. Companion mode (today) stays the default.

## Behaviour
- **Companion mode** (default, unchanged): WinGnome on top of Explorer.
- **Shell mode** (opt-in, experimental): Settings → General → *Use WinGnome as my shell (experimental)*, with a
  warning listing what changes and the recovery steps. Turning it on is a **trial**: it applies to the next sign-in
  only, and after signing in the user confirms *Keep WinGnome as my shell* (accepted only after at least 5 minutes of
  uptime); otherwise the following sign-in is Explorer again. The bootstrap never re-arms the value itself.
- **Always recoverable:**
  - WinGnome isn't "ready" (desktop, tray and AppBar windows exist) within 90 s of its process start, or crashes
    3 times within 2 minutes or 5 times within 30 minutes → Explorer, and the value is removed; WinGnome explains
    why on its next start. A kill during logoff or shutdown is not a crash and never starts Explorer.
  - `WinGnomeShell.exe --restore` (from Task Manager → Run new task, reachable through Ctrl+Alt+Del, which
    Winlogon owns) → removes the value and starts Explorer. It lives in the bootstrap, not the WPF app, so it works
    even when WinGnome or .NET is broken. The README also documents the one-line
    `reg delete "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /f`, and that typing
    `explorer.exe` in Task Manager opens only a folder window until the value is removed.
  - Safe Mode always starts Explorer. Holding **Shift** while signing in also does, but it's a secondary path
    (Windows Hello and autologon leave no sign-in moment, and Shift can trigger Sticky Keys).
- In shell mode WinGnome provides (by phase, see Roadmap): desktop and wallpaper, top bar, dock, app grid and search
  (Start), the tray (authoritative, no forwarding), the AppBar protocol and work area, startup apps, Win+ hotkeys,
  volume/media keys and OSD, notifications, Alt+Tab, snapping, workspaces, quick settings and a GNOME-style
  Settings app (spec 0015).
- File Explorer stays the file manager: folders open in Explorer windows (`explorer.exe <path>`), which don't make
  Explorer the shell (to verify).

## Non-goals
- Replacing DWM, the lock screen, UAC prompts, Ctrl+Alt+Del or Task Manager (Winlogon/LogonUI own them; they keep
  working).
- Injecting into or patching Explorer (KI-008).
- Running WinGnome elevated (see spec 0014; the shell receives messages from every app and must stay unelevated).

## Design
### Enabling and the bootstrap
- Per-user value `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell` (never HKLM). Recorded before
  it's written (same backup discipline as tweaks), removed by every recovery path.
- Winlogon does **not** fall back if the shell exe is missing or fails to start: the user gets an empty desktop. So
  the value points at **`WinGnomeShell.exe`** at a stable, non-versioned path, and every WinGnome start (in either
  mode) checks the target exists and repairs or removes the value.
- `WinGnomeShell.exe` is a tiny **NativeAOT C#** bootstrap (new project; no WPF, no package references, hand-written
  P/Invoke, ~3–5 MB, no runtime dependency) that links `ShellStartDecision` from Core (Core must stay
  AOT-compatible: no reflection). It reads only tiny independent inputs (flag files, a crash log), never
  `settings.json`. It is **fail-closed**: a top-level catch, a missing `WinGnome.exe` or runtime, or any unreadable
  input → remove the value and start Explorer. It:
  1. Starts Explorer if Safe Mode, a `shell-disabled` flag, Shift, or the crash history says so (`ShellStartDecision`).
  2. In trial mode removes the value as it starts (so a reboot falls back).
  3. Starts `WinGnome.exe --shell-mode`, signals Winlogon's shell-ready events promptly (so LogonUI doesn't linger on
     *Welcome*), waits for WinGnome's ready event (90 s), restarts it with backoff (1 s, 3 s, 10 s) on crash, watches
     for logoff/shutdown (`WM_ENDSESSION`, `WTS_SESSION_LOGOFF`), and on giving up removes the value, resets the work
     area and starts `explorer.exe`.
- New process and new project: justified because a crashed WPF app can't recover the session itself. (AGENTS.md
  "ask first": approved by the user's request for shell mode.)

### What Explorer provides → what WinGnome does

| Explorer provides | Without Explorer | WinGnome plan | Size | Phase |
|---|---|---|---|---|
| Autostart (Run/RunOnce HKCU+HKLM, Startup folders, StartupApproved flags, packaged StartupTasks) | Nothing starts | `StartupRunner`: parsing/ordering in Core; RunOnce deleted before run; skips WinGnome's own entry | M | 1 |
| Shell window (`SetShellWindow`, Progman) | `GetShellWindow()` = 0, breaks unelevated launches and apps looking for Progman | Desktop window at the bottom of the z-order, draws the wallpaper, calls `SetShellWindow` | S | 1 |
| Shell hook / taskman (`RegisterShellHookWindow`, `SetTaskmanWindow`, `HSHELL_*`) | No flash, min-rect, rude-app or APPCOMMAND handling | Register as taskman; handle APPCOMMAND (volume/media keys) via CoreAudio | M | 1 |
| Tray (`Shell_TrayWnd`, `WM_COPYDATA`) | TrayHost forwards to Explorer today; with none, calls fail | TrayHost becomes authoritative: `NIM_*`, `NIM_SETVERSION`, balloons (`NIF_INFO`) as banners, `Shell_NotifyIconGetRect` | M | 1 |
| AppBar protocol (`ABM_*`) and work area | Nobody reserves screen space | Full ABM implementation (NEW/REMOVE/QUERYPOS/SETPOS/GETSTATE/GETTASKBARPOS/autohide/ACTIVATE/WINDOWPOSCHANGED), per-monitor work areas, `ABN_POSCHANGED`/`ABN_FULLSCREENAPP`; negotiation logic in Core | M/L | 1 |
| `TaskbarCreated`, `TaskbarButtonCreated` | Apps never register tray icons or taskbar state | Broadcast at start; send per window | S | 1 |
| Start menu | Not launched | Existing app grid and search; launching via in-process `ShellExecuteEx` on AppsFolder items (spec 0014) | S | 0/1 |
| Win+ hotkeys (E, R, D, X, I, V, ., arrows, Shift+S) | Dead (Win+L, Ctrl+Alt+Del, Ctrl+Shift+Esc still work) | Registered in shell mode: Win+E file window, Win+R `SHRunFileDlg`, Win+I Settings (spec 0015), Win+Shift+S `ms-screenclip:`, Win+D show desktop, Win+arrows snap | M | 1 |
| Alt+Tab, Task View | Gone (possible legacy fallback — verify) | GNOME switcher over `WindowTracker` + DWM thumbnails; Task View = overview | M | 2 |
| Virtual desktops | Live in Explorer; gone | Phase 1 hides workspace dots; later WinGnome workspaces (hide/move windows, crash-restore marker) | L | 3 |
| Snap (keyboard, Layouts, Assist) | Mostly gone (drag-to-edge — verify) | Own snapping/tiling, geometry in Core | M | 2 |
| Toasts, notification centre | ShellExperienceHost not started; toasts vanish (verify) | `UserNotificationListener` mirror → GNOME banners and a list in the calendar popup (needs package identity: signed sparse package) | L | 2 |
| Quick Settings / Action Center | Gone | Extend WinGnome quick settings: Wi-Fi list, Bluetooth, airplane, Do Not Disturb | M | 2 |
| Volume/brightness/media OSD | Gone | GNOME-style OSD; media via `GlobalSystemMediaTransportControlsSessionManager` | M | 2 |
| Desktop: wallpaper, icons | No Progman | WinGnome desktop window draws wallpaper (and slideshows); no icons (GNOME default) | S | 1 |
| File windows | `explorer.exe <path>` should open a window without becoming shell (verify) | Keep Explorer as file manager | S | 1 |
| UWP/packaged activation | Reported broken under Cairo as shell (verify) | `IApplicationActivationManager`; if broken, shell mode is blocked | ? | spike |
| Explorer's tray icons (network, volume, battery, Safely Remove) | Gone | Top-bar indicators already cover three; Safely Remove via `hotplug.dll` | S | 3 |
| Search, Widgets, Copilot | Gone | Overview search; Widgets/Copilot out of scope | — | — |
| Input switcher, emoji, clipboard history | Explorer hotkeys/indicator gone; IME likely fine (verify) | Input indicator in top bar; launch TextInputHost where possible | M | 3 |
| Taskbar progress/overlays (`ITaskbarList3`) | Dropped | `TaskbandHWND` + WM_USER messages → dock progress and badges | M | 2 |
| Jump lists, thumbnail toolbars | Lost | Parse destination files (undocumented); no thumbnail toolbars | L | 3 |
| Bars on every monitor | — | Spec 0010 becomes required | M | 2 |
| Session end, UAC, lock, Ctrl+Alt+Del, Task Manager | Unaffected | Don't block `WM_QUERYENDSESSION` | S | 1 |
| Explorer restarting itself after a crash | — | Bootstrap watchdog | M | 1 |
| `Shell.Application` COM server (`ShellWindows`, `IShellDispatch`: de-elevated launches by installers, "open file location", `MinimizeAll`) | Served by the shell Explorer; breaks silently (verify whether a non-shell `explorer.exe` registers it) | Spike decides: keep a non-shell Explorer process for COM and file windows, or implement the commonly used parts | M | 1 |
| Session, power and display notifications (`WM_WTSSESSION_CHANGE`, `WM_POWERBROADCAST`, `WM_DISPLAYCHANGE`, DPI, `WM_SETTINGCHANGE`) | The shell owns per-monitor work areas | Re-apply work areas and AppBars on resume, display and DPI changes | S | 1 |
| AutoPlay (`QueryCancelAutoPlay`, the prompt) | Lost | Documented limitation; later a GNOME-style "removable media" banner | M | 3 |
| Accessibility hotkeys (Win+U, Win+Ctrl+Enter Narrator, Win+Plus Magnifier), touch keyboard auto-invoke | Unverified | Spike; anything lost is re-bound in phase 1 — stranding screen-reader users is a blocker | S | 1 |

References: Cairo Desktop's ManagedShell (Apache-2.0) is the best reference for the tray, AppBar, tasks and shell
hook; port ideas with attribution where code is adapted, no dependency.

## Roadmap
- **Phase 0 (companion mode, now):** in-process launching and *Run as administrator* (spec 0014); crisp top bar
  (spec 0012); GNOME Settings app MVP (spec 0015); Core models built and tested but unwired: `StartupRunner`,
  AppBar negotiation, Win+ hotkey map; **verification spike** (below).
- **Phase 1 (opt-in experimental shell mode):** bootstrap and watchdog, trial mode, `WinGnomeShell.exe --restore`, desktop
  window, taskman/shell hook, authoritative tray, AppBar server and work area, startup apps, core Win+ hotkeys,
  volume keys, Run dialog, workspace dots hidden, Settings "Shell" switch with warning.
- **Phase 2:** notifications, Alt+Tab, OSD and media, quick settings Wi-Fi/Bluetooth, snapping, taskbar progress,
  bars on every monitor.
- **Phase 3:** workspaces, jump lists, input indicator, optional desktop icons.

## Verification spike (before phase 1 is agreed)
On a **separate local standard account** (Hyper-V and Windows Sandbox aren't available on Home), with a minimal
test shell, record for Windows 11 25H2:
1. Is the HKCU Winlogon `Shell` value honoured (vs. `IniFileMapping` `SYS:`)?
2. Does `explorer.exe <folder>` open a plain window without becoming the shell? And `explorer.exe shell:AppsFolder\…`?
3. Do Settings, Calculator and Photos activate (`IApplicationActivationManager`)?
4. Are toasts shown? Does `UserNotificationListener` work with a sparse package?
5. Do volume/media keys reach the taskman window? Does any Alt+Tab appear?
6. Does `IDesktopWallpaper` work? Does drag-to-edge snapping still work?
7. Does Defender flag the HKCU Shell value?
8. Does Winlogon need `ShellReadyEvent`/`ShellDesktopSwitchEvent` from a custom shell?
9. Does a non-shell `explorer.exe` register `Shell.Application`, and can it be kept running for COM and file windows?
10. Narrator, Magnifier, Win+U and the touch keyboard: which survive?
11. Is `Policies\System\Shell` (if present) taking precedence over the Winlogon value?
Each answer updates the table above; a "no" on 1, 3 or 10 blocks shell mode.
Then **scripted chaos QA** on the test account, where every case must land on Explorer: missing exe, corrupt
settings, kill mid-start, power cut, update mid-trial, slow crash loop, logoff during start.

## Safety and recovery
- Recorded before changed: the Shell value (absent/previous) goes into the backup file before it's written.
- Restored on: trial expiry, crash loop, missing exe, Shift at sign-in, `WinGnomeShell.exe --restore`, turning the setting off, and
  uninstall instructions in the README. The bootstrap also resets the work area.
- Design reviewed by Fable 5.1 on 2026-10-09 (bootstrap fail-closed, restore in the bootstrap, crash policy,
  missing inventory rows); its recommendations are folded in above.
- `--safe` never writes the Shell value and never runs as shell; `--selftest` covers the bootstrap's decision logic
  through Core tests (`ShellStartDecision`: inputs = shift, safe mode, flag, crash history, trial state).
- Documented last resort: another admin account, or editing the user's hive offline from WinRE.

## Footprint
Shell mode replaces Explorer's ~100–150 MB with WinGnome's own surfaces; the bootstrap is a few MB and idle
(waits on a process handle). Every new service follows the existing idle rules: events not polling, lazily
created surfaces.

## Acceptance criteria (phase 1)
1. Core tests for `ShellStartDecision` (fail-closed inputs, crash windows, session end, ready timeout, trial
   uptime, restore), `StartupRunner` ordering and RunOnce handling, AppBar negotiation.
2. On a test account: sign in with shell mode on → WinGnome shell; startup apps start; tray icons appear; another
   app's AppBar reserves space; Win+E/R/I work; volume keys work.
3. Kill WinGnome 3 times → Explorer starts and the value is removed. Delete `WinGnome.exe` → Explorer. `--restore`
   from Task Manager → Explorer at next sign-in and immediately.
4. Trial: reboot without confirming → Explorer.

## Risks and open questions
- A failed start could leave a black screen: the bootstrap and recovery paths must ship before the switch does.
- UWP activation or the HKCU value may not work on 25H2 — the spike decides.
- Lost notifications until phase 2; virtual desktops, Snap Layouts, Win+V and Win+. vanish.
- Security software may flag the Winlogon value (MITRE T1547.004).
- The tray thread becomes critical for every app in the session; a hang there stalls `Shell_NotifyIcon` callers.
- Effort: phases 1–2 are roughly the size of everything built so far.
