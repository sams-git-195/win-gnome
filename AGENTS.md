# AGENTS.md

How to work on WinGnome. This applies to every contributor, human or AI agent. Read it before you change
anything, then read [README.md](README.md) (what the product does) and [docs/PLAN.md](docs/PLAN.md)
(architecture, conventions, Core API, safety rules). If this file and PLAN.md disagree, PLAN.md wins on
code conventions and this file wins on process. Fix the disagreement in the same change.

WinGnome runs all day inside the user's shell. It hides the taskbar, writes to the registry, hooks the
keyboard and draws over other apps' windows. A bug here doesn't just crash an app, it can leave
someone's desktop broken. Work accordingly.

---

## The rules that matter most

1. **Plan before you code.** Anything bigger than a small fix gets a written spec first (see below).
2. **Ask when the answer changes what you build.** Don't guess at behaviour, defaults, or anything that
   touches safety. Don't ask what the code, README or PLAN.md already answers.
3. **Every test must be able to fail.** See it go red before you trust it green.
4. **Leave the machine as you found it.** Session changes (taskbar, title-bar colours, focus-follows-mouse)
   are recorded before they're made and undone on exit, on crash, and on the next start. Persistent
   registry tweaks back up the original value first and can always be reverted.
5. **Never push unless the user asks you to in the current conversation.** Commit locally; pushing is the
   user's call (it runs CI once a workflow is set up).
6. **Write down what you find.** Bugs, risks and limitations you don't fix go into
   [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) with a severity.

---

## 1. Plan and spec

### Size the work

| Size | Examples | What to write first |
|---|---|---|
| Trivial | Typo, rename, one-line fix with an obvious cause | Nothing. Just a clear commit message. |
| Small | Bug fix in one feature, a new setting wired through | A few lines in your reply or commit: cause, fix, how it's tested. |
| Feature | New UI surface, new hook, new tweak, cross-feature change | A spec in `docs/specs/NNNN-short-title.md` (see [docs/specs](docs/specs/README.md)), agreed before coding. |

### Spec template

```markdown
# NNNN — Title

## Problem
What's wrong or missing, for whom, and how we know.

## Behaviour
What the user sees and does. Defaults. Settings added (name, type, default, where in Settings).

## Non-goals
What this deliberately doesn't do.

## Design
Which files and layers change. What goes in WinGnome.Core (testable) vs the app (interop/UI).
Threading, DPI, multi-monitor and elevated-window considerations.

## Safety and recovery
What system state changes, where the original is recorded, how it's restored on exit, crash and
force-kill, and how --safe and --selftest behave.

## Footprint
Idle CPU and memory cost. Any timers, hooks or per-frame work, and why they're needed.

## Acceptance criteria
Numbered, checkable statements. Each one maps to a test or a QA step.

## Risks and open questions
```

### Clarifying questions

Ask before building when:

- The request has two or more reasonable readings that lead to different code.
- A default, a setting name or anything user-visible isn't specified.
- The change touches the taskbar, the registry, keyboard/mouse hooks, other processes' windows, or
  elevation.
- It would add a NuGet package, a new process, a service, a scheduled task, or network access.
- It would break existing settings files or change existing behaviour users rely on.

Batch questions into one message, offer options with a recommended default, and say what you'll assume
if there's no answer. Don't ask about things you can find out by reading the code or running it.

---

## 2. Write clean code

