# WinGnome — implementation plan

This is the engineering plan. The user-facing overview lives in the repo [README](../README.md).

## Goals

1. **Dock** replaces the taskbar's app area (GNOME Dash-to-Dock / macOS style).
2. **Top bar**: Activities button, workspace dots, focused app name, centred clock and calendar, system indicators, quick-settings and power menu.
3. **Traffic-light window buttons**: round minimise/maximise/close buttons drawn over other apps' title bars, with colour presets and custom colours.
4. **Streamline extras**: Activities overview (live window thumbnails and app search), hot corner, Super key opens the overview, centring new windows, focus-follows-mouse, and reversible "streamline" registry tweaks.

## Architecture

```
src/WinGnome.Core        net8.0, pure logic, no Win32 calls. 100% unit-testable.
src/WinGnome             net8.0-windows WPF app. Interop, services, UI.
tests/WinGnome.Core.Tests xUnit tests for Core.
```

All Win32 work lives in the app project, and every decision that can be expressed as pure logic
(layout maths, filtering, state machines, parsing, tweak bookkeeping) lives in Core so it can be tested.

### Conventions (all agents)

- C# 12, nullable enabled, file-scoped namespaces, `sealed` by default, 4-space indent.
- No new NuGet packages in the app without a strong reason. The only extra packages are in the test project.
- P/Invoke goes in `src/WinGnome/Interop/NativeMethods.<Area>.cs` as `internal static partial class NativeMethods`,
  using `[LibraryImport]` where possible (with `[DllImport]` for COM or struct-marshalling cases). Never redeclare
  something that already exists in another partial file; check `Interop/` first.
- Every Win32 failure is non-fatal: log through `Log.Warn(...)` and degrade gracefully. WinGnome must never
  crash explorer or leave the taskbar hidden.
- UI threading: all windows live on the WPF dispatcher thread. WinEvent hooks are out-of-context and arrive on that thread.
  The exception is `TrayHost`, whose hidden tray window runs its own Win32 message loop on a dedicated thread.
- DPI: the process is Per-Monitor-V2 aware. Screen geometry from Win32 is in **physical pixels** (`PixelRect`).
  Convert with `VisualTreeHelper.GetDpi(visual).DpiScaleX` or `NativeMethods.GetDpiForWindow`/`GetDpiForMonitor`.
- Features implement `IFeature` (`Start(AppSettings)`, `ApplySettings(AppSettings)`, `Dispose()`) and are discovered
  automatically, ordered by `[FeatureOrder]`.
- Settings are read from `SettingsService.Current` and changes arrive through `SettingsService.Changed`.
- Footprint: WinGnome runs all day, so idle cost matters most. WPF renders in software process-wide (no Direct3D
  device; see `App.OnStartup`). Create per-window WPF surfaces only when they will be shown, size hidden windows
  down, avoid per-frame work (`CompositionTarget.Rendering`, full-screen opacity animations) outside short
  animations, and prefer events or one-shot timers to polling.

## Core API (WinGnome.Core)

