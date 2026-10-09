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
| Theming | `HexColor`, `TrafficLightPalette` | Colour parsing, presets → `TrafficLightColors` |
| Input | `Hotkey`, `SuperKeyStateMachine`, `HotCornerDetector` | Hotkey parsing, the "Win alone" detector, corner dwell |
| Geometry | `PixelRect`, `LayoutRect`, `LayoutSize`, `BackdropPlacement` | Integer screen rectangles, double layout rectangles, blur backdrop inset under rounded bodies |
| Windows | `WindowInfo`, `WindowFilter`, `AppIdentity`, `AppPathMatch`, `CaptionButtonLayout`, `CaptionButtonGeometry`, `CaptionHitTestProbe` | Alt-tab filtering, app grouping keys, matching a process to a shortcut's install (Squirrel `app-<version>` folders, generated AUMIDs), traffic-light geometry, finding custom title bars' buttons from hit-test samples |
| Dock | `DockModelBuilder`, `DockApp`, `RunningWindow`, `DockLayout`, `DockClickPlanner` | Dock items, geometry, magnification, click behaviour |
| Overview | `OverviewLayout` | Arranges window thumbnails in a grid that preserves aspect ratios |
| Search | `FuzzyMatcher` | Ranks apps and windows by a query |
| Shell | `KnownFolderPath` | Resolves `{GUID}\path` AppsFolder parsing names |
| TopBar | `ClockFormatter`, `BatteryStatus`, `BrightnessScale`, `BrightnessWheel`, `WriteCoalescer` | GNOME-style clock text, battery state and whether to poll it, brightness levels, wheel steps and coalesced writes |
| Tray | `ShellTrayData`, `TrayIconRegistry`, `TrayFrontCheckSchedule` | Tray-host message parsing, icon list, how often the host re-checks it is in front |
| Collections | `LruCache` | Bounded least-recently-used cache |
| Workspaces | `VirtualDesktopState` | Parses Explorer's virtual-desktop registry blobs |
| Tweaks | `TweakCatalog`, `TweakDefinition`, `RegistryChange`, `IRegistryStore`, `TweakEngine`, `TweakBackup`, `AccentColorChanges` | Reversible HKCU tweaks, accent-colour registry values |

## App modules (WinGnome)

| Folder | Owner | Contents |
|---|---|---|
| `Infrastructure/` | lead | `Log`, `IFeature`, `SettingsService`, `ObservableObject`, `RelayCommand`, theme resources |
| `Interop/` | shared | `NativeMethods.*.cs` partials, `WinEventHook`, `AppBar`, `BlurBackdrop` (blur window under the dock and top bar bodies) |
| `Services/WindowTracker.cs` | lead | Enumerates alt-tab windows, raises `WindowsChanged` and `ForegroundChanged`, plus raw location events |
| `Services/Apps/` | dock agent | `AppCatalog` (shell:AppsFolder), `IconProvider`, `AppLauncher` |
| `Features/Dock/` | dock agent | Dock window, intellihide, trigger strip, context menu, `TaskbarController` |
| `Features/TopBar/` | top-bar agent | Top-bar AppBar, clock and calendar, indicators (network, volume, battery), quick settings, power menu, workspace dots |
| `Features/WindowButtons/` | traffic-light agent | Overlay manager and per-window overlay |
| `Features/Overview/` | overview agent | Activities overview with DWM thumbnails and search, hot corner, Super key hook, global hotkey, centring new windows, focus-follows-mouse |
| `Features/Settings/` | settings agent | Settings window (all pages), tweaks page with `RegistryStore` (HKCU), start-with-Windows |

## Safety rules

- Taskbar: restore on normal exit, on unhandled exceptions, on `SessionEnding`, and on `--restore-taskbar`.
- Registry tweaks touch **HKCU only**, back up the previous value (including "absent"), and can be reverted one by one or all at once.
- Never act on elevated windows (UIPI would block us anyway); skip them explicitly.
- Command-line switches: `--settings-dir <path>` (isolated profile), `--safe` (no taskbar hiding, tweaks or hooks), `--selftest` (start every feature in safe mode, run for 5 s, exit 0 or 1), `--restore-taskbar`.

## QA plan

1. `dotnet build -warnaserror` clean, `dotnet test` green.
2. `WinGnome.exe --selftest --safe --settings-dir <scratch>` exits 0.
3. Visual smoke test: run with an isolated profile, take screenshots of the top bar, dock, traffic lights, overview and settings, then exit and confirm the taskbar and work area are restored.
4. An Opus code review of every module, with fixes applied before sign-off.