The conventions in [docs/PLAN.md](docs/PLAN.md#conventions-all-agents) are binding: C# 12, nullable on,
file-scoped namespaces, `sealed` by default, P/Invoke only in `Interop/NativeMethods.<Area>.cs`, Win32
failures are logged with `Log.Warn` and never fatal, Per-Monitor-V2 DPI with `PixelRect` for physical
pixels. In addition:

- **Logic in Core, platform in the app.** If a decision can be expressed without Win32 (geometry,
  filtering, parsing, state machines, ordering, timing rules) it belongs in `WinGnome.Core` with tests.
  The app layer should be thin glue: read the OS, call Core, apply the result.
- **Match the surrounding code.** Same naming, comment density, idioms and file layout as its
  neighbours. Read the files around your change before you write.
- **Small, single-purpose units.** A method does one thing at one level of abstraction. If you need
  "and" to describe it, split it.
- **Names say what, comments say why.** Don't comment what the code already says. Do comment Win32
  quirks, ordering constraints, and anything a reader would otherwise "fix" back into a bug.
- **No dead code.** No commented-out blocks, unused parameters, speculative abstractions or `// TODO`
  without a matching entry in KNOWN_ISSUES.md.
- **Own your resources.** Hooks, timers, HWNDs, GDI handles, COM objects and event subscriptions are
  released in `Dispose()`, in reverse order of creation, and disposal is safe to call twice.
- **Fail soft, never silently.** Catch at feature boundaries, log with context (which window, which call,
  the Win32 error code), degrade the feature, keep the shell running. Never swallow an exception without
  logging it.
- **Features stay independent.** Features never reference each other. They communicate through
  `ShellCommands` and settings.
- **No new dependencies** in the app without a written reason in the spec. Test-only packages are fine.
- **Settings are a public contract.** New settings have safe defaults; old settings files must still load.
  Never rename or repurpose a persisted field without a migration and a test for the old shape.
- **The build has no warnings.** `dotnet build -warnaserror` must pass. Suppress an analyzer only with a
  justification comment, at the narrowest scope possible.

---

## 3. Tests

Tests live in `tests/WinGnome.Core.Tests` (xUnit), mirroring the `src/WinGnome.Core` folder layout.
Prefer `Subject_Condition_ExpectedResult` for new tests (for example
`FocusOrMinimize_Focused_MinimisesTheForegroundWindow`); `Subject_ExpectedResult` is fine when there's no
interesting condition.

### Tests must be able to fail

A test that can't fail is worse than no test, because it tells you something is covered when it isn't.

- **See it red first.** For a bug fix, write the test, run it against the unfixed code, and watch it fail
  for the right reason. Then fix. For new code, after it's green, temporarily break the code under test
  (flip a condition, return a constant, delete a line) and confirm the test fails. Put it back.
- **Assert on outcomes, not on the test's own setup.** Don't assert that a value you just put in a fake
  comes back out. Don't assert only that "no exception was thrown" unless that really is the behaviour.
- **Assert specifically.** Compare exact values or whole records (`Assert.Equal(expected, result)`), not
  `NotNull` or `True(x > 0)` when the exact answer is known.
- **No logic in tests.** No loops or conditionals that compute the expected value the same way the
  production code does. Use literal expected values, or `[Theory]` rows.
- **Deterministic.** No real clock, no `Thread.Sleep`, no randomness without a fixed seed, no dependence
  on the machine's monitors, DPI, locale, time zone or registry. Pass time, culture and environment in.
  Use `InMemoryRegistryStore` instead of the real registry.
- **Cover the edges.** Empty, one, many; zero and negative sizes; boundaries (exactly at a threshold, one
  either side); DPI scaling other than 100%; monitors with negative coordinates; malformed input for
  parsers.
- **Every bug fix ships with a regression test** that fails without the fix, unless the bug is in
  interop that can't be unit-tested. Then say so in the commit and describe the manual check.

### What can't be unit tested

Interop, WPF rendering and hooks can't be reliably unit tested. Keep that layer thin and cover it with:

- `--selftest` (starts every feature in safe mode for 5 s and exits 0 on success).
- The manual smoke test in the QA section below.

If you find yourself wanting to test app-layer code, that's usually a sign the logic should move to Core.

---

## 4. QA: definition of done

A change is done when every item that applies is true. Report each one honestly: if you skipped a step
or something failed, say so with the output.

1. **Builds clean:** `dotnet build -c Release -warnaserror`
2. **Tests pass:** `dotnet test`, with new and changed behaviour covered, and each new test seen failing.
3. **Self-test passes:**
   `dotnet run --project src/WinGnome -c Release -- --selftest --safe --settings-dir <scratch folder>`
   exits with code 0.
4. **Smoke test** (for anything user-visible or touching system state): quit your everyday WinGnome first
   (two instances with different profiles both manage the taskbar), run with an isolated
   `--settings-dir`, exercise the change, check the top bar, dock, window buttons and overview still work,
   then quit and confirm the taskbar, work area and title-bar colours are back to how they were, and that
   any tweak you applied reverts cleanly.
   Check light and dark mode and at least one DPI other than 100% when layout changes.
5. **Safety paths checked:** if the change alters system state, verify restore on normal exit, on crash
   (add a temporary `throw` in a local build and remove it before committing), and after
   `taskkill /f` followed by a restart with the same `--settings-dir`. Check that `--safe` doesn't touch it.
6. **Footprint checked:** no new polling, per-frame work or always-on surfaces at idle. Compare idle
   memory and CPU in Task Manager before and after for anything that adds a window, hook or timer.
7. **Independent review:** non-trivial changes get a review by someone (or an agent) who didn't write the
   code, looking for correctness, races, leaks, restore paths and test quality. Fix what it finds, or log
   it in KNOWN_ISSUES.md.
8. **Docs updated:** README feature list and command-line table, PLAN.md Core API table and module map,
   the spec, and KNOWN_ISSUES.md.

---

## 5. Engineering judgement

- **Safety first, then correctness, then footprint, then features.** A feature that risks leaving the
  taskbar hidden or the registry changed doesn't ship until its recovery path is proven.
- **Record before you change.** Persist the original state before you modify the system, so a force-kill
  can be recovered on the next start. Restore in reverse order.
- **Least privilege.** HKCU only. No elevation, no code injection into Explorer or other processes, no
  acting on elevated windows. Use supported, documented APIs where they exist. When you must rely on
  undocumented behaviour, isolate it, log failures, and add it to KNOWN_ISSUES.md.
- **Think about the hostile cases.** Explorer restarting, display changes, DPI changes, monitors added or
  removed, sleep and resume, fast user switching, session end, a second WinGnome instance, windows that
  close mid-operation, and HWND reuse.
- **Threading.** All UI is on the WPF dispatcher thread. Don't block it: no synchronous I/O, waits or
  cross-process `SendMessage` without a timeout on the UI thread. The one exception is `TrayHost`, whose
  window runs its own Win32 message loop on a dedicated thread and must never wait on the dispatcher.
- **Idle cost is a feature.** WinGnome runs all day. Prefer events to polling, one-shot timers to
  repeating ones, and hidden-and-small to hidden-and-full-size. WPF renders in software process-wide.
- **Scope discipline.** Do what was asked. If you spot something else worth fixing, log it in
  KNOWN_ISSUES.md or mention it, rather than widening the change.
- **Small, reviewable commits.** One logical change per commit. Don't mix refactors with behaviour
  changes. A reviewer should be able to read the diff in one sitting.
- **Reversible decisions are cheap, irreversible ones aren't.** Spend your caution on settings formats,
  registry writes, and anything users will depend on.

---

## 6. Known issues

[docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) is the single list of known bugs, risks and limitations.

- **Add** an entry when you find a bug you aren't fixing now, a risk a reviewer raised that wasn't
  addressed, a limitation of a new feature, or a `// TODO` you leave in code (reference the ID in the
  comment, for example `// KI-012`).
- **Update** an entry when you learn more, and **close** it (status *Fixed*, with the commit) when it's
  resolved. Don't delete closed entries; move them to the *Resolved* section.
- Use the severity scale defined in that file. When in doubt, pick the higher severity.

---

## 7. Git and CI

- Solo work commits to `main`. Agents working in parallel each use their own branch or worktree and merge
  back to `main` when their work passes QA.
- Commit messages follow the existing log: a short imperative summary (≤ 72 characters, no trailing full
  stop), a blank line, then the why and any notable details.
- Never commit build output (`bin/`, `obj/`, `publish/`), logs, settings profiles or secrets.
- **Never push, open a pull request or trigger CI unless the user explicitly asks in the current
  conversation.** Permission to push once is not permission to push again later. Batch local commits and
  push when asked, so CI runs once per batch instead of once per commit.
- Never force-push `main`, rewrite published history, or skip hooks (`--no-verify`).

---

## 8. Working with multiple agents

- **Plan first, then fan out.** Agree the plan and specs, then split work so agents own separate folders
  (see the module map in PLAN.md). Two agents never edit the same file at once; `Interop/` partials are
  shared, so check for an existing declaration before adding one.
- **Use capable models for code.** Implementation is done by strong models (Sonnet or Opus class). Don't
  use small or fast models (Haiku class) to write code. They're fine for searching and summarising.
- **Get a second opinion on design.** For non-obvious design questions, consult a separate advisor model
  before committing to an approach.
- **Review with a different agent than the author.** QA review is done by Opus, especially for code
  written by Sonnet. The reviewer checks the definition of done above, not just whether the diff
  compiles.
- **Hand-offs are self-contained.** A sub-agent prompt includes the goal, the files it owns, the
  constraints from this file, and what "done" means, because it doesn't see the conversation.

---

## Quick reference

```powershell
dotnet build -c Release -warnaserror
dotnet test
# Self-test and smoke test use separate profiles: only one WinGnome runs per profile folder.
dotnet run --project src/WinGnome -c Release -- --selftest --safe --settings-dir "$env:TEMP\wingnome-selftest"
dotnet run --project src/WinGnome -c Release -- --settings-dir "$env:TEMP\wingnome-smoke"
# After a crashed run, restore the taskbar from the same profile it was hidden from.
dotnet run --project src/WinGnome -c Release -- --restore-taskbar --settings-dir "$env:TEMP\wingnome-smoke"
```

Logs: `%APPDATA%\WinGnome\wingnome.log`, or `<settings-dir>\wingnome.log` with `--settings-dir`.
Logo: [assets/logo/wingnome.svg](assets/logo/wingnome.svg) is the source for every icon size.
