# Hand-off: where WinGnome work stands (2026-10-09)

This is for the next agent picking up WinGnome. Read it all before touching code. Then read, in order:
[AGENTS.md](../AGENTS.md) (process, binding), [docs/PLAN.md](PLAN.md) (architecture, conventions, Core API),
[README.md](../README.md) and [docs/KNOWN_ISSUES.md](KNOWN_ISSUES.md).

WinGnome is a WPF (.NET 8, `net8.0-windows10.0.19041.0`) GNOME-style shell companion for Windows 11. It hides the
taskbar, writes HKCU registry values, hooks the keyboard and registers AppBars. A bug can leave the user's desktop
broken, so the safety rules below are not optional.

---

## 1. Non-negotiable rules (summary of AGENTS.md plus lessons from this session)

1. **Never push, open PRs or touch remotes.** Commit locally. The user pushes.
2. **Plan first.** All the remaining work already has agreed specs (section 4). Follow them; if the code proves a
   spec wrong, follow the code and update the spec in the same commit.
3. **Logic in `WinGnome.Core` with xUnit tests**, app layer thin. Every new test must be seen **failing**: after it
   passes, temporarily break the code under test, run, confirm red, restore. Say which tests you saw red.
4. **Definition of done per change:** `dotnet build -c Release -warnaserror` clean, `dotnet test` green,
   `dotnet run --project src/WinGnome -c Release -- --selftest --safe --settings-dir <scratch>\selftest` exits 0,
   plus the spec's manual checks. Report honestly what was and wasn't checked.
5. **The user's everyday WinGnome is always running** (`publish\WinGnome.exe`, settings in `%APPDATA%\WinGnome`).
   Never kill it, never write to `%APPDATA%\WinGnome`. Ask the user to quit it (quick settings → Quit WinGnome)
   when a test needs the tray or the real taskbar, and restart it afterwards with
   `explorer.exe C:\Users\samhe\Documents\GitHub\WinGnome\publish\WinGnome.exe`.
6. **Test instances:** always `--settings-dir <scratch folder>`. Use `--safe` unless the test needs writes. In
   the test profile's `settings.json` turn off every feature you aren't testing, e.g.
   `{"Dock":{"Enabled":false},"WindowButtons":{"Enabled":false}}`. **Any non-safe test instance must also have
   `{"General":{"HideWindowsTaskbar":false,"NativeTaskbarAutoHide":false}}`**, otherwise it fights the everyday
   instance over the taskbar (this happened). Quit instances gracefully (`taskkill /PID <pid>`, never `/f`) unless
   the test is about force-kill, and only ever your own PIDs.
7. **Never run anything while the user is doing a live test session** (an agent did, and it corrupted the
   measurements).
8. **Sandboxed shells see a private copy of the registry and `%APPDATA%`** (KI-010). Launch anything whose writes
   must reach the real user profile through `explorer.exe <file.cmd>`, and read the real log the same way
   (a `.cmd` that copies `%APPDATA%\WinGnome\wingnome.log` to your scratch folder).
9. **Never restart Explorer, change display settings, write HKLM, uninstall anything, or leave a user setting
   changed.** Live write tests are toggle → read back → restore the exact original (delete values that were absent).
10. **Independent review before merge:** every non-trivial branch gets a review by a different agent/model than
    the author, checking AGENTS.md §4, races, leaks, restore paths and test quality. Fix or log findings.
11. Commit messages: short imperative summary ≤72 chars, blank line, the why. One logical change per commit.

Useful: `tools/Get-WorkAreas.ps1` prints each monitor's bounds and work area in physical pixels and whether the
Windows taskbar is visible. Use it before and after every AppBar/taskbar test.

User's machine: Windows 11 build 26200. Primary laptop panel 2560×1600 physical at 125 % at (0,0); a second
3440×1440 monitor at 100 % placed **above** the primary at (−447,−1440), negative coordinates. Print spooler is
disabled (no printers). Pointer uses a custom colour (cursor-size row is disabled by design).

---

## 2. State of `main` (build clean, 2929 tests green; `publish\WinGnome.exe` is built from it)

