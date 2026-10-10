# Hand-off: where WinGnome work stands (2026-10-10)

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

## 2. State of `main` (build clean; `publish\WinGnome.exe` is still the e85e68e build until it is republished)

`main` is at the KI-102 merge **(this merge)**, on top of **e85e68e**. Merged 2026-10-10:

- **KI-102 work-area fight fix + diagnostic logging** (f402e2e, 0300eed, 73a3c5d, d9f85da, f9b92a6; design in
  spec 0010's addendum): Core's `WorkAreaFightDetector` makes direct work-area sets from the fourth application
  on a monitor inside 10 minutes silent (no `SPIF_SENDCHANGE`), starving the broadcast→recompute loop behind the
  mid-run fight; `StripRecovery`'s terminal give-up becomes a five-minute cool-down after which checks resume (a
  re-dock or the strip being reserved ends it at once); the give-up wordings are corrected; and bundled
  diagnostics log the taskbar re-hide chain (B1), the peek chain (B2), tray deliveries incl. the recorded NIM
  version (B3) and AppBar notification names (B4). 12 new/updated Core tests, the regression test seen red
  against the unfixed Core, all mutations run; independent review APPROVE-WITH-NITS, nits resolved. Live-verified
  2026-10-10 on build 26200: criteria 4–10 + 15 PASS, 12 PARTIAL (the burst/backoff path needs an unlocked
  workstation), 13/14 DEFERRED (need real clicks) — the last three ride on the everyday build after republish.
  KNOWN_ISSUES: KI-102 resolved, KI-103 added (silent writes leave already-maximised windows oversized until the
  next broadcast), KI-099's limits bullet updated; the next free ID is KI-104.

Merged 2026-10-09 (at **e85e68e**):

- **Spec 0010 multi-monitor** (e85e68e): a full top bar on every monitor, docks on the primary by default or on all
  as an option, per-monitor focused app, guarded hot corners, `DisplayLayoutService` — and the **`SPI_SETWORKAREA`
  work-area fallback** (outcome B): a bar whose granted strip is still missing 1.5 s after a check sets that
  monitor's work area itself, records it in `workareas.state` and recovers on exit, crash, force-kill and next start
  (KI-098, KI-099 — both carry the measured verification record).
- **Panel load-failure fix** (e3fe726): `LoadAsync`'s `onFailed` callback plus Core's `PanelLoadGate`, so a read
  that throws clears the panel's busy state (KI-091 — note its *corrected* Printers symptom claim: with the spooler
  merely disabled, `PrinterService.Read` never throws and the panel was never stuck).
- **Launcher-stub dock grouping fix** (c42d6dc, KI-100): `AppPathMatch.IsSameInstall` matches the launcher-stub
  layout, so a pinned Docker Desktop groups its dashboard window instead of showing a second, unpinned icon.

