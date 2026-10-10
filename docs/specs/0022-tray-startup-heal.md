# 0022 — Tray startup heal: one delayed TaskbarCreated re-broadcast

Status: Agreed

## Problem

Field logs from the user's machine show Windows Security's tray icon ignoring every click in the top bar:

> `Tray LeftDown on "Windows Security - No actions needed." (owner 0x10378, id 100) did nothing: the icon registered no callback message`

Mechanism, verified against the code:

1. At sign-in the tray host broadcasts `TaskbarCreated` immediately in `TrayHost.CreateHostWindow` while
   Explorer's `Shell_TrayWnd` may still be frontmost — the host's front check needs up to ~1 s to win the
   z-order (KI-019). Apps answer the broadcast with `Shell_NotifyIcon`, which goes to the *first*
   `Shell_TrayWnd` in z-order: SecurityHealthSystray's `NIM_ADD` + `NIM_SETVERSION` reach **Explorer only**.
2. Later, the app's tooltip-only `NIM_MODIFY`s (no `NIF_MESSAGE`) reach our host once we are in front.
   `Forward` succeeds → `knownToShell` → `TrayIconRegistry.Apply`'s Modify path creates an entry via
   `TrayIconState.Create`, whose `CallbackMessage` is 0 and whose `With()` applies a callback only when
   `NIF_MESSAGE` is flagged — which a tooltip modify isn't. The app's `NIM_SETVERSION` arrived before the
   entry existed, so it was Rejected and `Version` stays 0.
3. `TrayModel.Deliver` sees `CallbackMessage == 0` and no-ops forever: the icon shows and tooltips, but is
   click-dead until the app happens to re-register.

## Behaviour

No user-visible setting. Once per tray-host start, ~2 s after the host window is created (by which time the
front check has reliably won the z-order), the host brings itself to the front and broadcasts `TaskbarCreated`
a second time — the "startup heal". Every app re-answers with a full `NIM_ADD` (+ `NIM_SETVERSION`), which the
registry applies in place over any half-known entry, so callbacks and versions are repaired. Duplicate ADDs
are harmless: the registry updates in place (`DuplicateAdd_UpdatesInPlace`), Explorer treats a re-ADD of an
existing id as an update, and `Forward`'s `SendMessageTimeout(SMTO_ABORTIFHUNG)` burst defence was written for
exactly this storm. `AskAppsToRegister` re-arms the 15 ms/4 s front-check burst, so the re-registration burst
is caught while we are in front.

Heal timing rules (Core, `TrayRebroadcastPolicy`):

- The heal is due at/after `GraceMs` (2000) past `OnHostStarted`, and fires at most **once** per host start.
- **Every** `TaskbarCreated` broadcast the host sends (startup, heal, Explorer-restart rebroadcast) is
  reported to the policy and opens a `CooldownMs` (10 000) window. If the heal is still pending when such a
  broadcast goes out, the heal is **skipped for this host start** (not deferred): that broadcast already made
  every app re-register with us while we were in front, so a heal on top of it would only double the storm.
- `OnHostStarted` is called *after* the startup broadcast and clears any cooldown: the startup broadcast is
  what the heal repairs (it may have reached apps while Explorer was in front), not a reason to skip it.

Diagnostic: when a `NIM_MODIFY` the shell accepted creates a new registry entry (`TrayChange.CreatedViaModify`)
and the call carries no `NIF_MESSAGE`, the host logs once per such entry (plain `Log`, tray thread):

> `Tray icon (owner 0x…, id …) appeared without a callback (it registered before the host was in front); the startup heal should repair it`

## Non-goals

- **Mid-session front gaps** (KI-019 proper): an icon that registers while Explorer is briefly in front
  *after* the heal has fired stays click-dead until the app next re-registers. Re-broadcasting on every
  mid-session front regain would storm all apps repeatedly for a rare, bounded gap. Tracked as KI-105.
- **Persisting callbacks across runs**: rejected for safety — a stale (owner, id) → callback map would deliver
  clicks to whatever window later reuses that HWND.
- **Reading icons back from Explorer**: no supported API; not attempted.
- No change to `Forward`/forwarding semantics.

