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

## 2. State of `main` (build clean, 3480 tests green; `publish\WinGnome.exe` rebuilt from main 2026-10-10 evening)

`main` is at **b1b3053** (pushed; origin in sync). Merged 2026-10-10, in order:

- **KI-102 work-area fight fix + diagnostic logging** (merge c788f1c; design in spec 0010's addendum): Core's
  `WorkAreaFightDetector` makes direct work-area sets from the fourth application on a monitor inside 10 minutes
  silent (no `SPIF_SENDCHANGE`), starving the broadcast→recompute loop; `StripRecovery`'s terminal give-up became
  a five-minute cool-down carried on the bar's existing one-shot timer; bundled diagnostics log the taskbar
  re-hide chain (B1), the peek chain (B2), tray deliveries with the recorded NIM version (B3) and AppBar
  notification names (B4). Live-verified: criteria 4–10, 12 (single+burst), 15 PASS; 13/14 PASS via the logo QA
  session. KI-102 resolved; KI-103 (silent-write degradation) added.
- **Spec 0021 logo menu items + customisable white logo** (merge 8f84731): "Windows Settings…" (ms-settings:,
  Win+I hint kept) and "Control Panel…" (control.exe) in the logo menu; the logo button is customisable —
  built-in Foot/Star/Terminal marks or any image rendered as a silhouette in the bar's foreground colour
  (alpha mask, else inverted BT.601 luminance; Core `LogoSelection`/`LogoMaskRule`; settings `TopBar.Logo` +
  `LogoImagePath`, additive). Live UI QA: menu items, all four glyphs, custom alpha image, cancel-revert,
  gear-unchanged PASS; the opaque-JPEG, stray-pixel and missing-file edge cases were hand-tested by the user
  and remain manual items. KI-104 records the mask's limitations.
- **Spec 0022 tray start-up heal + callback learning + Security click fallback** (merges 355f004, b1b3053):
  the tray host re-broadcasts TaskbarCreated once 2 s after start (Core `TrayRebroadcastPolicy`), healing icons
  whose registration fell in the KI-019 front gap — live-proven for two apps; `TrayIconRegistry` can also LEARN
  a callback/version from twice-observed matching plausible unflagged wire fields (WM_USER..0xBFFF, versions
  0/3/4; flagged data always wins) for struct-reusing apps; and because SecurityHealthSystray does NEITHER
  (live-verified twice: ignores all broadcasts, zero-fills its structs), a built-in one-entry
  `TrayClickFallback` table launches `windowsdefender://` when its dead icon is clicked (rate-limited 750 ms
  per icon). Live-verified: left/right/double click each opened Windows Security, one launch per gesture,
  control icons unaffected. KI-105 (residual gaps), KI-106 (learning heuristic limits). Next free ID: KI-107.

Merged later on 2026-10-10 (Claude Opus 5.5 as PM, Sonnet authors, Opus reviews):

- **Taskbar flash fix** (merge 1653651): the fixed 5 s re-hide back-off (which left the taskbar visible at start-up,
  provoked by our own work-area broadcasts) is now Core `TaskbarRehidePolicy`: 50 ms (was 250 ms; a 0.3 s flash was still visible), then 750 ms from 5 shows in
  10 s, 3 s from 12; one show counted per scheduled re-hide (multi-monitor). Not yet field-confirmed: grep the
  everyday log for "re-hiding it less often" after the next sign-in.
- **Windows Update fixes** (merge a5e8d00): optional driver/browse-only updates listed separately instead of counted
  as waiting (Core `UpdateClassification`); restart detection adds the CBS/Auto Update registry keys
  (`PendingRestartSignals`); Restart/Power Off show "Install pending software updates" when the undocumented
  Orchestrator `ShutdownFlyoutOptions` is non-zero, using `InitiateShutdown(SHUTDOWN_INSTALL_UPDATES)` (KI-107; the
  install path is unverified live — check it the next time Start offers "Update and restart").
- **Spec 0019 Wi-Fi + Bluetooth panels** (merge 66bff78): Native Wifi API panel (list, connect incl. passwords,
  disconnect, forget; location-consent gated via `AppCapability("wiFiControl")`, no automatic gated call while
  consent is unasked) and Bluetooth panel (switch, paired devices, Remove Device). Network panel and part (a) stay
  deferred. All write paths unverified live — manual QA list in KI-109/KI-110. KI-108..111; next free KI-112.

Earlier QA at b1b3053: `dotnet build -c Release -warnaserror` clean; **3241 tests green**; `--selftest --safe`
exits 0 with no marker. `publish\WinGnome.exe` (b1b3053) is **running as the user's everyday instance** (started
15:17 through `explorer.exe`); the previous exe (KI-102-only build) is backed up at
`C:\Users\samhe\AppData\Local\Temp\opencode\publish-backup-20261010\`. Start at launch: the HKCU Run entry
(`WinGnome` → `publish\WinGnome.exe`) and `StartupApproved` (enabled) were verified; the user's "with priority"
wish is unanswered — Windows offers no priority/ordering knob for Run entries (see section 5).

## 3. Work in flight

Nothing. All branches merged; only `main` exists locally (worktrees for ki-102, logo-menu-spec and
tray-callback-adopt removed after their merges).

## 4. Queue, in order

Done since the 2026-10-09 hand-off: the **KI-102 work-area fight fix**, **spec 0021 logo menu + custom logo**,
and the **Windows Security tray icon fix** (former item 2 — diagnosed via the new B3 logging: the icon's mirror
entry had no callback because SecurityHealthSystray registered into the KI-019 front gap, ignores every
TaskbarCreated re-broadcast and zero-fills its unflagged structs; healed by the built-in `windowsdefender://`
click fallback after the general mechanisms were live-proven not to reach it — all three mechanisms are in
section 2 and spec 0022). Also diagnosed and closed as *not reproducible on the current build*: the "System
tray" quick-settings tile (the user's report) — the peek chain was live-verified working end-to-end with the
foreground window matching the tray hwnd; if it ever fails again, the B2 log lines name the exact branch.