QA at that merge: `dotnet build -c Release -warnaserror` clean; **3157 tests green**; `--selftest --safe` exits 0
(re-run on merged main). The everyday `publish\WinGnome.exe`, built from e85e68e, is **running on the user's
machine** (started 20:35 through `explorer.exe`, being watched for idle stability); the previous exe (built from
3d65d76) is backed up at `C:\Users\samhe\AppData\Local\Temp\opencode\publish-backup-20261009\`. Its first start
verified spec 0010's B6 live: both strips reserved ~4.3 s after start through the fallback, one direct set per
monitor, marker correct (details in KI-099).

## 3. Work in flight

- Branch **`logo-menu-spec`** (spec 0021, logo menu items + custom white logo): implemented and advisor-reviewed
  at spec level (3187 tests green), but **not** code-reviewed and **not** live-QA'd yet. Its KNOWN_ISSUES entry
  claims **KI-103** — renumber it to **KI-104** (next free KI-105) when that branch merges: `main` now uses
  KI-103 for the silent-write degradation (KI-102's fix).

## 4. Queue, in order

Done since the 2026-10-09 hand-off: the **KI-102 work-area fight fix + diagnostic logging** (section 2 above) —
fight detector, cool-down, silent writes and the B1–B4 log lines; live-verified except criteria 12-burst/13/14,
which ride on the everyday build. Done before that: former item 1 (spec 0010, section 2), former item 4 (panel
load failures, KI-091) and — reported by the user in between — the Docker Desktop grouping bug (KI-100). The
remaining spec work (0016–0019) can now be based on current `main`: they touch `DockInstance` and `TrayModel`,
which 0010 refactored, so anything started before the merge would have to be rebased.

1. **Dock dark ring (bug, user-reported):** a darker rim around the whole dock body on both monitors. Cause: the
   blur backdrop window is inset (`Core/Geometry/BackdropPlacement.Compute`) because DWM's accent blur only rounds
   with fixed 4/8 DIP radii, leaving an unblurred rim between the backdrop and the body's larger rounded outline
   (`Features/Dock/DockWindow.xaml.cs` `SyncBackdrop`, `Interop/BlurBackdrop.cs`). Spike whether
   `SetWindowRgn` or `DWMWA_SYSTEMBACKDROP_TYPE` on a region-clipped window now clips the blur exactly on build
   26200; otherwise reduce the rim (e.g. tint the rim to match, or snap the default radius to DWM's). Also
   affects the top bar's floating mode. Check light and dark.
2. **Windows Security tray icon ignores clicks** (user-reported; WinGnome delivers the callback without error, log
   shows nothing). Investigate `Features/TopBar/Tray/TrayViewModel.Deliver` / `TrayCallback` for
   `SecurityHealthSystray` (version-4 icon with GUID; may need `NIN_SELECT`/`WM_CONTEXTMENU` with the right
   anchor, or `AllowSetForegroundWindow` for its process). Check if the everyday build has the same problem.
   Add a KNOWN_ISSUES entry if unfixable. Since the KI-102 merge the B3 tray-delivery log lines answer the
   version question on the everyday build: after the user clicks the icon, check the log — `version 0` on its
   delivery line means we missed its `NIM_SETVERSION` (and the callback is encoded in legacy format the app
   ignores).
3. **Spec 0016 GNOME window management**, phased: v1 (Alt+Tab app switcher, Super+A/S/N, Super+Page Up/Down,
   tiling halves/quarters/expand with gaps), then v1.1 (layouts, Ctrl+drag zones, neighbour resize), then v1.2
   (Super+drag move/resize). WP0 spike first (which Win+ combos can be `RegisterHotKey`ed). Safety-critical:
   the shared low-level keyboard hook host — never block in the callback, swallow/unswallow symmetry, fail open.
   `Tiling.Enabled` defaults to true only if the 100 %/150 % cross-DPI QA passes.
4. **Spec 0017 OSD + Night Light / Do Not Disturb** (spike first; OSD on every monitor per the user). When the DND
   service exists, wire it into the Notifications panel's hidden DND row (KI-085).
5. **Spec 0018 overview**: workspace strip, window-to-workspace move (undocumented COM, strict build/UBR table,
   crash marker), app folders, dock pin/unpin by drag, file search. Results order Apps, Windows, Files.
6. **Spec 0019 connectivity panels**, three parts: (a) Win+I and dock Settings pin redirect (needs 0016's hook
   host), (b) Wi-Fi, (c) Network + Bluetooth.

## 5. Things that need the user (batch them into one message)

- **Spec 0010 live session** for the checks that still need someone at the machine: B7 unplug/replug, B11 scale
  change / primary swap / sleep-resume, B12 native taskbar mode, the stacked-records unwind on one monitor, the
  corrupt-marker repair with the taskbar hidden, the crash path and the TbExp hide-order experiment. A
  step-by-step runbook (who does what, expected work areas, pass/fail per step) is ready at
  `C:\Users\samhe\AppData\Local\Temp\opencode\mm-session\RUNBOOK.md`. It needs the everyday instance quit, so
  agree a window with the user first — and don't forget to restart it through `explorer.exe` afterwards.
- Accessibility high-contrast spike (steps in the spec 0020 notes / KI entry) and, if they want, cursor size
  (needs the white pointer style).
- AutoPlay "Memory card": change it once in Windows Settings while you diff
  `HKCU\...\Explorer\AutoplayHandlers` to confirm the registry location (the panel uses `ShowPicturesOnArrival`;
  Windows may use `CameraAlternate\ShowPicturesOnArrival`).
- Notifications: does a switched-off app stop showing toasts without sign-out? Privacy: does the Camera app lose
  access when the switch is off?
- **New (KI-100):** with Docker Desktop running, look at the dock and confirm there is one grouped icon, with the
  pin's own label and icon and a running dot — not a second, generic one.
- **New (spec 0010 behaviour change):** the top bar now appears on **every** monitor by default
  (`TopBar.Monitors = All`) and the dock on the main display only (`Dock.Monitors = Primary`). Ask the user to
  confirm both look right; both are settings (Top Bar → "Show on", Dock → "Show on").
- **New (KI-102 criteria 13/14):** after republishing the everyday build from merged `main`, click the quick
  settings "System tray" tile and a couple of tray icons — including Windows Security — once, so the peek-chain
  and tray-delivery lines land in the real log (the Windows Security line's `version` field answers queue item 2).
- **New (KI-102 criterion 12 field data):** watch for random taskbar flashes and correlate them with the new
  "Explorer showed taskbar window 0x…; re-hiding in … ms" lines in the log; a sustained burst should also show
  the backoff WARN and the throttled keys' suppressed counts.

## 6. How this session worked (copy what helped)

- One spec per area → independent advisor review of the spec → revise → user decisions batched with recommended
  defaults → implement per work package in its own worktree/branch → independent review → fix → merge `--no-ff`
  → build and test `main` after every merge.
- Parallel branches only when they own separate folders. `PanelRegistry.cs` / `PanelResources.xaml` conflicts are
  always "keep every entry".
- Merge conflicts and shared files: check the spec's work-package table for ownership before editing.
