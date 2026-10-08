# Known issues

The single list of known bugs, risks and limitations in WinGnome. See [AGENTS.md](../AGENTS.md#6-known-issues)
for when to add, update and close entries.

## Severity

| Severity | Meaning | Examples |
|---|---|---|
| **S1 Critical** | Leaves the user's system broken or loses data, or WinGnome can't run. Fix before anything else. | Taskbar stays hidden after exit; registry change not reverted; Explorer crashes; crash on start. |
| **S2 High** | A main feature is broken or unsafe for many users, with no reasonable workaround. | Dock doesn't show running apps; hook swallows keystrokes; window buttons close the wrong window. |
| **S3 Medium** | A feature is broken or degraded in some setups, or a workaround exists. | Wrong layout at 150% DPI; a setting doesn't apply until restart; secondary monitors unsupported. |
| **S4 Low** | Cosmetic, rare, or a documented limitation. | One-pixel misalignment; tooltip wording; behaviour Windows doesn't allow us to change. |

Status is one of *Open*, *In progress*, *By design* (a limitation we've chosen to accept, with the reason),
or *Fixed* (with the commit). When in doubt, pick the higher severity.

## Open

| ID | Severity | Area | Summary | Status |
|---|---|---|---|---|
| [KI-001](#ki-001) | S3 | Dock, Top bar | Dock and top bar appear on the primary monitor only | Open |
| [KI-002](#ki-002) | S4 | App | The executable has no app icon yet | Open |
| [KI-003](#ki-003) | S4 | Workspaces | Desktop switching relies on simulated Ctrl+Win+arrow keys | By design |
| [KI-004](#ki-004) | S4 | Overview | Super key opens Start when an elevated window has focus | By design |
| [KI-005](#ki-005) | S4 | Tray | Some tray icons only appear in the Windows tray | By design |
| [KI-006](#ki-006) | S4 | Tray | No tray icons when running elevated or alongside another tray host | By design |
| [KI-007](#ki-007) | S4 | Window buttons | Some apps that draw their own title bars keep their own buttons | Partly fixed |
| [KI-008](#ki-008) | S4 | Taskbar | The native taskbar isn't restyled | By design |
| [KI-009](#ki-009) | S4 | Repo | No CI workflow, although the README says `--selftest` is used by CI | Open |
| [KI-010](#ki-010) | S3 | App | Launched from a sandboxed terminal, WinGnome can't save settings or its restore marker | Open |
| [KI-014](#ki-014) | S4 | Tweaks | GNOME look tweaks are verified by tests only, not yet on a live Windows install | Open |
| [KI-015](#ki-015) | S3 | Window buttons | Two WinGnome instances that both decorate windows fight over title-bar colours | Open |
| [KI-016](#ki-016) | S4 | Window buttons | The patch behind the circles is a flat colour | Open |
| [KI-018](#ki-018) | S4 | Performance | Idle CPU of ~0.6–1 s per minute is unexplained | Open |
| [KI-019](#ki-019) | S4 | Tray | The tray host can take up to 1 s to get back in front of Explorer's taskbar | Open |
| [KI-021](#ki-021) | S4 | Top bar | Brightness slider controls only a laptop's built-in display | Open |

### KI-001
**Dock and top bar appear on the primary monitor only** · S3 · Dock, Top bar · Open

On multi-monitor setups the dock and top bar exist only on the primary monitor. Secondary monitors have
no top bar or dock.
*Workaround:* none; use the Windows taskbar mode if per-monitor taskbars are needed.
*Fix direction:* per-monitor top bar and dock instances with per-monitor window lists (roadmap item 1).

### KI-002
**The executable has no app icon yet** · S4 · App · Open

`WinGnome.exe` has no `ApplicationIcon`, so Explorer, Task Manager and Alt+Tab show the default icon.
The logo exists as [assets/logo/wingnome.svg](../assets/logo/wingnome.svg).
*Fix direction:* export a multi-size `.ico` (16, 20, 24, 32, 40, 48, 64, 256) from the SVG, add it as
`ApplicationIcon` in `src/WinGnome/WinGnome.csproj`, and use it for the settings window.

### KI-003
**Desktop switching relies on simulated Ctrl+Win+arrow keys** · S4 · Workspaces · By design

Windows has no public API for switching virtual desktops, so the workspace dots send Ctrl+Win+←/→ to
step between desktops. *Reason:* the alternatives are undocumented COM interfaces that change between Windows builds.

### KI-004
**Super key opens Start when an elevated window has focus** · S4 · Overview · By design

With *Super key opens the overview* on, Windows doesn't let a non-elevated low-level keyboard hook see keys
sent to elevated windows, so Start opens instead. *Reason:* WinGnome deliberately runs without elevation.

### KI-005
**Some tray icons only appear in the Windows tray** · S4 · Tray · By design

Top-bar tray icons come from apps re-registering after the *TaskbarCreated* broadcast. An app whose icon
belongs to a message-only window, or that doesn't re-register, only shows in the Windows tray. Windows 11's
own network, volume and battery icons aren't tray icons; the top bar's indicators replace them.
Balloon notifications are left to Windows.

### KI-006
**No tray icons when running elevated or alongside another tray host** · S4 · Tray · By design

WinGnome hosts tray icons only when it runs without administrator rights, and only one instance hosts them
at a time (a second copy takes over when the first exits). If another tray host such as RetroBar is running,
WinGnome doesn't compete with it.

### KI-007
**Some apps that draw their own title bars keep their own buttons** · S4 · Window buttons · Partly fixed

Chrome, Edge, Electron apps, Windows Terminal, WinUI 3 and UWP apps draw their own caption buttons and don't
report them to DWM (`DWMWA_CAPTION_BUTTON_BOUNDS` is empty or stale). Since 652c6cc the opt-in setting
*Decorate apps with custom title bars (experimental)* asks such windows what's under the top-right corner
(`WM_NCHITTEST`, off the UI thread, with timeouts) and decorates them when they report three adjacent close,
maximise and minimise zones: this works for apps that support Snap Layouts, such as Claude desktop
(spec 0005). Apps that report plain client area over their buttons (GitHub Desktop, Dia, Windows Terminal)
keep their own buttons; UI Automation was tried and is too slow and unreliable (3 s on Dia without a result).
Known limits of the experimental mode: while such a window is resized live it's re-probed repeatedly and its
circles stay hidden until the size settles, and an app that reports zones but draws its buttons elsewhere
would be mis-decorated (mitigated by strict order, size and edge checks).

### KI-008
**The native taskbar isn't restyled** · S4 · Taskbar · By design

In native taskbar mode WinGnome doesn't make the Windows taskbar rounded, floating or translucent.
*Reason:* that needs code injected into Explorer, which breaks with Windows updates. Tools such as
Windhawk's *Taskbar Styler* can run alongside WinGnome in native taskbar mode.

### KI-009
**No CI workflow, although the README says `--selftest` is used by CI** · S4 · Repo · Open

There's no `.github/workflows` yet, so nothing builds or tests on push.
*Fix direction:* a Windows workflow that runs `dotnet build -c Release -warnaserror`, `dotnet test` and
the self-test, triggered on push to `main` and on pull requests only.

### KI-010
**Launched from a sandboxed terminal, WinGnome can't save settings or its restore marker** · S3 · App · Open

A WinGnome started from a sandboxed shell (for example an AI coding agent's terminal) may be blocked from
reading or writing `%APPDATA%\WinGnome`. It then runs on default settings, keeps no log, can't delete its
taskbar marker on exit, and loses every settings change. On 2026-10-08 this hid the user's pinned apps and
left the taskbar on auto-hide after quitting. Since 6e07eb0 the Settings window shows a *Changes won't be kept*
banner when the folder isn't writable. *Workaround:* start WinGnome from Explorer, the Start menu or sign-in;
`WinGnome.exe --restore-taskbar` fixes a taskbar left on auto-hide.
*Fix direction:* also warn from the top bar (not only Settings), and refuse to hide the taskbar when the
settings folder can't be written (the marker check covers new markers, not a stale one it can't delete).

### KI-014
**GNOME look tweaks are verified by tests only, not yet on a live Windows install** · S4 · Tweaks · Open

The *Adwaita blue accent*, *Neutral window chrome*, *Hide desktop icons* and *Hide "Learn about this picture"*
tweaks were written and tested against an in-memory registry. Still to confirm on a real machine:
- Windows builds its accent palette with a private algorithm; WinGnome's tints and shades approximate it, and
  Windows may normalise the values, so the tweak can afterwards read as not applied.
- Whether Start, the taskbar and title bars recolour from the broadcast alone or need an Explorer restart.
- The Spotlight icon's CLSID (`{2cc5ca98-6485-489a-920e-b3e88a6ccce3}`) is community-documented, not by Microsoft.
- *Revert all* restores the accent from before WinGnome, discarding a newer accent picked in Windows Settings
  (the engine does this for every tweak).

### KI-015
**Two WinGnome instances that both decorate windows fight over title-bar colours** · S3 · Window buttons · Open

With *Unify title bar colour* on in both, each records and restores the colours it changed, and the first to
quit resets them under the other. Only matters with a second profile (`--settings-dir`) running alongside the
everyday one, e.g. during QA: turn window buttons off in test profiles.

### KI-016
**The patch behind the circles is a flat colour** · S4 · Window buttons · Open

The patch is one sampled colour. It matches solid and Mica title bars after the Mica fade settles (re-sampled
~450 ms after activation), but doesn't reproduce a gradient, and can go stale when a Mica window is moved by
code rather than dragged. Snapped windows keep a 1 px border inset, so the corner pixel shows the app's frame.
*Fix direction:* sample a strip just left of the buttons and stretch it, re-sampling on move/size end.

### KI-018
**Idle CPU of ~0.6–1 s per minute is unexplained** · S4 · Performance · Open

Measured on 2026-10-08 with window buttons off, in safe mode, with other test instances running (so noisy).
*Fix direction:* profile on a quiet machine (ETW / PerfView CPU sampling) and find the remaining wake-ups.

### KI-019
**The tray host can take up to 1 s to get back in front of Explorer's taskbar** · S4 · Tray · Open

The front check runs every 250 ms for 2 s after activity and every 1 s at rest (it was 250 ms always).
If Explorer raises its taskbar with no event announcing it, a tray-icon call in that gap reaches Explorer
only, and the icon appears in the top bar when the app next updates it. A posted `WM_CLOSE` to "the taskbar"
also reaches WinGnome's host first and now quits WinGnome (see KI-020).

### KI-021
**Brightness slider controls only a laptop's built-in display** · S4 · Top bar · Open

Brightness goes through WMI (`WmiMonitorBrightness`), which only covers internal panels; the row is hidden on
desktops. External monitors would need DDC/CI (`dxva2` `GetMonitorBrightness`/`SetMonitorBrightness`).
Also: after any WMI failure (for example while Windows has dimmed the display) the row hides until the card
is next opened, and on the development laptop `WmiSetBrightness` failed on every instance object except the
first, so writes go through `SWbemServices.ExecMethod` (an undocumented quirk; late-bound COM via `dynamic`).

## Resolved

| ID | Severity | Area | Summary | Fixed in |
|---|---|---|---|---|
| KI-011 | S2 | Settings | Non-safe runs with `--settings-dir` rewrote or deleted the shared "Start with Windows" entry | 6aeef5f |
| KI-012 | S3 | Settings | A settings folder that couldn't be written silently dropped every change | 6e07eb0 (warning banner) |
| KI-013 | S4 | Top bar | Large hover corner radius drew oval highlights instead of pills | c26e1c8 |
| KI-017 | S3 | Window buttons | The patch behind the circles didn't match Mica title bars and hid the window border | 45ae75a |
| KI-020 | S3 | App | A graceful `taskkill` that reached the tray host window was ignored, so WinGnome didn't quit | 085fa14 |