1. **Dock dark ring (bug, user-reported):** a darker rim around the whole dock body on both monitors, and around
   the top bar in floating mode with blur on. A read-only design investigation (2026-10-10) derived the exact
   geometry: `BackdropPlacement.Compute` insets the blur backdrop by `ceil((radiusDip − systemRadiusDip)·(1−1/√2)·scale)`
   (≈4 physical px at 125% for the default 18-DIP dock radius), so in the rim zone the body's 0.75-opacity tint
   composites over the RAW desktop instead of blur, and the 1-px `BodyOutline` sharpens the edge. Worst case:
   radius 40 + dark theme + low opacity + 125%. **Key finding: `BlurBackdrop.cs:17-18` records that DWM ignores
   `SetWindowRgn` for accent blur, verified on build 26200** — the HANDOFF's old spike hope (region-clipping the
   backdrop) is likely dead on arrival, and `DWMWA_SYSTEMBACKDROP_TYPE` on the body would require de-layering the
   dock/top-bar windows (breaking per-pixel-alpha slide tricks and spec 0012's type rendering) while DWM's fixed
   8-DIP rounding would overhang the 18-DIP tint — the mirror defect. Recommended path: a 30-minute scratch-app
   spike to confirm the region finding on 26200, then almost certainly the cosmetic fix: snap the DEFAULT dock
   radius to 8 DIP (zero inset; needs a settings-migration decision for persisted 18s) and/or tint the rim zone
   (`RimWidth(radiusDip, scale)` is Core-testable). Check light and dark, 100%/125%.
2. **Spec 0016 GNOME window management**, phased: v1 (Alt+Tab app switcher, Super+A/S/N, Super+Page Up/Down,
   tiling halves/quarters/expand with gaps), then v1.1 (layouts, Ctrl+drag zones, neighbour resize), then v1.2
   (Super+drag move/resize). WP0 spike first (which Win+ combos can be `RegisterHotKey`ed). Safety-critical:
   the shared low-level keyboard hook host — never block in the callback, swallow/unswallow symmetry, fail open.
   `Tiling.Enabled` defaults to true only if the 100 %/150 % cross-DPI QA passes.
3. **Spec 0017 OSD + Night Light / Do Not Disturb** (spike first; OSD on every monitor per the user). When the DND
   service exists, wire it into the Notifications panel's hidden DND row (KI-085).
4. **Spec 0018 overview**: workspace strip, window-to-workspace move (undocumented COM, strict build/UBR table,
   crash marker), app folders, dock pin/unpin by drag, file search. Results order Apps, Windows, Files.
5. **Spec 0019 connectivity panels**, three parts: (a) Win+I and dock Settings pin redirect (needs 0016's hook
   host), (b) Wi-Fi, (c) Network + Bluetooth.

## 5. Things that need the user (batch them into one message)

- **"Start at launch with priority" — clarification needed.** The Run entry (`WinGnome` → `publish\WinGnome.exe`)
  and its `StartupApproved` flag are verified enabled, so it starts at sign-in (proven live 2026-10-10 09:27).
  Windows offers no priority or ordering knob for HKCU Run entries; if "priority" meant process priority or
  starting before other apps, that needs a decision (a scheduled task could set priority but adds a system
  component — AGENTS.md says ask first).
- **Spec 0010 live session** for the checks that still need someone at the machine: B7 unplug/replug, B11 scale
  change / primary swap / sleep-resume, B12 native taskbar mode, the stacked-records unwind on one monitor, the
  corrupt-marker repair with the taskbar hidden, the crash path and the TbExp hide-order experiment. Runbook:
  `C:\Users\samhe\AppData\Local\Temp\opencode\mm-session\RUNBOOK.md` (its expectations predate the KI-102 fix —
  a fight/cool-down episode during those tests is now expected behaviour, not a failure). Needs the everyday
  instance quit; agree a window first and restart it through `explorer.exe` afterwards.
- **Taskbar flashes (user-reported bug, now instrumented):** the B1 lines correlate flashes with Explorer's own
  shows — field data shows bursts at sign-in and around tray/appbar churn (e.g. 11 suppressed shows in one
  minute at 09:28:57, backoff windows where re-hiding slowed to 5 s — those were the most visible flashes; now a 50 ms / 750 ms / 3 s ramp, `TaskbarRehidePolicy`). When
  the user next sees one, grep the everyday log for "Explorer showed taskbar window" at that timestamp; if
  flashes bother them, the tunables are the constants in `TaskbarRehidePolicy`.
- **Logo edge cases (manual items from the QA session):** opaque JPEG (dark mark on light → silhouette), the
  stray-transparent-pixel degenerate case (KI-104), and the missing-file restart fallback — the user
  hand-tested the changer itself and waived agent runs; keep as manual QA.
- **Dock dark ring:** design investigation done (queue item 1); the spike + cosmetic fix needs a product
  decision on snapping the default dock radius to 8 DIP (migration for persisted 18s) vs tinting the rim.
- Accessibility high-contrast spike (steps in the spec 0020 notes / KI entry) and, if they want, cursor size
  (needs the white pointer style).
- AutoPlay "Memory card": change it once in Windows Settings while you diff
  `HKCU\...\Explorer\AutoplayHandlers` to confirm the registry location (the panel uses `ShowPicturesOnArrival`;
  Windows may use `CameraAlternate\ShowPicturesOnArrival`).
- Notifications: does a switched-off app stop showing toasts without sign-out? Privacy: does the Camera app lose
  access when the switch is off?
- **KI-100 check:** with Docker Desktop running, confirm one grouped dock icon (pin's label/icon + running dot).
- **Spec 0010 behaviour:** top bar on every monitor / dock on the main display only are the defaults — both are
  settings (Top Bar → "Show on", Dock → "Show on"); confirm they look right.

## 6. How this session worked (copy what helped)

- One spec per area → independent advisor review of the spec → revise → user decisions batched with recommended
  defaults → implement per work package in its own worktree/branch → independent review → fix → merge `--no-ff`
  → build and test `main` after every merge.
- Parallel branches only when they own separate folders. `PanelRegistry.cs` / `PanelResources.xaml` conflicts are
  always "keep every entry".
- Merge conflicts and shared files: check the spec's work-package table for ownership before editing.
- **Dry-run merges** in a throwaway worktree (`git merge --no-commit`) de-risked the two-branch doc collisions
  (KI renumbering, PLAN.md rows); the real merge then followed the proven resolution.
- **Live QA by vision-model agents** (per the user's rule: code review = deepseek-v4-pro/qwen3.8-max, run-through
  testing = qwen3.8-flash/deepseek-v4.1-flash) worked well: screenshots + synthesized clicks (SendInput tools in
  `%TEMP%\opencode\ki102-session\` — note the original `click-at.ps1` was broken; use the QA agent's fixed
  `ui-click.ps1`/`seq-click.ps1`/`dlgitem-*.ps1`) drove criteria 13/14 and the whole logo-menu QA without the user.
  A locked workstation blocks clicks/screenshots AND makes `explorer.exe <script>` spawn transient explorer
  processes (PID-selection traps in scripts); log-only criteria still run locked.
- **Diagnose before fixing:** the B3 delivery log identified the Security icon's dead-callback root cause from one
  morning of field data; two general fixes were then live-DISPROVEN on the same machine in hours (the app ignores
  broadcasts and zero-fills structs), and the shipped fix is the targeted fallback. Field evidence beats armchair
  mechanism design — keep the diagnostic lines in.
- Editing docs with CRLF: `sed`/python heredoc string replacements can silently miss (line endings); prefer the
  edit tool, and grep for conflict markers before committing a merge.
