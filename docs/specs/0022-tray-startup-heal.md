# 0022 — Tray startup heal: one delayed TaskbarCreated re-broadcast

Status: Implemented (Core e7b7308, host wiring 41a93b7). The 2026-10-10 live check showed the heal works
but SecurityHealthSystray never re-registers on TaskbarCreated — see the extension addendum below
(Implemented: 429ccf9 Core learning, 887e3f0 host logging; its live acceptance is pending with the
integrator). AC 2/4–6 below still need a running app, which this branch must not start.

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

> `Tray icon (owner 0x…, id …) appeared without a callback (it registered before the host was in front); clicks do nothing until the app re-registers`

(The wording deliberately does not promise the heal: an adoption logged *after* the heal's broadcast is a
mid-session gap the heal no longer covers — KI-105. Compare timestamps with the heal line.)

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

---

## Extension 2026-10-10: learning the callback from unflagged updates

Status: Implemented (Core learning + tests 429ccf9, host logging 887e3f0) — live acceptance below needs a
running app, which this branch must not start; the integrator runs it.

### Problem

The live check of the heal on the shipping build (2026-10-10) **failed for SecurityHealthSystray**. The
heal itself works — it fired at +2.002 s and two apps re-registered with full data within 13 ms — but
Windows Security never re-registers on `TaskbarCreated`: not for the first broadcast, not for the heal.
It answers each broadcast with a tooltip-only `NIM_MODIFY` (no `NIF_MESSAGE`), which creates and keeps a
callback-less entry:

> `12:22:58.757 INFO Tray icon (owner 0x10378, id 100) appeared without a callback (it registered before the host was in front); clicks do nothing until the app re-registers`

and every click on it stays dead:

> `Tray LeftDown on "Windows Security - No actions needed." (owner 0x10378, id 100) did nothing: the icon registered no callback message`

The re-registration the original design waits for never comes, so the heal cannot repair this icon.

### Design

Apps nearly always reuse ONE `NOTIFYICONDATA` struct for every `Shell_NotifyIcon` call, so the raw
`uCallbackMessage`/`uVersion` fields ride along on tooltip-only modifies even when `NIF_MESSAGE` isn't
flagged — `ParseNotifyIcon` already exposes them (`NotifyIconCommand.CallbackMessage`/`.Version`). The
registry learns from them (`TrayIconRegistry.Learn`, state internal to the registry):

1. A `NIM_MODIFY` whose entry exists (or is created via the `knownToShell` adoption path) with
   `CallbackMessage == 0` in the resulting state, **without** `NIF_MESSAGE`, and with a *plausible* raw
   pair — callback in `[0x0400 (WM_USER), 0xBFFF (top of WM_APP)]`, version in `{0, 3, 4}` — is a
   **candidate observation**, stored per icon.
2. A **second** observation of the **same** pair **adopts** it: `CallbackMessage` and `Version` are set
   together from the confirmed pair and the returned `TrayChange` carries `LearnedCallback = true`
   (optional positional flag, like `CreatedViaModify`). A *differing* plausible pair **replaces** the
   candidate. An implausible pair (0, out of range) neither stores nor clears.
3. **Flagged data always wins**: a modify/add with `NIF_MESSAGE`, or a `NIM_SETVERSION`, sets the
   authoritative values (existing behaviour) and clears any pending candidate. Learning never overwrites
   a non-zero callback.
4. Removing an icon (delete or owner-gone sweep) clears its candidate. Candidates are **in-memory only**,
   never persisted.
5. Version 0 in a confirmed pair is fine: it means legacy callback encoding, which `TrayCallback`
   already speaks.

Diagnostics (`TrayHost.OnNotifyIcon`, plain `Log` — tray thread): adoption logs
`… learned its callback 0x… (version …) from unflagged updates; clicks now deliver`; storing or replacing
a candidate logs `…: unflagged update carries callback 0x… version … (observation 1)` — the field
evidence for whether apps populate the raw fields at all. `TrayChange.ObservedCallback`
(`(uint Callback, uint Version)?`) is the channel. The observation line is always "observation 1": a
store/replace is by construction the first observation of its candidate (a matching second one adopts
instead, a differing one restarts the count). The existing "appeared without a callback" line stays; a
learned line may follow it ~2 s later. `TrayModel.Deliver`, the heal mechanism, forwarding, `TrayAnchor`
and work-area code are untouched: a learned callback flows through the existing delivery path unchanged.

### Safety

- **Two-observation rule**: a single unflagged raw value could be an uninitialised-struct leftover; the
  same value must arrive twice from the app's own live struct. In practice apps answer every
  `TaskbarCreated` broadcast with a modify, so the host's startup broadcast and its +2 s heal broadcast
  deliver both observations — healing within ~2 s while making false positives vanishingly rare.
- **Range gates**: below `WM_USER` is refused absolutely (a junk `WM_CLOSE` 0x0010 must never be
  adopted); `0xC000`+ is the `RegisterWindowMessage` range and refused; versions other than {0, 3, 4}
  (e.g. balloon `uTimeout` values sharing the union field) are refused.
- **Worst case of a false positive**: the app's own window is posted a plausible message number taken
  from the app's own struct field. Unknown message ids fall through to `DefWindowProc`; the value cannot
  name another process's window (it is sent only to the icon's recorded owner).
- Flagged data always overrides the guess, learning never touches a non-zero callback, and nothing is
  persisted — a restart re-learns from live wire data only. No system state, no recovery surface.