## Design

- **Core, new** `src/WinGnome.Core/Tray/TrayRebroadcastPolicy.cs`: pure, clock-injected (`long nowMs`) state
  machine in the style of `TrayFrontCheckSchedule`. `OnHostStarted(nowMs)` arms (and re-arms) the one heal and
  clears the cooldown; `OnBroadcastSent(nowMs)` starts a cooldown; `ShouldSendHeal(nowMs)` returns true exactly
  once per host start, only at/after the grace point and outside any cooldown, and consumes the heal when it
  does. Tests: `TrayRebroadcastPolicyTests`.
- **Core, extended** `TrayChange` gains `CreatedViaModify` (optional positional flag, defaults false; existing
  constructions and equality unchanged): true only when the registry's knownToShell-Modify path created the
  entry. Tests in `TrayIconRegistryTests` pin the click-dead mechanism
  (`Modify_UnknownIcon_WithoutMessageFlag_CreatesEntryWithNoCallback`) and the heal
  (`DuplicateAdd_HealsMissingCallbackAndVersion`).
- **App** `TrayHost`: `StartupHealDelayMs = 2000` and `StartupHealTimer = 3` (next free WM_TIMER id). In
  `CreateHostWindow`, after the first `AskAppsToRegister`, the policy is armed and the one-shot timer set.
  `OnTimer` kills the timer and, when `ShouldSendHeal` allows, does `BringToFront()`, logs the heal line and
  calls `AskAppsToRegister(hwnd)`. `DestroyHostWindow` kills the timer. Every `BroadcastTaskbarCreated` reports
  to `OnBroadcastSent`. The policy object is tray-thread-only, like the registry.
- The existing `IsOwnBroadcast` 'WGNM' wParam filter (verified, kept) prevents our own heal broadcast from
  being misread as an Explorer restart, so the heal cannot schedule a rebroadcast loop.

## Safety and recovery

- No system state is touched: the heal is one extra `SendNotifyMessage` broadcast per host start, identical
  in kind to the existing startup broadcast. Nothing to record, nothing to restore; `--safe` and `--selftest`
  behave as before (the selftest host simply sends its startup broadcast, heals at ~2 s, and is torn down).
- Broadcast storm is bounded: at most one extra broadcast per host start, and the cooldown *removes* the heal
  when another broadcast (Explorer restart) already did the work.
- Takeover-safe: a second WinGnome instance holds no host window while it waits; when it takes over, it runs
  `CreateHostWindow` and performs its own heal.
- Loop-safe: our broadcasts carry 'WGNM', which `OnTaskbarCreated` ignores.

## Footprint

One one-shot `SetTimer` per host start, killed on the heal or on destroy. No polling, no per-frame work, no
new always-on surface. Idle cost unchanged.

## Acceptance criteria

1. Cold start with SecurityHealthSystray running: within ~5 s the log shows `Tray host re-asks apps to
   register (startup heal)`, then `Tray icon (owner …, id …) set callback version 4` for it, and a click on
   its icon produces a real `Tray LeftDown … sending [0x…]` delivery line instead of the
   `registered no callback message` no-op.
2. Exactly one extra `TaskbarCreated` broadcast per host start in the normal case (startup + heal), and none
   of them schedules a rebroadcast (the 'WGNM' filter).
3. If another broadcast went out before the heal was due (e.g. Explorer restarted at sign-in), the heal is
   skipped for that host start — covered by `TrayRebroadcastPolicyTests`.
4. Idle CPU and memory unchanged; no new timer at rest.
5. Explorer keeps all icons (every heal ADD is forwarded like every other call).
6. A takeover instance performs its own heal after it creates its host window.

## Risks and open questions

- An app that ignores a second `TaskbarCreated` in quick succession (the concern documented at
  `TrayHost.DestroyHostWindow`, KI-005) may end up only in Explorer's tray after the heal; the same risk
  already exists for the startup broadcast and the Explorer-restart rebroadcast.
- If Explorer restarts *just after* the heal fires, its rebroadcast follows within ~2 s: two registration
  storms close together. Bounded, defended by the SMTO burst handling, and strictly rarer than the disease.
