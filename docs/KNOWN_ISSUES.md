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
or *Fixed* (with the commit). When in doubt, pick the higher severity. A resolved entry may carry a `### KI-…`
detail section below the Resolved table when the measured evidence behind the fix is worth keeping (KI-100).

IDs are allocated before their entries exist: KI-093 to KI-097 are reserved by spec 0017 (KI-098 to KI-101 all
have entries now). The next free ID is **KI-102**; grep the specs for `KI-0` before allocating one.

## Open

| ID | Severity | Area | Summary | Status |
|---|---|---|---|---|
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
| [KI-022](#ki-022) | S4 | Tray | A `WM_CLOSE` posted to "the taskbar" quits WinGnome while it hosts tray icons | By design |
| [KI-023](#ki-023) | S4 | Dock | Two pins with the same target group the app's windows into the first one | Open |
| [KI-031](#ki-031) | S4 | Overview | Overview animation details not verified on every path | Open |
| [KI-032](#ki-032) | S4 | Overview | The overview's close glide gets 8–13 frames | Open |
| [KI-033](#ki-033) | S4 | Overview | A dock request for some windows doesn't narrow an open overview | Open |
| [KI-040](#ki-040) | S4 | Window buttons | A custom title bar resized often keeps its circles hidden for up to a minute | By design |
| [KI-041](#ki-041) | S4 | Window buttons | Clicks on custom title bars wait for one hit test, and clicks meanwhile are ignored | By design |
| [KI-042](#ki-042) | S4 | Window buttons | WinGnome builds from before the window-buttons role still decorate alongside newer ones | Open |
| [KI-050](#ki-050) | S4 | Settings | Another WinGnome instance draws its circles over this one's header bars | Open |
| [KI-052](#ki-052) | S4 | Dock | Explorer windows other than folder windows don't join the File Explorer pin | Open |
| [KI-055](#ki-055) | S4 | Top bar | The Wi-Fi icon doesn't show signal strength | Open |
| [KI-056](#ki-056) | S4 | Launching | Elevation details: an undocumented host value, and a late UAC answer can close a reopened overview | Open |
| [KI-060](#ki-060) | S4 | Settings | Choosing the default sound device uses the undocumented `IPolicyConfig` | Open |
| [KI-061](#ki-061) | S4 | Settings | The power mode uses undocumented powrprof functions | Open |
| [KI-062](#ki-062) | S4 | Settings | Display scale, orientation and turning displays on or off are left to Windows Settings | By design |
| [KI-063](#ki-063) | S3 | Settings | The Settings panels' writes are verified by code review and tests, not yet on a live machine | Open |
| [KI-064](#ki-064) | S4 | Settings | Snap and Alt+Tab options may need a new sign-in to take effect | Open |
| [KI-066](#ki-066) | S4 | Settings | Input sources are listed but not switched or reordered in the Keyboard panel | Open |
| [KI-067](#ki-067) | S4 | Settings | Appearance style and accent are read-only while the matching Streamline tweak is on | By design |
| [KI-068](#ki-068) | S4 | Settings | A few exit paths leave an unconfirmed display change until sign-out | Open |
| [KI-069](#ki-069) | S4 | Tray | Some apps open tray flyouts on the primary monitor when clicked on another monitor's bar | By design |
| [KI-070](#ki-070) | S4 | Top bar, Dock | Reclaiming a force-killed instance's strips relies on undocumented Explorer behaviour | Open |
| [KI-071](#ki-071) | S4 | Dock | Edge reveal is unreliable for a dock on an inner edge between two monitors | Open |
| [KI-072](#ki-072) | S4 | Overview | Activities and hot corners on other monitors open the overview on the primary | By design |
| [KI-085](#ki-085) | S4 | Settings | Notifications use undocumented registry values, and the app list is only the part Windows keeps in the registry | Open |
| [KI-086](#ki-086) | S4 | Settings | Privacy switches write the undocumented `ConsentStore`; device-wide switches are read-only | Open |
| [KI-087](#ki-087) | S4 | Settings | Accessibility: cursor size uses undocumented values, and high contrast is read-only until a live spike passes | Open |
| [KI-088](#ki-088) | S4 | Settings | Slow keys and bounce keys can't both be on, and bounce keys turn repeat keys off | By design |
| [KI-089](#ki-089) | S4 | Settings | Apps: limited details for packaged apps, and uninstall limits | Open |
| [KI-090](#ki-090) | S4 | Settings | Startup apps: machine-wide items are read-only, packaged startup tasks aren't listed | Open |
| [KI-091](#ki-091) | S4 | Settings | Printers and Removable Media: undocumented values, no change notifications, a limited event list, little tested on real devices | Open |
| [KI-092](#ki-092) | S4 | Settings | Region & Language formats and Windows Update status are partly left to Windows Settings | By design |
| [KI-098](#ki-098) | S4 | Top bar, Dock | `MonitorKeyOf` can map a recycled HMONITOR to the wrong monitor for up to 250 ms | Open |
| [KI-099](#ki-099) | S3 | Top bar, Dock | WinGnome sets monitor work areas directly when Explorer doesn't apply a strip it granted | Open |
| [KI-101](#ki-101) | S4 | Settings | About and Displays bypass the shared load gate; a failed About read shows nothing at all | Open |

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
**A `WM_CLOSE` posted to "the taskbar" quits WinGnome while it hosts tray icons** · S4 · Tray · By design

While WinGnome hosts the tray, its hidden host window is the first `Shell_TrayWnd` that `FindWindow` returns.
Since 085fa14 a posted `WM_CLOSE` to it is treated as a quit request (that fixed KI-020, where a graceful
`taskkill` was ignored). A tool or script that posts `WM_CLOSE` to the taskbar to open Explorer's *Shut Down
Windows* dialog therefore quits WinGnome instead.
*Verified (2026-10-09):* the `ControlWindow` filter is what acts, and it is thread-wide — its
`ComponentDispatcher.ThreadFilterMessage` hook catches a `WM_CLOSE` posted to **any** window on the UI thread, not
just the tray host. Posting `WM_CLOSE` to the running everyday instance's Settings window (hwnd 0x260410) quit the
whole app: "Close requested from outside (WM_CLOSE); shutting down", then *Window buttons stopped*, *Tray host
stopped*, *Taskbar restored*, "WinGnome exited with code 0". That is by design: a posted `WM_CLOSE` is the quit
request, whichever of our windows a graceful `taskkill` (without `/f`) happens to pick, and forwarding the message
to Explorer instead would bring back KI-020. Practical consequence for anything automating WinGnome: you cannot
close the settings window — or any other WinGnome window — programmatically with `WM_CLOSE` without quitting the
shell.

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

### KI-055
**The Wi-Fi icon doesn't show signal strength** · S4 · Top bar · Open

`NetworkMonitor` only knows whether the connection is wired, wireless or absent, so the bar shows one
"connected" Wi-Fi wedge (spec 0012) where GNOME shows 0–4 bars. Signal strength changes without a network-change
event, so adding it means either polling (WLAN API or `ConnectionProfile.GetSignalBars`) or refreshing it only on
network changes and when quick settings opens. Draw the 0–3 bar icons in `Theme/SymbolicIcons.xaml` (GNOME dims
the unlit part of the wedge) when a source is added.

### KI-056
**Elevation details: an undocumented host value, and a late UAC answer can close a reopened overview** · S4 · Launching · Open

Spec 0014 tells full-trust packaged apps (Windows Terminal, Notepad) from UWP apps by the AppsFolder property
`System.AppUserModel.HostEnvironment`, whose value 2 isn't documented by Microsoft (checked on one machine against the
apps' manifests). If a Windows build changes it, those apps stop offering *Run as administrator* rather than
misbehave. Also, the overview stays open while an elevated launch waits for UAC; if the user dismisses and reopens
it before answering, the late "started" callback closes the reopened overview.

### KI-060
**Choosing the default sound device uses the undocumented `IPolicyConfig`** · S4 · Settings · Open

Windows has no documented API to change the default audio endpoint. The Sound panel uses `IPolicyConfig`
(`CPolicyConfigClient` `{870AF99C-…}`, interface `{F8679F50-…}`, `SetDefaultEndpoint` for the console,
multimedia and communications roles), as Windows' own sound settings and tools such as SoundSwitch do. It has been
stable since Windows 7, but an update could change it. The panel checks that the interface answers when it opens;
if not, the device drop-downs are disabled and point to Windows Settings, and a failed call shows the problem banner
with the link (`Interop/PolicyConfig.cs`, `Panels/Sound/AudioDevices.cs`).

### KI-061
**The power mode uses undocumented powrprof functions** · S4 · Settings · Open

`PowerGetEffectiveOverlayScheme` and `PowerSetActiveOverlayScheme` (the power mode behind Windows 11's *Best power
efficiency / Balanced / Best performance*) are exported by `powrprof.dll` but not documented. They are resolved at
run time (`NativeMethods.Power.cs`); when they are missing, or Windows reports no power mode (Windows 11 has none
while a plan other than Balanced is active), the Power Mode group is hidden and the rest of the panel works.

### KI-062
**Display scale, orientation and turning displays on or off are left to Windows Settings** · S4 · Settings · By design

Changing the scale needs the undocumented `DisplayConfigSetDeviceInfo` type −4, so the Displays panel shows the
effective scale (documented `GetDpiForMonitor`) read-only with a *Change in Windows Settings* button. Rotation,
mirroring, HDR, and enabling or disabling a display aren't offered in this MVP; *More in Windows Settings* opens
the page. Modes are listed at the current colour depth; a display rotated to portrait reports its modes as Windows
gives them.
Cloned (mirrored) displays share one source mode: changing either one's resolution changes both, and the panel
lists them as separate displays. Making or breaking a clone, like turning displays on or off, is left to Windows
Settings.

### KI-063
**The Settings panels' writes are verified by code review and tests, not yet on a live machine** · S3 · Settings · Open

Every panel was opened and read on Windows 11 25H2 (single display, laptop) and checked against Windows Settings,
in light and dark mode at 125 %, but in `--safe` mode: to avoid changing the developer's machine, no display mode,
default device, power plan value, power mode, time zone, wallpaper, accent, style, mouse, keyboard or snap value was
written during development. The write paths follow the documented APIs, run off the UI thread, log Win32 errors and
show the problem banner; Displays tests every mode before applying, records the previous and new settings first,
applies the whole configuration atomically for the session only (`SetDisplayConfig` without `SDC_SAVE_TO_DATABASE`
until *Keep Changes*) and always counts down to a revert. The refresh-rate rewrite (GDI's whole rates mapped to the path's
fraction, 59 to 60000/1001, with the target timing left for Windows to choose) is the part most worth checking live;
if Windows adjusts the configuration, the panel records and keeps what is really showing. Multi-display arrangement was exercised only through `DisplayArrangement` tests.
*Next step:* a manual pass per panel on a test machine (acceptance criteria 2 of spec 0015): change, confirm it
shows in Windows Settings, change back; for Displays, let the countdown revert, kill WinGnome during a countdown
and restart it.
The native panels of spec 0020 (Notifications, Printers, Apps and Startup apps, Accessibility, Region & Language,
Privacy, Removable Media, Windows Update) were partly checked live; what remains unchecked for each is listed in
KI-085 to KI-092.

### KI-064
**Snap and Alt+Tab options may need a new sign-in to take effect** · S4 · Settings · Open

The Multitasking panel writes `SnapAssist`, `EnableSnapAssistFlyout` and `VirtualDesktopAltTabFilter` under
`HKCU\…\Explorer\Advanced`, the values Windows Settings writes, and broadcasts `WM_SETTINGCHANGE` ("TraySettings");
Explorer may still read some of them only when it starts (the Streamline snap-flyout tweak restarts Explorer for
that reason). *Snap Windows* uses
`SPI_SETWINARRANGING` and applies at once. *Fix direction:* confirm live which values need a restart, and offer an
Explorer restart for those as the Streamline page does.

### KI-066
**Input sources are listed but not switched or reordered in the Keyboard panel** · S4 · Settings · Open

`ActivateKeyboardLayout` only switches the calling process, so the panel lists the session's input sources (from
`GetKeyboardLayoutList`), marks the one in use, and leaves switching to Win+Space and adding or removing to Windows
Settings (linked). Layout names are derived from the HKL (the layout's language), not the registry's layout text,
so a variant shows as "Keyboard variant N". *Fix direction:* `ITfInputProcessorProfileMgr` for names, switching and
order.

### KI-067
**Appearance style and accent are read-only while the matching Streamline tweak is on** · S4 · Settings · By design

The *Dark mode* and *Adwaita blue accent* tweaks own those registry values and back up the originals to restore
them when turned off. If the Appearance panel changed them too, reverting the tweak would silently undo the panel's
change, so the rows are disabled with a note pointing to the Streamline page while the tweak is on. The same holds
for *Snap Layouts* and the *Disable the snap layouts flyout* tweak.

### KI-068
**A few exit paths leave an unconfirmed display change until sign-out** · S4 · Settings · Open

An unconfirmed display change is never saved to Windows' display database, so signing out or restarting always
drops it, and the revert record makes the next WinGnome start revert it. Between those, the change stays showing
when: WinGnome quits while the change is still being applied (the countdown hasn't started, so shutdown has nothing
to revert; the record does it at the next start); the UI thread is hung, so neither the countdown nor *Revert* runs;
WinGnome is force-killed and next started with a different `--settings-dir` (the record is per profile) or with
`--safe` (which never changes system state, so it leaves the record for a normal start). *Workaround:* sign out, or
start WinGnome normally with the same profile.

### KI-069
**Some apps open tray flyouts on the primary monitor when clicked on another monitor's bar** · S4 · Tray · By design

Every bar shows the same tray icons, and a click passes that bar's anchor (version 4 apps) and, through
`Shell_NotifyIconGetRect`, that icon's rectangle, so most menus open beside the icon that was clicked. Apps that
place their flyout from the taskbar instead (`ABM_GETTASKBARPOS`, or the rectangle of `Shell_TrayWnd`) still get the
primary's: Explorer answers `ABM_GETTASKBARPOS`, and WinGnome's hidden tray host window stays on the primary bar's
strip. `Shell_NotifyIconGetRect` also has one answer per icon (the bar it was last used on), so a flyout the app
opens on its own later appears there. *Fix direction:* none planned (spec 0010 non-goal).

### KI-070
**Reclaiming a force-killed instance's strips relies on undocumented Explorer behaviour** · S4 · Top bar, Dock · Open

A force-killed WinGnome cannot send `ABM_REMOVE`. Spike 0 of spec 0010 (Windows 11 build 26200, taskbar auto-hidden
by another WinGnome instance, a primary at 125 % and an upper secondary at 100 %): Explorer gave back a killed
process's left-edge strips on both monitors within about 300 ms, before any further AppBar traffic, and a 1×1
`ABM_NEW` + `ABM_REMOVE` from another process changed nothing more. `AppBarJanitor` makes that nudge at every start
and on `--restore-taskbar` and logs every work area before and after; `--selftest` fails if a work area differs
after shutdown. Neither the automatic reclaim nor the nudge is documented. Not yet verified with Explorer's taskbar
visible (the user's everyday instance hides it). *Workaround:* `--restore-taskbar`, or sign out. *Since outcome B of
spec 0010 (KI-099)* a work area WinGnome set itself is recorded and recovered from `workareas.state`, so what still
relies on this undocumented behaviour is only a strip Explorer applied on its own and then failed to reclaim.

### KI-071
**Edge reveal is unreliable for a dock on an inner edge between two monitors** · S4 · Dock · Open

With docks on every display, a dock whose edge borders another monitor (for example the bottom of a monitor placed
above the primary) is revealed by pushing the pointer against that edge, but the pointer crosses to the other
monitor instead of stopping. Always-visible docks are unaffected. The dock logs its monitor and edge on layout; no
detection or Settings hint. *Workaround:* use Always visible, or move the dock to an outer edge.

### KI-072
**Activities and hot corners on other monitors open the overview on the primary** · S4 · Overview · By design

Every bar has an Activities button and every monitor with a true top-left corner a hot corner, but the overview,
its thumbnails and backdrop still cover only the primary monitor. *Fix direction:* a per-monitor overview spec.

### KI-085
**Notifications use undocumented registry values, and the app list is only the part Windows keeps in the registry** · S4 · Settings · Open

The panel (spec [0020](specs/0020-native-system-panels.md)) reads and writes undocumented HKCU values:
`PushNotifications\ToastEnabled` and `LockScreenToastEnabled` first, then `NOC_GLOBAL_SETTING_TOASTS_ENABLED` /
`NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK` as the fallback (written only when they already exist), the
`NoToastApplicationNotificationOnLockScreen` policy, and per app `Enabled`, `ShowBanner` and `ShowInActionCenter`. The
app list is the registry subset of Windows' own list (`wpndatabase.db`): apps that never wrote a key are missing,
Windows' own sources are shown only from a small name table, and generated notification-icon ids are hidden. The Do Not
Disturb row stays hidden until spec 0017's service is added to `SystemPanelServices` and
`NotificationsPanelViewModel.DoNotDisturb` is filled in `Open`.
*Not verified live:* the registry writes round-trip through the panel (master, lock screen and one app's three
switches), but that a disabled app stops showing toasts without signing out (acceptance criterion 3) was not
demonstrated: `ToastNotifier.Setting` for the test app did not change after the per-app `Enabled` write, and a test
toast from a throwaway id created no settings key. *Next step:* send a real toast from a listed app with its switch off;
if it still shows, the row must say "Takes effect after you sign out".

### KI-086
**Privacy switches write the undocumented `ConsentStore`; device-wide switches are read-only** · S4 · Settings · Open

The switches write `CapabilityAccessManager\ConsentStore` values under HKCU (`Value`, `NonPackaged\Value`, per-package
`Value`). The device-wide switches (HKLM) are read-only and need an administrator. A per-app value of `Prompt` is shown
off and turning it on stores `Allow`; a value other than Allow, Deny or Prompt is shown off and left unchangeable.
Desktop apps have no per-app switch (Windows has none) and show only *In use* / *Last used*. Packaged apps the app
catalogue can't name (system packages) are hidden.
*Not verified live:* the microphone "Let apps access" and "Let desktop apps access" switches and one packaged app
round-tripped and were restored, but that the Camera app then reports no access without signing out (acceptance
criterion 7) was not exercised.

### KI-087
**Accessibility: cursor size uses undocumented values, and high contrast is read-only until a live spike passes** · S4 · Settings · Open

*Cursor size* writes the undocumented `HKCU\Software\Microsoft\Accessibility\CursorSize` (1–15) and
`HKCU\Control Panel\Cursors\CursorBaseSize` (32 + 16 per step), then `SPI_SETCURSORS` (`CursorSizeStore`). The two
registry writes are separate, so if the second fails the first stays written (the problem banner shows). The row is
disabled with a link to Windows Settings for black, inverted and custom-colour pointers (`CursorType` ≠ 0) and for a
user cursor scheme (`Scheme Source` = 1), because Windows regenerates those cursor files through a private API. Pointer
colour, text size (`TextScaleFactor`, shown read-only) and the text cursor indicator stay in Windows Settings (by
design).
*High contrast* is a read-only row with a link to `ms-settings:easeofaccess-highcontrast` until a spike on a live
machine passes (spec: spike first, link if unproven). It shows the theme Windows reports (Aquatic = "High Contrast
Black"/`hcblack.theme`, Desert = "High Contrast White"/`hcwhite.theme`, Dusk = "High Contrast #1"/`hc1.theme`, Night sky
= "High Contrast #2"/`hc2.theme`, matched by the files' colours; the read accepts the Windows 11 name, the legacy name,
the file name or a path). The write was removed from the UI (no dead code); how to put it back and the spike steps are in
the spec's Accessibility section.
*Not verified live:* the high-contrast spike (turns the whole desktop to a contrast theme; needs the user at the
machine) and cursor size (the development machine has a custom-colour pointer, so the row is correctly disabled). The
other switches (sticky, slow and bounce keys, text cursor thickness, reduce animation, on-screen keyboard) were checked
live and restored bit for bit. WinGnome's own surfaces don't follow high contrast (separate work).

### KI-088
**Slow keys and bounce keys can't both be on, and bounce keys turn repeat keys off** · S4 · Settings · By design

Windows' `FILTERKEYS` needs a bounce time to come with the acceptance delay and repeat timings at 0, so turning one of
slow and bounce keys on turns the other off and the panel says so. Turning bounce keys on also turns Windows' repeat
keys off (`iDelayMSec`/`iRepeatMSec` 0), and switching back to slow keys leaves them off; Windows Settings' filter keys
page turns them back on. WinGnome has no repeat-keys control to restore them from.

### KI-089
**Apps: limited details for packaged apps, and uninstall limits** · S4 · Settings · Open

Packaged apps show no size, and publisher, version and install date only when the row is expanded; the first expand or
packaged uninstall loads the WinRT projection (`Microsoft.Windows.SDK.NET`), which then stays for the session.
Uninstallers that relaunch themselves from %TEMP% exit early, so the list still shows the app until *Refresh*; an
uninstaller that fails to start is only logged (`ShellLaunch.StartAndWatch` doesn't report the failure). A desktop app
gets WinGnome's *Uninstall…* only when its command is safe (`UninstallPlan`: MSI product code → `System32\msiexec.exe
/X`, bare `msiexec`/`rundll32` rooted at System32, otherwise a fully qualified `.exe`); every other one shows
*Uninstall in Windows Settings*. System-signed and framework packages can't be removed here. The Recycle Bin operation
has no owner window (the panel has no HWND), so a shell prompt such as "delete permanently?" isn't modal to Settings.
*Not verified live:* uninstall itself was not run during development (desktop or packaged); the plans were checked by
Core tests and by reading all 282 Uninstall keys on the development machine (64 listed, 63 with a plan, 1 without an
`UninstallString`).

### KI-090
**Startup apps: machine-wide items are read-only, packaged startup tasks aren't listed** · S4 · Settings · Open

Machine-wide items (HKLM Run, Run32, common Startup folder) are read-only with an *Open Task Manager* button, and their
approval is read from HKLM `StartupApproved` only. The spec's standard-user check of where Task Manager stores an HKLM
item's approval wasn't possible (the development account is an administrator, and a test HKLM item needs elevation); an
HKLM item disabled earlier through Task Manager had its value under HKLM. Packaged apps' startup tasks aren't listed (no
documented API; a row links to `ms-settings:startupapps`). Per-user items can be switched on and off; only Startup-folder
shortcuts can be removed (to the Recycle Bin), HKCU Run values can be turned off but not removed (deferred by design).
*Add app…* reuses the dock's app picker, whose heading still reads "Add app to dock" (`IDialogService.PickApp` takes
no title).
*Not verified live:* Task Manager's display after a toggle (it runs elevated, so it can't be automated) and start-up at
the next sign-in.

### KI-091
**Printers and Removable Media: undocumented values, no change notifications, a limited event list, little tested on real devices** · S4 · Settings · Open

Undocumented storage: `LegacyDefaultPrinterMode` (HKCU `Software\Microsoft\Windows NT\CurrentVersion\Windows`, DWORD,
1 = the user manages the default printer, 0 or absent = Windows does; stable since Windows 10 1511) is isolated in
`PrinterService`, and the AutoPlay handler keys (HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers`:
`DisableAutoplay`, `UserChosenExecuteHandlers\<Event>`, `EventHandlersDefaultSelection\<Event>`, handlers from
`EventHandlers\<Event>` and `Handlers\<id>\Action|Provider` in HKCU and HKLM) are isolated in `AutoplayStore`. Both
panels show what they read back after every write.
*Printers:*
- The list is one `EnumPrinters` (level 2, local and connections) per open, action and *Refresh*; there is no change
  notification, so a printer added, removed or going offline elsewhere shows on the next *Refresh*. A print server that
  doesn't answer keeps the list empty until its RPC times out (the read has its own thread, so the window stays
  responsive).
- With the print spooler stopped or disabled the list reads as empty with the "couldn't read the printer list" banner;
  the *Let Windows manage my default printer* switch still works (a registry value).
- *Print queue*, *Printer properties* and *Printing preferences* start `rundll32.exe printui.dll,PrintUIEntry /o|/p|/e
  /n "<name>"`; a printer whose name contains a double quote can't be passed on (PrintUIEntry has no escape) and the
  panel points to Windows Settings instead.

*Removable Media:*
- Only the media types in `AutoplayModel`'s event table are listed (about 18 of the ~35 events Windows registers);
  vendor-specific portable-player events and `WPD` are hidden. An event Windows knows but this machine has no named
  handler for is hidden too, except Removable drive and Memory card, which are always listed.
- *Open folder* is offered for every media type, as the spec says; Windows Settings itself only offers it for some. If
  Explorer ignores it for a type (for example a DVD movie), the choice is stored but has no visible effect.
- In shell mode (spec 0013) Explorer's AutoPlay does not run, so the choices have no effect there.
- The *Memory card* row uses `ShowPicturesOnArrival`; Windows Settings may store that choice under
  `UserChosenExecuteHandlers\CameraAlternate\ShowPicturesOnArrival` instead. Unverified: this machine has no
  `CameraAlternate` key (HKCU has `EventHandlers\CameraMemoryOnArrival`). *Next step:* change *Memory card* in Windows
  Settings and diff the registry.

*Shared failure path:* **fixed** in c8f1c26 + 8b409f0: `LoadAsync` takes an optional `onFailed` callback that runs when
the read throws, on the dispatcher and under the same conditions as `show` (still open from the same `Open`, still the
newest load on its channel — the counting lives in Core's `PanelLoadGate`, with tests). Printers, Removable Media and
Apps pass it, so a failed read clears the busy state instead of leaving the controls disabled or the loading indicator
up until the panel is reopened.
*Correction (2026-10-09, measured):* the Printers symptom behind the fix does not reproduce on a machine whose spooler
is merely disabled. With the spooler off, `EnumPrinters` fails with Win32 error 1722, but `PrinterService.Read` catches
that internally and returns a `PrinterSnapshot` with `ListFailed = true`, so `LoadAsync`'s throw path is never taken
and the panel was never stuck. PrintWindow screenshots of the panel on the pre-fix build (3d65d76) and the fixed build
(e3fe726) are pixel-identical: the printer-specific banner ("Couldn't read the printer list from Windows. The print
spooler may be stopped…"), *Add Printer…* and *Refresh* enabled, the default-printer toggle enabled, no misleading
"no printers" note. Both builds logged only "Printers: could not list the printers" (the service's own catch), never
`LoadAsync`'s "Settings: could not read the \"printers\" panel's settings"; Removable Media and Apps read successfully
here, so no panel threw and none was stuck. The `onFailed` fix stands as a correct, defensive change and its Core gate
is unit-tested, but UI-level proof of the failure path needs a read that really throws (an injected throw in
`AutoplayStore.Read` or `InstalledAppsService.Read`), not a disabled spooler.
*Not verified live:* printers and set-default were not tried on real printers (the spooler is disabled on the
development machine), and no USB stick, memory card or disc was inserted for AutoPlay.

### KI-092
**Region & Language formats and Windows Update status are partly left to Windows Settings** · S4 · Settings · By design

*Region & Language:* the format locale and display language are changed in Windows Settings; the panel shows them
read-only with links. Region, the five formats and the first day of the week are written here (documented
`SetLocaleInfoW` / `SetUserGeoName`). .NET keeps the user's formats on the `CurrentCulture` instance it already built,
so `ClearCachedData()` alone doesn't make the clock and calendar follow a format change; `SettingsFeature` replaces the
culture on `WM_SETTINGCHANGE "intl"` (see the spec). The first day of the week and the short date were checked live
(changed, read back, restored exactly). Region, long date, short and long time and *Reset to defaults* use the same code
path but were not changed live.
*Windows Update:* the panel shows the Windows Update Agent's cached state only (offline search, `Online = false`):
updates Windows hasn't found yet, or found since the last check, aren't listed until Windows checks, and the list can
differ from Windows Settings', which also merges Microsoft Store and driver sources. The panel never scans or installs.
A standard user may be refused by policy on managed machines; the panel then shows the error with the Windows Settings
link.

### KI-098
**`MonitorKeyOf` can map a recycled HMONITOR to the wrong monitor for up to 250 ms** · S4 · Top bar, Dock · Open

`DisplayLayoutService.MonitorKeyOf` maps a `MonitorFromWindow` handle through the handle→key table built by the last
monitor read. HMONITORs are not stable across display changes and Windows can reuse a value for a different monitor,
so a window can be given the wrong key until the next pass: at most 250 ms, plus the 1.5 s follow-up. Effect: a bar's
focused app, an isolated dock's window list, or a dock's full-screen/intellihide check can be wrong for that moment.
There is no AppBar or work-area effect — a bar whose cached monitor rectangle no longer matches a live monitor
undocks itself (spec 0010's detach guard), and `WorkAreaController` re-derives the monitor from the rectangle it is
given and refuses one whose bounds changed (spec 0010, risk 10).
*Fix direction:* compare the handle's own `GetMonitorInfo` bounds with the table entry's before trusting the key.

### KI-099
**WinGnome sets monitor work areas directly when Explorer doesn't apply a strip it granted** · S3 · Top bar, Dock · Open

Explorer grants WinGnome's AppBars their strips but applies them to monitor work areas only inside its own taskbar
layout pass. That pass is deferred while its taskbar is auto-hidden and `SW_HIDE`n (measured ~35 s on build 26200,
in the build before spec 0010 as well — and ~35 s is not a ceiling: the pre-fix build, restarted 2026-10-09 20:14,
still had the primary's work area at its full bounds when observed at 20:28) and can be skipped entirely after a
monitor is unplugged, so maximised windows cover the bar and the strip is left unreserved. Re-registering the AppBar
did not help and may restart the deferral (three live sessions, spec 0010).

Since outcome B of spec 0010, a bar whose granted strip is still missing 1.5 s after a check sets that monitor's work
area itself with the documented `SystemParametersInfo(SPI_SETWORKAREA)`, never `SPIF_UPDATEINIFILE`, so nothing is
persisted to the user's profile. Limits, all deliberate:
- Only the bar's own edge moves, and always from a **fresh** `GetMonitorInfo` read taken inside the same lock as the
  write, so the taskbar's strip and other AppBars' strips survive and two bars on one edge stack.
- Bounded twice: at most three actions per bar per missing-strip episode (1.5 s, 5 s, 20 s apart, then it gives up
  until the bar is docked afresh) and three applications per monitor per 60 s. A refusal is logged, so a third-party
  tool that also sets work areas cannot be fought in a loop.
- Recorded before it is changed, in `workareas.state` in the settings directory (written aside and moved into place,
  so a crash cannot truncate it): **no record, no shrink**. At most one record per bar and monitor: a re-shrink
  replaces the pair's record and moves to the end of the list (the unwind order), so something that keeps resetting
  a work area cannot grow the marker. `WorkAreaRecovery` unwinds a monitor's records newest first and only while each
  is still the live value, so a top bar's and a dock's strips both go back and a live bar's is never written over. Recovered on exit, on the crash path (without the `WM_SETTINGCHANGE` broadcast, so a hung
  window cannot block it), on the next start and by `--restore-taskbar`.
- `--safe` and `--selftest` never shrink; they do recover, which is a repair of an earlier run's change.
- A marker that cannot be parsed is not read as empty (that would strand the shrinks it described): where the taskbar
  marker says the taskbar is hidden, every monitor's work area is reset to its full bounds; where it does not,
  nothing is written so a visible taskbar keeps its strip. The janitor nudges Explorer right after either way.

*Risks:* WinGnome now writes a value Explorer also writes, so Explorer recomputing later can overwrite ours (it
normally produces the same rectangle while our AppBar is registered; a different one is caught by the next check,
within the budget). The marker is per profile, so a force-kill followed by a start with a different `--settings-dir`
cannot recover, as for `display-revert.json` (KI-068). `--restore-taskbar` does not take the single-instance mutex
(as it already did not for `taskbar.state`), so running it while a healthy instance is up gives back a strip that
instance's bar still holds and deletes its marker; the broadcast reaches the live bar, which re-shrinks and rewrites
the marker within ~1.5 s, so only a force-kill inside that window could strand it.
*Verified live (2026-10-09, build 26200; DISPLAY1 primary 2560×1600 at 125 % at (0,0), DISPLAY2 3440×1440 at 100 %
above it at (−447,−1440)):*
- **B6, in the real failing scenario.** The everyday build, republished from merged main (e85e68e) and started
  through `explorer.exe` at 20:35:22: taskbar hidden 20:35:22.8; the display pass at 20:35:24.990 found Explorer had
  applied *neither* strip; both bars logged "leaving it to Explorer for 1.5 s"; then exactly one direct set per
  monitor — `\\.\DISPLAY1: work area set directly for the Top strip 0,0,2560,40: 0,0,2560,1600 -> 0,40,2560,1600`
  at 20:35:26.791 and the DISPLAY2 mirror (`-447,-1440,2993,0 -> -447,-1408,2993,0`) at 20:35:27.096 — "the strip
  … is reserved again" for both at 20:35:27.097, and the forced pass at 20:35:33.490 found both still reserved with
  no further action. Both strips reserved ~4.3 s after start; no repeats, no budget warning, no WARN/ERROR.
  `%APPDATA%\WinGnome\workareas.state` held exactly two records — `Original` the full bounds, `Applied` the shrunk
  rectangles, owners the two bar HWNDs from the log (0xF05FC, 0x1550530); `taskbar.state` still `{"WasAutoHide":false}`.
- **Recovery paths** (a non-safe test profile that did *not* hide the taskbar — top bars on both displays, dock and
  window buttons off — with the fallback triggered by resetting DISPLAY2's work area from another process with
  `SPI_SETWORKAREA`): the shrink fired ~1.5–3 s after each reset, one line, marker written with one record whose
  `Owner` was the bar's HWND; a second reset produced a second shrink (this exposed the duplicate-record growth that
  `WorkAreaLedger` then fixed). Graceful quit: Explorer restored the strip first, so the plan dropped the record
  through its "already given back" branch and deleted the marker, and DISPLAY1's work area was never touched at any
  point in the whole test. Force-kill (`taskkill /f`): the marker survived verbatim, Explorer released the dead
  HWNDs' strips itself within about a second, and the `--restore-taskbar` recovery run logged "Recovering 1 work
  area(s) a previous run set directly", found nothing to write and deleted the marker. Corrupt marker
  (`{not valid json`, no `taskbar.state` present): WARN "… is unreadable; repairing what can be repaired", then INFO
  "The taskbar is not hidden, so no work area is reset" — no write at all, file deleted, i.e. the conservative branch
  behaved as designed. `--selftest --safe` exits 0 with the fallback compiled in and writes no marker (verified on
  the branch and again on merged main).
*Still to verify (needs the user at the machine; runbook prepared at
`C:\Users\samhe\AppData\Local\Temp\opencode\mm-session\RUNBOOK.md`):* spec 0010's B7 (unplug/replug the secondary)
and B11 (scale change, primary swap, sleep/resume); the stacked top-bar-plus-always-visible-dock chain on one
monitor (two records unwinding newest first — Core-tested only); B12 (native taskbar mode: taskbar visible, a dock's
strip above the taskbar's); the budget refusal (never triggered — at most two applications per monitor per run were
observed); the taskbar-**hidden** branch of the corrupt-marker repair ("work area reset to the full monitor …"); the
crash path (`EmergencyRestore` → `AppBar.UndockAll` → `ReleaseAll` without the broadcast — only `taskkill /f` was
exercised, which by design runs nothing); and the TbExp hide-order experiment (immediate / settle / nohide / spi),
which would say whether the cheaper option 1 in the spec could complement or replace the fallback.
*Workaround:* none needed; without it the strip simply arrives late or not at all.

### KI-101
**About and Displays bypass the shared load gate; a failed About read shows nothing at all** · S4 · Settings · Open

The two panels that predate spec 0020's `LoadAsync` keep their own `Task.Run(...).ContinueWith` loads and generation
counters, so neither benefits from the shared failure path (`onFailed`), the newest-only load rules or Core's
`PanelLoadGate` generations. When the read throws, About logs it and leaves `Info` null: the heading keeps its
"Reading…" fallback and the rows stay blank until the panel is closed and reopened, with no problem banner to say why.
Displays logs it and shows an empty display list, whose `Show` does raise the banner ("WinGnome couldn't read the
displays."), so its gap is the duplicated machinery, not a silent failure. Found while fixing KI-091's shared failure
path. The fix is to convert both to `LoadAsync`, which brings the banner, `onFailed` and the gate for free and deletes
their local counters.

## Resolved

| ID | Severity | Area | Summary | Fixed in |
|---|---|---|---|---|
| KI-001 | S3 | Dock, Top bar | Dock and top bar appeared on the primary monitor only | 767388e, 32877c1 (spec 0010: a top bar on every monitor, docks on all monitors as an option) |
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
| KI-053 | S4 | Dock | An elevated launch played the launch animation even when the UAC prompt was cancelled | b09a222 (feedback posted back only after `ShellExecuteEx` succeeds) |
| KI-054 | S3 | Dock | Full-trust packaged apps such as Windows Terminal couldn't be run as administrator | b09a222 (`PKEY_AppUserModel_HostEnvironment` in `AppCatalog`) |
| KI-065 | S4 | Settings | Quick settings rows and the gear opened Windows Settings pages instead of the matching native panel | f5dcf13 (`TopBarActions` via `ShowSettings`; `--settings-panel` forwarded to a running instance; Win+I waits for shell-mode hotkeys, spec 0013) |
| KI-100 | S3 | Dock | A pinned Docker Desktop showed a second, unpinned icon while running | c842f5d (launcher-child rule in `AppPathMatch.IsSameInstall`; see below) |

### KI-100
**A pinned Docker Desktop showed a second, unpinned icon while running** · S3 · Dock · Fixed in c842f5d

The user's only Docker pin is the named AUMID `Docker.DockerForWindows.Settings`. Evidence measured live on
Docker Desktop 4.83.0: that AUMID resolves through the Start-menu shortcut to the root launcher
`C:\Program Files\Docker\Docker\Docker Desktop.exe` (FileDescription "Docker Desktop Launcher"; it holds no
windows), while the dashboard window (class `Chrome_WidgetWin_1`, unowned) belongs to a different same-named exe
one folder down, `C:\Program Files\Docker\Docker\frontend\Docker Desktop.exe` (Electron, `--name=dashboard`).
Neither exposes an AppUserModelID another process can read: `SHGetPropertyStoreForWindow` +
`PKEY_AppUserModel_ID` returns S_OK with VT_EMPTY, and `GetApplicationUserModelId` returns 15703
(APPMODEL_ERROR_NO_APPLICATION) for both processes; the probe was validated against a packaged app that does
return one. The layout is neither Squirrel (`app-<version>`) nor MSIX, so none of the identity, same-install or
exe-name passes in `DockModelBuilder.FindPinnedSlot` matched and the window became an unpinned group after the
pinned slots.

*Fix:* `AppPathMatch.IsSameInstall` now also matches the launcher-stub layout — a same-file-named executable in
a direct child folder of the target's folder, with the shared folder required to be a real directory (never a
drive root). The rule is one-way, the process below the target; the reverse layout (launcher in a subfolder, UI
at the root) needs no rule, because such a pin is a path pin whose root-UI window already matches by identity or
by the exe-name fallback. `DockModelBuilder` is unchanged; its gate (a named-AUMID pin only takes a window with
no AUMID of its own) still keeps browser-PWA windows separate.

*Deliberately flipped guardrail:* the 632e8ac test `IsSameInstall_ProcessInASubfolderThatIsNotSquirrel_IsFalse`
(`...\Foo\bin\Foo.exe` against `...\Foo\Foo.exe`) asserted exactly the shape that is now true. No documented
real-world app stood behind it (spec 0006 describes only the Squirrel rule), so it was replaced by the
two-folders-down case `IsSameInstall_ProcessInAGrandchildFolder_IsFalse`.

*Accepted trade-off:* a different product installed one level inside a pinned app's folder with the same binary
name now groups with that pin (for example `...\GitHubDesktop\app-foo\GitHubDesktop.exe` joins the stub pin).
Windows that report their own AUMID are still exempt for named-AUMID pins. The real-directory guard covers drive
roots and folder-less relative paths only: a UNC share root (`\\server\share`) is not guarded, so a same-named
exe in a child folder of a share would also pair — accepted because Start-menu pins resolve through
`System.Link.TargetParsingPath`, which is almost always local.

*Fix direction (not done, out of scope):* `AppCatalog.FindForWindow` falls back to the Squirrel stub only, so an
**unpinned** Docker dashboard still gets its name and icon from the `frontend\` exe rather than the catalogue
entry. Extending that fallback to the launcher-child layout would be a small, separate change.