### Acceptance criteria (live, integrator)

1. Cold start with SecurityHealthSystray running: the log shows the adoption line for `(owner 0x10378,
   id 100)`, then — after the heal broadcast, within ~2 s of it — the observation lines
   (`unflagged update carries callback 0x… version 4`) and the learned line
   (`learned its callback 0x… (version 4) … clicks now deliver`) for the same icon.
2. A click on the Windows Security icon then produces a real delivery line
   (`Tray LeftDown … version 4, callback 0x…: sending [0x…]`) instead of the `registered no callback
   message` no-op, and the Windows Security flyout opens.
3. Icons that re-register with flagged data (the two apps the heal already repaired) are untouched by
   learning: no observation or learned lines for them beyond at most one candidate that never adopts.

### Residual gap

Apps that neither re-register on `TaskbarCreated` nor send at least two unflagged updates reusing their
struct stay click-dead, and a mid-session adoption heals only once its second observation arrives (any
later update the app sends). KI-105 narrowed accordingly; the heuristic itself is KI-106.

---

## Extension 2 (2026-10-10): the targeted fallback for SecurityHealthSystray

Status: Implemented (Core table + tests a3a9c8d, TrayModel wiring 3defc51) — live acceptance below needs a
running app, which this branch must not start; the integrator runs it.

### Problem

Two live runs of the heal+learning build on the user's machine show SecurityHealthSystray (owner 0x10378,
icon id 100) defeating **both** general mechanisms:

- It **ignores every `TaskbarCreated` broadcast** — the host's startup one and the +2 s heal — and never
  re-registers, unlike two other apps that re-registered with full data within 13 ms of the heal (the heal
  works in general and stays).
- It sent **exactly one** tooltip-only `NIM_MODIFY` (the adoption at host start), and its raw wire
  callback/version fields were implausible/zero — no `unflagged update carries` observation line was ever
  logged for it. It does not reuse its `NOTIFYICONDATA` struct, so learning has nothing to learn (learning
  stays for other apps; it is harmless and proven inert here).

Clicks therefore remain dead:

> `Tray LeftDown/LeftUp/RightDown/RightUp/LeftDoubleClick on "Windows Security…" did nothing: the icon registered no callback message`

The user's bug report expects clicking, double-clicking and right-clicking the icon to open Windows
Security — what the icon's own click does (SecHealthUI, via the `windowsdefender://` protocol).

### Design

A **built-in click fallback table** (`WinGnome.Core.Tray/TrayClickFallback.cs`, pure and tested):

- `LaunchUriFor(processFileName)` — case-insensitive exact file-name match (a full path is accepted; the
  file-name part is compared) against `{ "securityhealthsystray.exe" → "windowsdefender://" }`; null
  otherwise. Precedent for hardcoding Windows' own components: `NotificationAppList.SystemNames`. Scope
  discipline: ONE entry; the table is data, extensible per field-confirmed dead icon; no generic
  "activate the app" heuristic.
- `ShouldLaunch(nowMs, lastLaunchMs)` with `LaunchGapMs = 750` — one physical double-click delivers
  LeftUp → LeftDoubleClick → LeftUp; one launch must serve the whole sequence.

App layer (`TrayModel.Deliver`): in the `CallbackMessage == 0` branch, for click-class actions only (the
same `MayTakeForeground` gate as the existing diagnosis log), resolve the owner's process path via the
existing `NativeMethods.GetProcessId`/`GetProcessPath` helpers (cached per owner hwnd; both fallback
caches are cleared in `StopHost` — hwnds are reused, and a stale mapping could at worst launch the one
hardcoded URI). On a table hit and `ShouldLaunch` (per-icon last-launch timestamps,
`Environment.TickCount64`), launch the URI through an optional `Func<string, bool>` injected into
`TrayModel` — `TopBarServices` passes `uri => context.Launcher.Launch(uri)`, the same launcher path
`TopBarActions` uses for `ms-settings:` — and log:

> `Tray {action} on "{tip}": no callback; opening the registered app instead (windowsdefender://)`

On a miss (unknown process, no launcher — tests/selftest), the existing "did nothing: the icon registered
no callback message" log stays **exactly** as it was; deduplicated events inside one click sequence stay
silent. `TrayModel.Deliver`'s real delivery path, the heal, learning, forwarding and anchor code are
untouched.

Right-click opens the app too: the icon's own context menu lives behind its callback message, which this
app never delivers to us — the user asked for exactly this behaviour.

### What remains impossible

The icon's real context menu and any balloon actions: they are delivered *to the app's callback*, which
never exists for this icon. The fallback opens the app; it cannot emulate its menu.

### Acceptance criteria (live, integrator)

1. Cold start, SecurityHealthSystray adopted click-dead as before ("appeared without a callback" line, no
   observation line): a click, double-click or right-click on the Windows Security icon launches
   `windowsdefender://` — the Windows Security app opens — and the delivery log shows
   `no callback; opening the registered app instead (windowsdefender://)`.
2. One physical double-click opens the app exactly once (the 750 ms gap absorbs Up/DoubleClick/Up).
3. Every other dead icon (process not in the table) still logs the unchanged `did nothing: the icon
   registered no callback message` line; icons with callbacks are entirely unaffected.

### Residual gap (unchanged in kind)

Any *other* app that neither re-registers nor carries usable wire fields stays click-dead until it is
confirmed in the field and added to the table (KI-105).
