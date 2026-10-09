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
| [KI-010](#ki-010) | S3 | App | Launched from a sandboxed terminal, WinGnome uses a private copy of its settings folder | By design |
| [KI-014](#ki-014) | S4 | Tweaks | GNOME look tweaks are verified by tests only, not yet on a live Windows install | Open |
| [KI-016](#ki-016) | S4 | Window buttons | The patch behind the circles is a flat colour | Open |
| [KI-018](#ki-018) | S4 | Performance | Idle CPU needs profiling on a quiet machine | Open |
| [KI-019](#ki-019) | S4 | Tray | The tray host can take up to 1 s to get back in front of Explorer's taskbar | By design |
| [KI-021](#ki-021) | S4 | Top bar | Brightness slider controls only a laptop's built-in display | Open |
| [KI-022](#ki-022) | S4 | Tray | A `WM_CLOSE` posted to "the taskbar" quits WinGnome while it hosts tray icons | Open |
| [KI-023](#ki-023) | S4 | Dock | Two pins with the same target group the app's windows into the first one | Open |
| [KI-031](#ki-031) | S4 | Overview | Overview animation details not verified on every path | Open |
| [KI-032](#ki-032) | S4 | Overview | The overview's close glide gets 8–13 frames | Open |
| [KI-033](#ki-033) | S4 | Overview | A dock request for some windows doesn't narrow an open overview | Open |
| [KI-040](#ki-040) | S4 | Window buttons | A custom title bar resized often keeps its circles hidden for up to a minute | By design |
| [KI-041](#ki-041) | S4 | Window buttons | Clicks on custom title bars wait for one hit test, and clicks meanwhile are ignored | By design |
| [KI-042](#ki-042) | S4 | Window buttons | WinGnome builds from before the window-buttons role still decorate alongside newer ones | Open |
| [KI-050](#ki-050) | S4 | Settings | Another WinGnome instance draws its circles over this one's header bars | Open |
| [KI-052](#ki-052) | S4 | Dock | Explorer windows other than folder windows don't join the File Explorer pin | Open |

### KI-001
**Dock and top bar appear on the primary monitor only** · S3 · Dock, Top bar · Open

On multi-monitor setups the dock and top bar exist only on the primary monitor. Secondary monitors have
no top bar or dock.
*Workaround:* none; use the Windows taskbar mode if per-monitor taskbars are needed.
*Fix direction:* planned in spec [0010](specs/0010-per-monitor-top-bar-and-dock.md) (draft, needs decisions).

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
(spec 0005). Spec 0009 (branch, 2026-10-09) adds VS Code and Docker Desktop (short gaps between zones, every
`Chrome_WidgetWin_*` class probed), asks the deepest child window under each point, and, behind a second
setting, recognises GitHub Desktop's HTML buttons from a built-in profile of their width. Clicks on probed
windows are checked against a fresh hit test first, so a stale or wrong decoration drops the click instead of
sending a command. Still undecorated:
- **Dia** answers `HTMAXBUTTON` only over its maximise button (from its Windows App SDK non-client child) and
  `HTCLIENT` over close and minimise. Since the maximise-anchored rule (spec 0009 addendum) it's decorated
  with *Also decorate apps with web-drawn buttons* on, but only verified by Core tests from measured numbers:
  Dia stayed minimised afterwards. The rule infers the minimise zone's left edge (it's client area like what
  lies left of it), so the circles may sit a few pixels off if Dia's minimise button isn't as wide as maximise.
  Its gate (a visible `ReunionWindowingCaptionControls` child, which is 0 px wide on Dia) admits any Windows App
  SDK window that reports only `HTMAXBUTTON`, not just Dia; the click guard's maximise re-check is what protects
  close and minimise clicks there.
- Windows Terminal and other apps that report plain client area, unless they get a web-button profile.
- Profiled apps whose layout changes in a new release (the hole check then fails, and they keep their buttons).

UI Automation was tried and is too slow and unreliable (3 s on Dia without a result). While a probed window is
resized live its circles stay hidden until the size has held still for 300 ms; see also KI-040.

### KI-008
**The native taskbar isn't restyled** · S4 · Taskbar · By design

In native taskbar mode WinGnome doesn't make the Windows taskbar rounded, floating or translucent.
*Reason:* that needs code injected into Explorer, which breaks with Windows updates. Tools such as
Windhawk's *Taskbar Styler* can run alongside WinGnome in native taskbar mode.

### KI-010
**Launched from a sandboxed terminal, WinGnome uses a private copy of its settings folder** · S3 · App · By design

An AI coding agent's sandboxed shell can give the processes it starts a private, copy-on-write view of
`%APPDATA%\WinGnome`. A WinGnome started there reads and saves settings, logs and its taskbar marker in that
private copy; the real folder never sees them, and the sandbox keeps seeing its stale copy afterwards. On
2026-10-08 the previous session's WinGnome stored the user's setup (8 pinned apps, top bar) only in the
sandbox's copy, so the first normal launch at 21:11 started from defaults and the user had to set everything
up again. The sandboxed shell then misread the real log as silent and the real settings as older than they were.
Writes succeed inside the sandbox, so the *Changes won't be kept* banner (6e07eb0) does not catch this case.
*Workaround:* start the everyday WinGnome from Explorer, the Start menu or sign-in. Agents must launch it with
`explorer.exe <path>\WinGnome.exe` and read its real files through an Explorer-launched process (AGENTS.md).
*Reason:* the sandbox, not WinGnome, decides what the process sees, and its writes succeed, so WinGnome can't
detect it. Keep test runs on `--safe` with their own `--settings-dir` (triaged 2026-10-09).

### KI-014
**GNOME look tweaks are verified by tests only, not yet on a live Windows install** · S4 · Tweaks · Open

*Next step:* a manual pass on the user's machine (it writes real HKCU values, so only with the user's go-ahead):
apply each tweak, check Start, taskbar and title bars with and without an Explorer restart, then *Revert all* and
compare HKCU with `tweaks-backup.json`.

The *Adwaita blue accent*, *Neutral window chrome*, *Hide desktop icons* and *Hide "Learn about this picture"*
tweaks were written and tested against an in-memory registry. Still to confirm on a real machine:
- Windows builds its accent palette with a private algorithm; WinGnome's tints and shades approximate it, and
  Windows may normalise the values, so the tweak can afterwards read as not applied.
- Whether Start, the taskbar and title bars recolour from the broadcast alone or need an Explorer restart.
- The Spotlight icon's CLSID (`{2cc5ca98-6485-489a-920e-b3e88a6ccce3}`) is community-documented, not by Microsoft.
- *Revert all* restores the accent from before WinGnome, discarding a newer accent picked in Windows Settings
  (the engine does this for every tweak).

### KI-016
**The patch behind the circles is a flat colour** · S4 · Window buttons · Open

The patch is one sampled colour. It matches solid and Mica title bars after the Mica fade settles (re-sampled
~450 ms after activation, after a drag ends, and since 6e5c21f 450 ms after a move or resize by code settles,
which costs one screen `GetPixel` per settled move), but doesn't reproduce a gradient. Snapped windows keep a 1 px border inset, so the corner pixel shows the app's
frame.
*Fix direction:* sample a strip just left of the buttons and stretch it.

### KI-018
**Idle CPU needs profiling on a quiet machine** · S4 · Performance · Open

*Next step:* a WPR CPU and context-switch trace (`wpr -start CPU`) over 10 idle minutes with only the everyday
instance running, ranking WinGnome's thread wake-ups in WPA. Note the overview warm-up (1e4522f) adds ~30 MB
private memory from start instead of from the first open.

On 2026-10-08 the footprint work measured 0.6–1 s of CPU per idle minute, but with other test instances
running. The final build, measured alone with every feature on (user's settings, safe mode, one decorated
window), used 0.24 s per idle minute, ~82 MB private memory and 15 threads once settled.
*Fix direction:* ETW / PerfView CPU sampling over a long idle period to find the remaining wake-ups.

### KI-019
**The tray host can take up to 1 s to get back in front of Explorer's taskbar** · S4 · Tray · By design

The front check runs every 250 ms for 2 s after activity and every 1 s at rest (it was 250 ms always).
If Explorer raises its taskbar with no event announcing it, a tray-icon call in that gap reaches Explorer
only, and the icon appears in the top bar when the app next updates it.
*Reason:* no event announces Explorer raising its taskbar, and checking more often costs idle CPU all day (the
trade-off accepted in spec 0004). Triaged 2026-10-09.

### KI-021
**Brightness slider controls only a laptop's built-in display** · S4 · Top bar · Open

Brightness goes through WMI (`WmiMonitorBrightness`), which only covers internal panels; the row is hidden on
desktops. External monitors would need DDC/CI (`dxva2` `GetMonitorBrightness`/`SetMonitorBrightness`).
Also: after any WMI failure (for example while Windows has dimmed the display) the row hides until the card
is next opened, and on the development laptop `WmiSetBrightness` failed on every instance object except the
first, so writes go through `SWbemServices.ExecMethod` (an undocumented quirk; late-bound COM via `dynamic`).
*Fix direction:* planned in spec [0011](specs/0011-external-monitor-brightness.md) (draft, needs a product decision).

### KI-022
**A `WM_CLOSE` posted to "the taskbar" quits WinGnome while it hosts tray icons** · S4 · Tray · Open

While WinGnome hosts the tray, its hidden host window is the first `Shell_TrayWnd` that `FindWindow` returns.
Since 085fa14 a posted `WM_CLOSE` to it is treated as a quit request (that fixed KI-020, where a graceful
`taskkill` was ignored). A tool or script that posts `WM_CLOSE` to the taskbar to open Explorer's *Shut Down
Windows* dialog therefore quits WinGnome instead. *Triage (2026-10-09):* history suggests taskkill posts
to one window only: the UI-thread `WM_CLOSE` filter (b4bd338) predates 085fa14 and KI-020 was still seen in
between. Forwarding the message to Explorer would then most likely bring KI-020 back, so nothing was changed.
*To verify:* quit the everyday instance, start a `--safe` profile through `explorer.exe`, `taskkill /PID` it, and
check whether the log has both the tray-host and the `ControlWindow` close lines. If both appear, forward the
message to Explorer and rely on `ControlWindow`; if only the tray host's, mark this By design.

### KI-023
**Two pins with the same target group the app's windows into the first one** · S4 · Dock · Open

Since spec 0006 a window joins a pin whose shortcut target is the same install as the window's process
(`AppPathMatch.IsSameInstall`), as well as a pin with the same identity. When two pins resolve to the same target,
for example a named-AUMID pin and a path pin for the same executable, or two Squirrel apps pinned through stubs in
the same folder, all of its windows go to the first pin and the second shows as not running. An exact identity
match still wins over a path match. Also, before the app catalogue has loaded, named-AUMID pins can't resolve
their target, so their windows show unpinned until the catalogue's `Changed` refresh.

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

### KI-040
**A custom title bar resized often keeps its circles hidden for up to a minute** · S4 · Window buttons · By design

Spec 0009 limits probing to 3 probes per window per minute, so a misbehaving app can't keep WinGnome sending it
hit tests. Each new size of a probed window (after it has held still for 300 ms) needs a probe, so the fourth
new size within a minute keeps the circles hidden until the minute is up. Going back to the size probed just
before (restore after maximise) reuses that probe and costs nothing, so toggling maximise isn't affected.
Measured on VS Code: three maximise/restore cycles kept the circles in place throughout.

### KI-041
**Clicks on custom title bars wait for one hit test, and clicks meanwhile are ignored** · S4 · Window buttons · By design

Before a click on a probed window is passed on, the native button's position is hit-tested again off the UI
thread (one `WM_NCHITTEST`, at most 50 ms; normally well under 1 ms). A second click before that answer arrives
is ignored, and a click on a hung app is dropped (its own buttons don't respond either; DWM's ghost window
takes over). When the answer doesn't match, the click is dropped with a log line and the window is probed again.

The check is only strong where the expected answer is a button code (Claude desktop, VS Code, Docker Desktop,
and Dia's maximise zone, which is also re-checked for Dia's close and minimise clicks). For GitHub Desktop's
HTML buttons it expects `HTCLIENT`, which most of the window answers: it catches buttons that turned into drag
region, but if a new layout put other client content where a button was, a click before the next probe would
still send the command. The row checks (three equal holes of the profiled width at the edge, drag region to
their left) and the separate opt-in setting are the main protection there.

### KI-042
**WinGnome builds from before the window-buttons role still decorate alongside newer ones** · S4 · Window buttons · Open

Since dbf86fb only the instance holding `Local\WinGnome-WindowButtons` decorates windows (KI-015). Older builds,
such as an everyday copy in `publish\` that predates it, don't know the mutex, so they and a newer instance both
draw, as before. Taking over from a killed holder (abandoned mutex) is handled in code but wasn't exercised live
(agents quit instances gracefully). Turning window buttons off and on quickly may log "another instance
decorates windows" once while the previous thread releases the mutex.

If the mutex can't be opened because an instance at another integrity level (elevated) created it, this
instance treats the role as held and doesn't decorate; it can't wait for that holder, so it only takes over
after a restart or switching window buttons off and on. Any other failure to create or wait on the mutex fails
open: the instance logs a warning and decorates, as before the role existed, rather than silently showing no
buttons; two instances may then both draw.
*Fix direction:* republish the everyday copy.

### KI-050
**Another WinGnome instance draws its circles over this one's header bars** · S4 · Settings · Open

The settings window and app picker hide the Windows caption with `WindowChrome` but keep `WS_CAPTION`, the
system menu and the maximise box (needed for Snap Layouts), so DWM still reports caption-button bounds. Each
instance skips only its own process, so another WinGnome that decorates windows overlays its circles on top of
the header bar's own ones. Since dbf86fb only one instance per session decorates (the window-buttons session
role), so with current builds this happens only when the decorating instance is a different one from the
instance whose header bar is shown, e.g. a QA profile's settings window while the everyday instance holds the
role. A pre-role build (KI-042) still decorates regardless and overlays every other instance's header bars. One
instance alone is unaffected. Capture header bars with `PrintWindow` to see only the window's own drawing.

### KI-052
**Explorer windows other than folder windows don't join the File Explorer pin** · S4 · Dock · Open

Only `CabinetWClass` windows in `explorer.exe` are treated as File Explorer (`AppIdentity.ImpliedAppUserModelId`,
spec 0006). Other un-owned `explorer.exe` windows that pass the alt-tab filter, such as a file-copy progress
window, have no AUMID and still show as a separate unpinned "Explorer" icon, where the Windows taskbar groups them
under File Explorer. *Fix direction:* list the classes seen live (for example `OperationStatusWindow`) and add
them to the rule, keeping the desktop, taskbar and other shell windows out.

## Resolved

| ID | Severity | Area | Summary | Fixed in |
|---|---|---|---|---|
| KI-002 | S4 | App | The executable had no app icon | 2f7c44b |
| KI-009 | S4 | Repo | No CI workflow, although the README said `--selftest` is used by CI | 94b9013 |
| KI-011 | S2 | Settings | Non-safe runs with `--settings-dir` rewrote or deleted the shared "Start with Windows" entry | 6aeef5f |
| KI-012 | S3 | Settings | A settings folder that couldn't be written silently dropped every change | 6e07eb0 (warning banner) |
| KI-013 | S4 | Top bar | Large hover corner radius drew oval highlights instead of pills | c26e1c8 |
| KI-015 | S3 | Window buttons | Two WinGnome instances that both decorated windows fought over title-bar colours | dbf86fb (one instance per session decorates; see KI-042) |
| KI-017 | S3 | Window buttons | The patch behind the circles didn't match Mica title bars and hid the window border | 45ae75a |
| KI-020 | S3 | App | A graceful `taskkill` that reached the tray host window was ignored, so WinGnome didn't quit | 085fa14 |
| KI-030 | S4 | Overview | The first overview open after start took about half a second (WPF's first full-screen frame) | 1e4522f (cloaked warm-up 3 s after start) |
| KI-051 | S3 | Accessibility | The round window buttons weren't exposed to screen readers or the keyboard | fea478b |
