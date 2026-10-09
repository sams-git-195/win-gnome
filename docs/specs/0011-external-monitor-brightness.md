# 0011 — Brightness for external monitors (DDC/CI)

Status: Draft (plans KI-021; needs a product decision before it's agreed)

## Problem
The quick-settings brightness slider uses WMI, which covers only a laptop's internal panel; on desktops the row is
hidden (KI-021).

## Behaviour
- When no internal panel is found, the slider controls external monitors that support DDC/CI.
- Open decision: one slider for all monitors (simplest, GNOME-like) or one per monitor (spec 0002 made per-monitor
  a non-goal). Recommended: one slider, applying the same percentage to every DDC monitor.
- Monitors that don't answer within 500 ms are dropped until the card next opens.

## Non-goals
- Contrast, input switching or other VCP codes.

## Design
- `IBrightnessPanel` in front of `WmiBrightnessPanel`; new `DdcBrightnessPanel` using dxva2
  (`GetNumberOfPhysicalMonitorsFromHMONITOR`, `GetPhysicalMonitorsFromHMONITOR`, `GetMonitorBrightness`,
  `SetMonitorBrightness`, `DestroyPhysicalMonitors`) declared in `NativeMethods.TopBar.cs`.
- `BrightnessController` already runs on the thread pool and coalesces writes; DDC writes coalesce harder (only on
  drag end or at most every 250 ms) because I2C is slow and monitor EEPROMs wear.
- Core: per-monitor min/max normalisation into `BrightnessScale`, with tests for bogus ranges (max ≤ min).
- Physical monitor handles are reopened on display change.

## Safety and recovery
Brightness is a monitor setting the user changes on purpose; nothing to restore. No UI-thread I2C calls.

## Footprint
Nothing at idle; calls only while the card is open or the slider moves.

## Acceptance criteria
1. Core tests for range normalisation.
2. Manual on a DDC monitor: slider reads and sets brightness; an unsupported monitor hides the row; unplugging
   while the card is open doesn't hang the UI.

## Risks and open questions
- Docks, MST hubs and HDMI adapters often break DDC/CI.
- Some monitors take seconds per call.