| Namespace | Type | Purpose |
|---|---|---|
| Settings | `AppSettings`, `SettingsStore`, `SettingsSerializer` | Persisted config, JSON in `%APPDATA%\WinGnome` |
| Settings | `SettingsActivationRequest` | Format and validation of the `settings-request.txt` a second launch leaves for the running instance (`--settings-panel`) |
| Theming | `HexColor`, `TrafficLightPalette`, `AccentPalette` | Colour parsing, presets → `TrafficLightColors`, the accent from Windows (DWM `AccentColor`) and a readable label colour on it |
| Input | `Hotkey`, `SuperKeyStateMachine`, `HotCornerDetector` | Hotkey parsing, the "Win alone" detector, corner dwell |
| Geometry | `PixelRect`, `LayoutRect`, `LayoutSize`, `BackdropPlacement` | Integer screen rectangles, double layout rectangles, blur backdrop inset under rounded bodies |
| Windows | `WindowInfo`, `WindowFilter`, `AppIdentity`, `AppPathMatch`, `CaptionButtonLayout`, `CaptionButtonHitTest`, `CaptionButtonAccessibility`, `CaptionButtonGeometry`, `CaptionDecorationRules`, `CaptionHitTestProbe`, `CaptionHoleProbe`, `CaptionMaxAnchorProbe`, `ProbedClickCheck`, `CaptionProbeThrottle` | Alt-tab filtering, app grouping keys, matching a process to a shortcut's install (Squirrel `app-<version>` folders, generated AUMIDs), traffic-light geometry, hit-testing, accessible names and keyboard movement (overlays and WinGnome's own header bars via `ComputeForHeaderBar`/`FindInHeaderBar`), finding custom title bars' buttons from hit-test samples (button codes, HTCLIENT holes of a profiled width for web-drawn buttons, or a maximise zone between client zones in Windows App SDK windows), the click guard and probe rate limits for them |
| Dock | `DockModelBuilder`, `DockApp`, `RunningWindow`, `DockLayout`, `DockClickPlanner` | Dock items, geometry, magnification, click behaviour |
| Overview | `OverviewLayout`, `SelectionNavigator`, `OverviewTransition`, `ThumbnailTrack`, `OverviewTransitionState` | Arranges window thumbnails in a grid that preserves aspect ratios, keyboard selection, open/close animation (ease-out-quad, retargetable tracks, dim alpha, the reversible open/close state machine) |
| Search | `FuzzyMatcher` | Ranks apps and windows by a query |
| Shell | `KnownFolderPath`, `LaunchPlanner`, `LaunchRequest`, `LaunchModifiers`, `AppHost` | Resolves `{GUID}\path` AppsFolder parsing names; classifies launch ids and decides elevation (Ctrl+Shift or a pin's `RunAsAdministrator`; desktop and full-trust packaged apps, never UWP apps or URIs) |
| Shell | `ShellStartDecision`, `ShellStartInputs`, `CrashHistory`, `RestartBackoff`, `TrialConfirmation` | Shell-mode bootstrap decisions (spec 0013), unwired: fail-closed start/Explorer/give-up outcome, shell-value removal, trial handling, crash-loop detection (3 in 2 min or 5 in 30 min), restart backoff (1 s, 3 s, 10 s), trial confirmation (explicit and 5 min uptime) |
| Shell | `StartupRunner`, `StartupEntry`, `StartupApprovedSet` | Ordered sign-in launch plan from Run/RunOnce/Startup folders: StartupApproved flags, RunOnce `!`/`*` prefixes, own-entry skip, command-line parsing (unwired) |
| Shell | `AppBarNegotiator` | AppBar protocol geometry: QUERYPOS/SETPOS/REMOVE, one auto-hide bar per edge per monitor, per-monitor work areas in integer pixels (unwired) |
| Shell | `ShellHotkeyMap` | Win+ hotkey defaults for shell mode (E, R, I, D, Shift+S, arrows), lookup by modifiers and key, conflicts with user hotkeys (unwired) |
| TopBar | `ClockFormatter`, `BatteryStatus`, `BrightnessScale`, `BrightnessWheel`, `WriteCoalescer`, `BarMetrics`, `TrayIconPlacement` | GNOME-style clock text, battery state and whether to poll it, brightness levels, wheel steps and coalesced writes, whole-device-pixel text, symbolic icon and hover-pill sizes, how a tray icon bitmap is drawn in its slot (never upscaled by a non-integer factor) |
| Tray | `ShellTrayData`, `TrayIconRegistry`, `TrayFrontCheckSchedule` | Tray-host message parsing, icon list, how often the host re-checks it is in front |
| Collections | `LruCache`, `DeadlineSchedule` | Bounded least-recently-used cache; one deadline per key for debouncing on a single timer |
| Workspaces | `VirtualDesktopState` | Parses Explorer's virtual-desktop registry blobs |
| ControlCenter | `SettingsPanelCatalog` (incl. `DirectLinkFor`), `SettingsPanel`, `PanelIds`, `PlannedCommand`, `DisplayArrangement`, `KeepChangesCountdown`, `DisplayRevertRecord`, `DisplayChangeFlow`, `DisplayModes`, `PowerTimeouts`, `TimeZoneList`, `InputTuning`, `SystemInfoText` | The GNOME Settings style app (spec 0015): sidebar panels, groups, links and search; display arrangement geometry (edge snapping, primary at the origin, preview fit), the 15-second keep-or-revert state machine, the crash-safe revert record and the order of apply, keep, revert and recovery; resolution and refresh choices; GNOME's power timeouts; time zone ordering and search; SystemParametersInfo value ranges; About and Sound text |
| Tweaks | `TweakCatalog`, `TweakDefinition`, `RegistryChange`, `IRegistryStore`, `TweakEngine`, `TweakBackup`, `AccentColorChanges` | Reversible HKCU tweaks, accent-colour registry values (also read back and handed to Windows by the Appearance panel) |

## App modules (WinGnome)

| Folder | Owner | Contents |
|---|---|---|
| `Infrastructure/` | lead | `Log`, `IFeature`, `SettingsService`, `ObservableObject`, `RelayCommand`, `SessionRole` (one instance per session does a job, e.g. window buttons), theme resources |
| `Interop/` | shared | `NativeMethods.*.cs` partials, `WinEventHook`, `AppBar`, `BlurBackdrop` (blur window under the dock and top bar bodies) |
| `Services/WindowTracker.cs` | lead | Enumerates alt-tab windows, raises `WindowsChanged` and `ForegroundChanged`, plus raw location events |
| `Services/Apps/` | dock agent | `AppCatalog` (shell:AppsFolder), `IconProvider`, `AppLauncher` (in-process `ShellExecuteEx`, never `explorer.exe`), `ShellThread` (short-lived STA thread for blocking shell calls) |
| `Features/Dock/` | dock agent | Dock window, intellihide, trigger strip, context menu, `TaskbarController` |
| `Features/TopBar/` | top-bar agent | Top-bar AppBar, clock and calendar, indicators (network, volume, battery), quick settings, power menu, workspace dots, `TopBarFonts` (bundled Adwaita Sans or Segoe UI), `SymbolicIcon` (draws `Theme/SymbolicIcons.xaml` on whole pixels) |
| `Controls/TrafficLights/` | shared | `TrafficLightButtonsView`, its automation peers and `GlyphGeometry` (the circles, used by the overlay and header bars), `HeaderBarWindow` (WindowChrome header bar with round buttons and Snap Layouts for WinGnome's own windows) |
| `Features/WindowButtons/` | traffic-light agent | Overlay manager and per-window overlay, custom title bar probing and click guard |
| `Features/Overview/` | overview agent | Activities overview with DWM thumbnails and search, hot corner, Super key hook, global hotkey, centring new windows, focus-follows-mouse |
| `Features/Settings/` | settings agent | Settings window as a GNOME Settings style app (header bar, searchable grouped sidebar, links to Windows Settings, WinGnome's pages), tweaks page with `RegistryStore` (HKCU), start-with-Windows |
| `Features/Settings/Panels/` | settings agent | Native system panels (Displays, Sound, Power, Mouse, Keyboard, Appearance, Multitasking, Date & Time, About): one folder per panel with its view, view model and service; `SystemPanelViewModel` (open/close lifecycle, safe-mode read-only, problem banner and Windows Settings link) and `SystemSettingWriter` (ordered off-UI-thread writes, none in safe mode); `PanelRegistry` and `PanelResources.xaml` (one line per panel; a native catalogue entry without a page falls back to its link), `ShellLaunch` (System32 tools and watched uninstallers on `ShellThread`), `IndirectString` (spec 0020) |

## Safety rules

- Taskbar: restore on normal exit, on unhandled exceptions, on `SessionEnding`, and on `--restore-taskbar`.
- Registry tweaks touch **HKCU only**, back up the previous value (including "absent"), and can be reverted one by one or all at once.
- Never act on elevated windows (UIPI would block us anyway); skip them explicitly.
- Settings panels change Windows settings on purpose and don't back them up, except Displays: a change is validated and applied atomically for the session only (`SetDisplayConfig` without saving), the previous and new settings are written to `display-revert.json` first, and the change is reverted unless kept within 15 s (also after a crash, at the next start, if it is still showing). Safe mode never writes system settings.
- Command-line switches: `--settings-dir <path>` (isolated profile), `--safe` (no taskbar hiding, tweaks, hooks or system setting changes), `--selftest` (start every feature in safe mode, run for 5 s, exit 0 or 1), `--restore-taskbar`, `--settings` / `--settings-panel <id>`.

## QA plan

1. `dotnet build -warnaserror` clean, `dotnet test` green.
2. `WinGnome.exe --selftest --safe --settings-dir <scratch>` exits 0.
3. Visual smoke test: run with an isolated profile, take screenshots of the top bar, dock, traffic lights, overview and settings, then exit and confirm the taskbar and work area are restored.
4. An Opus code review of every module, with fixes applied before sign-off.
