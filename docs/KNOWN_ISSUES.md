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
| [KI-003](#ki-003) | S4 | Workspaces | Desktop switching relies on simulated Ctrl+Win+arrow keys | By design |
| [KI-004](#ki-004) | S4 | Overview | Super key opens Start when an elevated window has focus | By design |
| [KI-005](#ki-005) | S4 | Tray | Some tray icons only appear in the Windows tray | By design |
| [KI-006](#ki-006) | S4 | Tray | No tray icons when running elevated or alongside another tray host | By design |
| [KI-007](#ki-007) | S4 | Window buttons | Some apps that draw their own title bars keep their own buttons | Partly fixed |
| [KI-008](#ki-008) | S4 | Taskbar | The native taskbar isn't restyled | By design |
| [KI-009](#ki-009) | S4 | Repo | No CI workflow, although the README says `--selftest` is used by CI | Open |
| [KI-010](#ki-010) | S3 | App | Launched from a sandboxed terminal, WinGnome uses a private copy of its settings folder | Open |
| [KI-014](#ki-014) | S4 | Tweaks | GNOME look tweaks are verified by tests only, not yet on a live Windows install | Open |
| [KI-015](#ki-015) | S3 | Window buttons | Two WinGnome instances that both decorate windows fight over title-bar colours | Open |
| [KI-016](#ki-016) | S4 | Window buttons | The patch behind the circles is a flat colour | Open |
| [KI-018](#ki-018) | S4 | Performance | Idle CPU needs profiling on a quiet machine | Open |
| [KI-019](#ki-019) | S4 | Tray | The tray host can take up to 1 s to get back in front of Explorer's taskbar | Open |
| [KI-021](#ki-021) | S4 | Top bar | Brightness slider controls only a laptop's built-in display | Open |
| [KI-022](#ki-022) | S4 | Tray | A `WM_CLOSE` posted to "the taskbar" quits WinGnome while it hosts tray icons | Open |
| [KI-023](#ki-023) | S4 | Dock | Two pins with the same target group the app's windows into the first one | Open |
| [KI-050](#ki-050) | S4 | Settings | Another WinGnome instance draws its circles over this one's header bars | Open |
| [KI-051](#ki-051) | S3 | Accessibility | The round window buttons aren't exposed to screen readers or keyboard | Open |
| [KI-030](#ki-030) | S4 | Overview | The first overview open after start takes about half a second | Open |
| [KI-031](#ki-031) | S4 | Overview | Overview animation details not verified on every path | Open |
| [KI-032](#ki-032) | S4 | Overview | The overview's close glide gets 8–13 frames | Open |
| [KI-033](#ki-033) | S4 | Overview | A dock request for some windows doesn't narrow an open overview | Open |

### KI-001
**Dock and top bar appear on the primary monitor only** · S3 · Dock, Top bar · Open

On multi-monitor setups the dock and top bar exist only on the primary monitor. Secondary monitors have
no top bar or dock.
*Workaround:* none; use the Windows taskbar mode if per-monitor taskbars are needed.
*Fix direction:* per-monitor top bar and dock instances with per-monitor window lists (roadmap item 1).

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
**Launched from a sandboxed terminal, WinGnome uses a private copy of its settings folder** · S3 · App · Open

An AI coding agent's sandboxed shell can give the processes it starts a private, copy-on-write view of
`%APPDATA%\WinGnome`. A WinGnome started there reads and saves settings, logs and its taskbar marker in that
private copy; the real folder never sees them, and the sandbox keeps seeing its stale copy afterwards. On
2026-10-08 the previous session's WinGnome stored the user's setup (8 pinned apps, top bar) only in the
sandbox's copy, so the first normal launch at 21:11 started from defaults and the user had to set everything
up again. The sandboxed shell then misread the real log as silent and the real settings as older than they were.
Writes succeed inside the sandbox, so the *Changes won't be kept* banner (6e07eb0) does not catch this case.
*Workaround:* start the everyday WinGnome from Explorer, the Start menu or sign-in. Agents must launch it with
`explorer.exe <path>\WinGnome.exe` and read its real files through an Explorer-launched process (AGENTS.md).
*Fix direction:* none in WinGnome itself; keep test runs on `--safe` with their own `--settings-dir`.

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
**Idle CPU needs profiling on a quiet machine** · S4 · Performance · Open

On 2026-10-08 the footprint work measured 0.6–1 s of CPU per idle minute, but with other test instances
running. The final build, measured alone with every feature on (user's settings, safe mode, one decorated
window), used 0.24 s per idle minute, ~82 MB private memory and 15 threads once settled.
*Fix direction:* ETW / PerfView CPU sampling over a long idle period to find the remaining wake-ups.

### KI-019
**The tray host can take up to 1 s to get back in front of Explorer's taskbar** · S4 · Tray · Open

The front check runs every 250 ms for 2 s after activity and every 1 s at rest (it was 250 ms always).
If Explorer raises its taskbar with no event announcing it, a tray-icon call in that gap reaches Explorer
only, and the icon appears in the top bar when the app next updates it.

### KI-021
**Brightness slider controls only a laptop's built-in display** · S4 · Top bar · Open

Brightness goes through WMI (`WmiMonitorBrightness`), which only covers internal panels; the row is hidden on
desktops. External monitors would need DDC/CI (`dxva2` `GetMonitorBrightness`/`SetMonitorBrightness`).
Also: after any WMI failure (for example while Windows has dimmed the display) the row hides until the card
is next opened, and on the development laptop `WmiSetBrightness` failed on every instance object except the
first, so writes go through `SWbemServices.ExecMethod` (an undocumented quirk; late-bound COM via `dynamic`).

### KI-022
**A `WM_CLOSE` posted to "the taskbar" quits WinGnome while it hosts tray icons** · S4 · Tray · Open

While WinGnome hosts the tray, its hidden host window is the first `Shell_TrayWnd` that `FindWindow` returns.
Since 085fa14 a posted `WM_CLOSE` to it is treated as a quit request (that fixed KI-020, where a graceful
`taskkill` was ignored). A tool or script that posts `WM_CLOSE` to the taskbar to open Explorer's *Shut Down
Windows* dialog therefore quits WinGnome instead. *Fix direction:* if taskkill is confirmed to post to every
top-level window of the process, forward the message to Explorer's real taskbar again and rely on the
UI-thread windows for quitting.

### KI-023
**Two pins with the same target group the app's windows into the first one** · S4 · Dock · Open

Since spec 0006 a window joins a pin whose shortcut target is the same install as the window's process
(`AppPathMatch.IsSameInstall`), as well as a pin with the same identity. When two pins resolve to the same target,
for example a named-AUMID pin and a path pin for the same executable, or two Squirrel apps pinned through stubs in
the same folder, all of its windows go to the first pin and the second shows as not running. An exact identity
match still wins over a path match. Also, before the app catalogue has loaded, named-AUMID pins can't resolve
their target, so their windows show unpinned until the catalogue's `Changed` refresh.

### KI-050
**Another WinGnome instance draws its circles over this one's header bars** · S4 · Settings · Open

The settings window and app picker hide the Windows caption with `WindowChrome` but keep `WS_CAPTION`, the
system menu and the maximise box (needed for Snap Layouts), so DWM still reports caption-button bounds. Each
instance skips only its own process, so a second WinGnome with window buttons on (a QA profile next to the
everyday one) overlays its circles on top of the header bar's own ones. One instance alone is unaffected. As
with KI-015, turn window buttons off in test profiles; capture header bars with `PrintWindow` to see only
the window's own drawing.

### KI-051
**The round window buttons aren't exposed to screen readers or keyboard** · S3 · Accessibility · Open

`TrafficLightButtonsView` draws all three circles in one element with no `AutomationPeer`, so Narrator and UI
Automation see no Close, Minimise or Maximise buttons in the settings header bar or the overlay, and the circles
can't be focused. The settings window still closes with Alt+F4 and its system menu (Alt+Space) has Minimise,
Maximise and Close. *Fix direction:* an `AutomationPeer` for the view with one child peer per circle
(`IInvokeProvider`), named from `CaptionButtonKind`.

### KI-030
**The first overview open after start takes about half a second** · S4 · Overview · Open

The first time the overview opens, WPF's first full-screen software-rendered frame takes ~430 ms (JIT, glyph and
layout caches). Since spec 0008 the window stays cloaked until that frame is presented, so nothing appears for
~470 ms instead of a bare backdrop flashing up; later opens appear 60–120 ms after the key. The 250 ms reveal
timeout can't help while the UI thread is busy rendering. *Fix direction:* render the overview once, cloaked
and inactive, shortly after start-up, then hide and shrink it again.

### KI-031
**Overview animation details not verified on every path** · S4 · Overview · Open

Spec 0008 was checked live for opening, closing by hotkey, Esc and click, reversing mid-open and mid-close, and
typing during the glide. Not yet exercised live: Windows animations turned off (`SPI_GETCLIENTAREAANIMATION`),
a window closing during a glide, the hot corner, and DPI other than the dev machine's. Closing from search
results or the app grid is instant by design (only the window grid glides back).

### KI-032
**The overview's close glide gets 8–13 frames** · S4 · Overview · Open

Spec 0008 said to move the dim layer into its own small layered window if a glide got fewer than ~12 frames.
Opening (250 ms) measured 12–15 frames; closing (200 ms) 8–13 at the same 40–60 fps, so the per-frame cost is
the same and the dim stays in the overview's brush alpha (every frame re-renders the full-screen WPF surface in
software). *Fix direction:* if closes look choppy on slower machines, move the dim to a layered window or
lengthen the close.

### KI-033
**A dock request for some windows doesn't narrow an open overview** · S4 · Overview · Open

Not from spec 0008. With the all-windows grid open, a request for a subset of windows (dock previews,
`OverviewRequest.OnlyWindows`) only re-shows the existing grid (`ShowModeContent` returns early when thumbnails
are shown), so every window stays visible until the next window-list change; the reverse switch has the same
problem. *Fix direction:* rebuild the thumbnails in `ShowModeContent` when the requested window set differs.

## Resolved

| ID | Severity | Area | Summary | Fixed in |
|---|---|---|---|---|
| KI-011 | S2 | Settings | Non-safe runs with `--settings-dir` rewrote or deleted the shared "Start with Windows" entry | 6aeef5f |
| KI-012 | S3 | Settings | A settings folder that couldn't be written silently dropped every change | 6e07eb0 (warning banner) |
| KI-013 | S4 | Top bar | Large hover corner radius drew oval highlights instead of pills | c26e1c8 |
| KI-017 | S3 | Window buttons | The patch behind the circles didn't match Mica title bars and hid the window border | 45ae75a |
| KI-002 | S4 | App | The executable had no app icon | 2f7c44b |
| KI-020 | S3 | App | A graceful `taskkill` that reached the tray host window was ignored, so WinGnome didn't quit | 085fa14 |
