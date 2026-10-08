# 0002 — Brightness slider in quick settings

Status: Implemented (see the commit that adds this line)

## Problem
Clicking the system pill at the top right opens the quick-settings card, which has a volume slider but no
screen brightness control. On laptops the user has to leave WinGnome (Windows quick settings or Fn keys) to
dim the panel.

## Behaviour
- The card gets a brightness row directly under the volume row, built the same way: round icon button
  (sun glyph U+E706 from Segoe Fluent Icons, the font the volume row uses), the `QuickSlider`, and a
  right-aligned percentage. The icon is decorative (no click action), unlike the volume mute button.
- Dragging the slider or scrolling the mouse wheel over the row (5 % per notch) changes the internal panel's
  brightness live.
- The row is hidden when no display supports brightness control (desktops, external-only setups), so
  the card looks exactly as before there.
- The current value is read each time the card opens. Nothing is read or polled while the card is closed.
  Changes made elsewhere (Fn keys) while the card is open are not tracked until it is reopened.
- Slider positions are snapped to the levels the panel reports as supported.
- No new settings.

## Non-goals
- External monitors over DDC/CI (dxva2). Not in this change; logged as a limitation.
- Night light, auto brightness, per-monitor sliders, a brightness entry on the status pill tooltip or wheel.
- Reacting to brightness changes made while the card is closed.

## Design
- **Core** (`WinGnome.Core.TopBar`):
  - `BrightnessScale`: `NormalizeLevels` (sort, de-duplicate, drop values outside 0..100), `Snap` (slider
    percent to the nearest supported level; clamps non-finite input and tolerates an empty list), `Step`
    (the neighbouring supported level up or down, for the keyboard).
  - `BrightnessWheel`: wheel deltas to levels, 5 % per notch. Precision touchpads send fractions of a notch,
    so the travel that has not yet bought a whole step is banked and added to the next delta; sub-notch
    deltas adding up to a notch move exactly one notch. On a panel with coarse levels (e.g. 0/25/50/75/100) a
    whole notch steps to the neighbouring level. Travel that cannot be used (already at an end) is dropped.
  - `WriteCoalescer`: coalescing write state machine. At most one write is in flight; values posted while
    it runs replace each other, so a drag produces roughly one WMI call per completed call instead of one per
    mouse-move pixel, and the last value always wins.
- **App** (`Features/TopBar`):
  - `Services/WmiBrightnessPanel`: the WMI side. Reads `WmiMonitorBrightness` and writes
    `WmiMonitorBrightnessMethods.WmiSetBrightness` in `root\wmi` through late-bound COM
    (`WbemScripting.SWbemLocator` with `dynamic`), so **no NuGet package is added** (System.Management would
    add a package for a handful of calls). Writes use `SWbemServices.ExecMethod` with an in-parameters
    object: calling `WmiSetBrightness` on an instance object works only for the first instance object fetched
    in a process on the test laptop and fails with E_FAIL ("Unspecified error") for every later one, so a
    card that was opened twice would lose its slider.
  - `Services/BrightnessController`: owns the panel, the current level and the `WriteCoalescer`. All WMI work
    runs on thread-pool threads (`Task.Run`) and results are marshalled back to the dispatcher; the UI
    thread never waits on WMI. The coalescer is only touched on the dispatcher thread.
  - `SystemStatusViewModel`: `IsBrightnessAvailable`, `BrightnessPercent` (two-way), `BrightnessText`,
    `NudgeBrightness`, `RefreshBrightness`.
  - `QuickSettingsCard.xaml(.cs)`: the row, its wheel handler and a key handler on the slider: arrow and Page keys
    step one supported level (the slider's own 2 % / 10 % steps would snap straight back on a coarse panel);
    pointer drags use plain `Snap`, so a coarse panel never flips levels under a stationary pointer. `TopBarWindow` calls
    `RefreshBrightness` when the card opens.
- Threading: reads and writes are serialised (one WMI call at a time). A read that finishes while a write
  is in flight is discarded; one that finishes after the user changed the level (even if the write is done)
  is adopted but keeps the user's level, so a stale read can never move the slider back. Panels are disposed
  on a worker thread because disposing waits for a call in flight. Results
  arriving after `Dispose` are ignored.
- DPI/multi-monitor: no layout beyond the existing card.

## Safety and recovery
Changes screen brightness only, through the supported WMI brightness interface. Nothing is recorded or
restored: brightness is the user's own setting and persists in Windows like any other brightness change.
`--safe` still shows the row (it is a user action, not a session tweak); `--selftest` never touches it
because no card is opened. A WMI failure of any kind is logged with `Log.Warn` and hides the row; the next
open of the card tries again, including after "no instances" (a desktop, or a laptop that was docked and
has since been undocked), so the row can come back.

## Footprint
No timers, hooks or polling. One WMI connection plus read per card open, on a worker thread. The first open
loads the WMI scripting libraries into the process (one-off memory cost); a machine without a controllable
panel pays one such query per card open. The COM objects are released on dispose and replaced on every refresh. While the display is dimmed or off
(Windows presence dimming, lid closing) WMI reads 0 and writes fail; that is the normal failure path above.

## Acceptance criteria
1. `Snap` returns the nearest supported level for in-range, between-level, out-of-range, NaN and sparse
   inputs. (Core test)
2. `NormalizeLevels` sorts, de-duplicates and removes out-of-range values; empty input stays empty.
   (Core test)
3. `BrightnessWheel` moves 5 % per notch, banks sub-notch deltas so that they add up to exactly one notch,
   clamps at the ends without banking unusable travel, and reaches the neighbouring level on coarse panels.
   `Step` returns the neighbouring level, staying put at the ends. (Core tests)
4. `WriteCoalescer` starts a write immediately when idle, keeps only the latest value while busy, skips a
   re-write of a value already written, and goes idle after the last write. (Core test)
5. On a laptop the row appears, shows the current brightness, and dragging changes the real brightness. (QA)
6. On a machine without brightness support the row is absent. (WMI-less path is a code path covered by the
   same hide logic; checked by inspection since this laptop supports it.)
7. The card opens without a visible stall; dragging never blocks the UI. (QA)
8. `dotnet build -c Release -warnaserror`, `dotnet test` and the self-test pass.

## Risks and open questions
- Late-bound COM via `dynamic` is not trim/AOT friendly; the app is not trimmed.
- Some panels or drivers accept `WmiSetBrightness` slowly or ignore it (hybrid graphics). The row then
  just shows the value read on the next open.
- DDC/CI external monitors remain unsupported.
