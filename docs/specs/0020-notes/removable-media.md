# Spec 0020 WP7 (Removable Media): notes for WP9

## KNOWN_ISSUES rows

Merge into KI-091 (shared with the printer note):

- KI-091 S4 Settings: the AutoPlay handler keys (HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\
  AutoplayHandlers`: `DisableAutoplay`, `UserChosenExecuteHandlers\<Event>`, `EventHandlersDefaultSelection\<Event>`;
  handlers from `EventHandlers\<Event>` and `Handlers\<id>\Action|Provider` in HKCU and HKLM) are undocumented but
  long stable. Isolated in `AutoplayStore`. The panel shows what it reads back after every write.

New rows (WP9 picks the numbers):

- S4 Settings: only the media types in `AutoplayModel`'s event table are listed (about 18 of the ~35 events Windows
  registers); vendor-specific portable-player events and `WPD` are hidden. An event Windows knows but this machine
  has no named handler for is hidden too, except Removable drive and Memory card, which are always listed.
- S4 Settings: "Open folder" is offered for every media type, as the spec says; Windows Settings itself only offers it
  for some. If Explorer ignores it for a type (for example a DVD movie), the choice is stored but has no visible
  effect. Not checked on real media (no USB stick, memory card or disc was inserted during this work).
- S4 Settings: in shell mode (spec 0013) Explorer's AutoPlay does not run, so the choices have no effect there.

## PLAN.md Core API rows

| Type | Where | Purpose |
|---|---|---|
| `AutoplayModel.Build(events, handlersByEvent, names, chosen)` | `ControlCenter/AutoplayModel.cs` | The Removable Media rows: Removable drive and Memory card always, other known events when they have a named handler; choices = Ask what to do, Do nothing, Open folder, then installed handlers by name; a chosen handler Windows holds but no longer offers stays shown |
| `AutoplayModel.WritesFor(event, handler)` | same | The two HKCU default values Windows Settings writes (`UserChosenExecuteHandlers\<Event>` and `EventHandlersDefaultSelection\<Event>`); empty for names that can't be key names |
| `AutoplayModel.MergeNames(machine, user)` / `EventLabel(id)` | same | Handler names with the per-user registration winning; plain-language media type names (unknown events null, hidden) |
| `AutoplayChoice`, `AutoplayRow`, `AutoplayWrite` | same | Records for the above |

App layer: `Features/Settings/Panels/RemovableMedia/` (`AutoplayStore`, `RemovableMediaPanelViewModel`,
`RemovableMediaPanel`). No new Interop file (`IndirectString` and `Microsoft.Win32.Registry` only).