Merged this session:
- **Accent colour fix** (bdd4aa5): WinGnome's palette follows Windows' accent (DWM `AccentColor`), `AccentPalette`
  in Core.
- **Specs 0010, 0016–0020** written, advisor-reviewed and agreed with the user (406b081, fb1d5cc).
- **Spec 0020 native settings panels, all eight**: WP0 groundwork (1f0c26b), Printers + Removable Media (bc37621),
  Accessibility (826ee86; high contrast is read-only with a link until its spike passes), Notifications + Privacy
  (070753f), Region & Language + Windows Update (85fc5ba), Apps + Startup apps (56dc6ed).

## 3. Work in flight (branches, not merged)

Each lives in a git worktree under `.claude/worktrees/agent-<id>`, branch `worktree-agent-<id>`.

### 3a. Spec 0010 multi-monitor — branch `worktree-agent-ae7ed8eca306d9c7d` (HEAD 97a2538 or later) — NOT merge-ready

Implemented and reviewed (all packages, hot corner per the user's decision: primary's corner always on, with an
8×8 px dwell box where another monitor borders it). Passing: bars and docks on both monitors at their own DPI,
tray click/hover on both bars, one popup at a time, crash / `taskkill /f` / `--restore-taskbar` restore work areas.

**Blocking bug found in live sessions with the user:** AppBar strips are not reliably reserved.
- After start with the taskbar hidden (non-safe, the user's real settings), neither bar's strip appears in the work
  area. Re-registering (REMOVE/NEW/QUERYPOS/SETPOS) at +2 s and +10 s did **not** fix it.
- The **old build on main has the same delay**: Explorer applied the strip ~35 s after docking, with no WinGnome
  activity at that moment. Re-registering may be restarting that delay.
- Unplugging the secondary made Explorer reset the primary's work area to full while our bar stayed registered.
- Session logs: `C:\Users\samhe\AppData\Local\Temp\claude\C--Users-samhe-Documents-GitHub-WinGnome\79f95df3-9ca6-4c97-a5d3-e2b7b006d26c\scratchpad\mm-session\profile\wingnome*.log`
  (session1/session2/current) and `everyday.log` (old build timing). The test profile used is `...\mm-session\profile`.

When this session ended, the branch author was investigating (how `TaskbarController` hides the taskbar, whether
the tray host's `Shell_TrayWnd`-class window intercepts `SHAppBarMessage`, Explorer's own recompute delay) and an
advisor was evaluating **spec 0010 "outcome B"**: WinGnome sets the work area itself with documented
`SystemParametersInfo(SPI_SETWORKAREA)` per monitor, recording originals first and restoring on exit, crash and
next start, either alone or as a fallback when the AppBar strip isn't applied within a few seconds. Check the
branch for commits after 97a2538 and the spec's Risks/implementation notes before continuing.

To finish: fix the reservation (with a cap and back-off, never a re-register loop), then repeat the **live session
with the user** (they must quit the everyday instance first; nothing else may run):
1. Start the branch build non-safe with a copy of the user's settings (`...\mm-session\start-test.cmd`).
2. Within ~10 s, `tools/Get-WorkAreas.ps1` must show DISPLAY1 work top = 40 and DISPLAY2 work top = −1408.
3. User unplugs the upper monitor → DISPLAY1 keeps top = 40. Replug (Win+P → Extend if Windows loses it) → both
   strips back, no doubled strips (top must be exactly 40 / −1408).
4. Then scale change, primary swap, sleep/resume. Explorer restart only with the user's OK.
5. Quit the test build, confirm work areas are back to the baseline (taskbar visible: DISPLAY1 bottom 1540,
   DISPLAY2 bottom −48), restart the everyday instance, confirm DISPLAY1 top = 40 (may take ~35 s, see above).
Then an independent review of the new commits, merge to main with `--no-ff`, assign KI-098 to "MonitorKeyOf can
map a recycled HMONITOR for ≤250 ms" (spec 0010 Risk 10).

### 3b. Spec 0020 docs — merged

WP9 docs integration is merged (KI-085..092, PLAN.md, README, spec status).

Other `worktree-agent-*` branches are from earlier sessions and already merged; ignore them.

---

## 4. Queue, in order

Specs are agreed; each lists its work packages, safety-critical parts and acceptance criteria. Do one spec (or
phase) per branch, review, merge, then the next. Specs 0017/0018/0020-dependent work must be based on `main`
**after** 0010 is merged (they touch the same services).

1. **Finish 0010** (section 3a).
2. **Dock dark ring (bug, user-reported):** a darker rim around the whole dock body on both monitors. Cause: the
   blur backdrop window is inset (`Core/Geometry/BackdropPlacement.Compute`) because DWM's accent blur only rounds
   with fixed 4/8 DIP radii, leaving an unblurred rim between the backdrop and the body's larger rounded outline
   (`Features/Dock/DockWindow.xaml.cs` `SyncBackdrop`, `Interop/BlurBackdrop.cs`). Spike whether
   `SetWindowRgn` or `DWMWA_SYSTEMBACKDROP_TYPE` on a region-clipped window now clips the blur exactly on build
   26200; otherwise reduce the rim (e.g. tint the rim to match, or snap the default radius to DWM's). Also
   affects the top bar's floating mode. Check light and dark.
3. **Windows Security tray icon ignores clicks** (user-reported; WinGnome delivers the callback without error, log
   shows nothing). Investigate `Features/TopBar/Tray/TrayViewModel.Deliver` / `TrayCallback` for
   `SecurityHealthSystray` (version-4 icon with GUID; may need `NIN_SELECT`/`WM_CONTEXTMENU` with the right
   anchor, or `AllowSetForegroundWindow` for its process). Check if the everyday build has the same problem.
   Add a KNOWN_ISSUES entry if unfixable.
4. **Panel load failures leave rows busy** (KI-089, KI-091): when a panel's background read throws,
   `SystemPanelViewModel.LoadAsync` never calls `show`, so Printers, Removable Media and the Apps list keep
   `IsBusy` set until reopened. Add a failure callback to `LoadAsync` (small, shared base) and use it.
5. **Spec 0016 GNOME window management**, phased: v1 (Alt+Tab app switcher, Super+A/S/N, Super+Page Up/Down,
   tiling halves/quarters/expand with gaps), then v1.1 (layouts, Ctrl+drag zones, neighbour resize), then v1.2
   (Super+drag move/resize). WP0 spike first (which Win+ combos can be `RegisterHotKey`ed). Safety-critical:
   the shared low-level keyboard hook host — never block in the callback, swallow/unswallow symmetry, fail open.
   `Tiling.Enabled` defaults to true only if the 100 %/150 % cross-DPI QA passes.
6. **Spec 0017 OSD + Night Light / Do Not Disturb** (spike first; OSD on every monitor per the user). When the DND
   service exists, wire it into the Notifications panel's hidden DND row (KI-085).
7. **Spec 0018 overview**: workspace strip, window-to-workspace move (undocumented COM, strict build/UBR table,
   crash marker), app folders, dock pin/unpin by drag, file search. Results order Apps, Windows, Files.
8. **Spec 0019 connectivity panels**, three parts: (a) Win+I and dock Settings pin redirect (needs 0016's hook
   host), (b) Wi-Fi, (c) Network + Bluetooth.

## 5. Things that need the user (batch them into one message)

- 0010 live session (above).
- Accessibility high-contrast spike (steps in the spec 0020 notes / KI entry) and, if they want, cursor size
  (needs the white pointer style).
- AutoPlay "Memory card": change it once in Windows Settings while you diff
  `HKCU\...\Explorer\AutoplayHandlers` to confirm the registry location (the panel uses `ShowPicturesOnArrival`;
  Windows may use `CameraAlternate\ShowPicturesOnArrival`).
- Notifications: does a switched-off app stop showing toasts without sign-out? Privacy: does the Camera app lose
  access when the switch is off?

## 6. How this session worked (copy what helped)

- One spec per area → independent advisor review of the spec → revise → user decisions batched with recommended
  defaults → implement per work package in its own worktree/branch → independent review → fix → merge `--no-ff`
  → build and test `main` after every merge.
- Parallel branches only when they own separate folders. `PanelRegistry.cs` / `PanelResources.xaml` conflicts are
  always "keep every entry".
- Merge conflicts and shared files: check the spec's work-package table for ownership before editing.
