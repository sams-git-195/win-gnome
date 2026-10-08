# Known issues

The single list of known bugs, risks and limitations in WinGnome. See [AGENTS.md](../AGENTS.md#6-known-issues)
for when to add, update and close entries.

## Severity

| Severity | Meaning | Examples |
|---|---|---|
| **S1 Critical** | Leaves the user's system broken or loses data, or WinGnome can't run. Fix before anything else. | Taskbar stays hidden after exit; registry change not reverted; Explorer crashes; crash on start. |
| **S2 High** | A main feature is broken or unsafe for many users, with no reasonable workaround. | Dock doesn't show running apps; hook swallows keystrokes; window buttons close the wrong window. |
| **S3 Medium** | A feature is broken or degraded in some setups, or a workaround exists. | Wrong layout at 150% DPI; a setting doesn't apply until restart; secondary monitors unsupported. |
| **S4 Low** | Cosmetic, rare, or a documented limitation. | One-pixel misalignment; tooltip wording; behaviour Windows doesn't allow us to change. |

Status is one of *Open*, *In progress*, *By design* (a limitation we've chosen to accept, with the reason),
or *Fixed* (with the commit). When in doubt, pick the higher severity.

## Open

| ID | Severity | Area | Summary | Status |
|---|---|---|---|---|
| [KI-001](#ki-001) | S3 | Dock, Top bar | Dock and top bar appear on the primary monitor only | Open |
| [KI-002](#ki-002) | S4 | App | The executable has no app icon yet | Open |
| [KI-003](#ki-003) | S4 | Workspaces | Desktop switching relies on simulated Ctrl+Win+arrow keys | By design |
| [KI-004](#ki-004) | S4 | Overview | Super key opens Start when an elevated window has focus | By design |
| [KI-005](#ki-005) | S4 | Tray | Some tray icons only appear in the Windows tray | By design |
| [KI-006](#ki-006) | S4 | Tray | No tray icons when running elevated or alongside another tray host | By design |
| [KI-007](#ki-007) | S4 | Window buttons | Apps that draw their own title bars keep their own buttons | By design |
| [KI-008](#ki-008) | S4 | Taskbar | The native taskbar isn't restyled | By design |
| [KI-009](#ki-009) | S4 | Repo | No CI workflow, although the README says `--selftest` is used by CI | Open |

### KI-001
**Dock and top bar appear on the primary monitor only** · S3 · Dock, Top bar · Open

On multi-monitor setups the dock and top bar exist only on the primary monitor. Secondary monitors have
no top bar or dock.
*Workaround:* none; use the Windows taskbar mode if per-monitor taskbars are needed.
*Fix direction:* per-monitor top bar and dock instances with per-monitor window lists (roadmap item 1).

### KI-002
**The executable has no app icon yet** · S4 · App · Open

`WinGnome.exe` has no `ApplicationIcon`, so Explorer, Task Manager and Alt+Tab show the default icon.
The logo exists as [assets/logo/wingnome.svg](../assets/logo/wingnome.svg).
*Fix direction:* export a multi-size `.ico` (16, 20, 24, 32, 40, 48, 64, 256) from the SVG, add it as
`ApplicationIcon` in `src/WinGnome/WinGnome.csproj`, and use it for the settings window.

### KI-003
**Desktop switching relies on simulated Ctrl+Win+arrow keys** · S4 · Workspaces · By design

Windows has no public API for switching virtual desktops, so the workspace dots send Ctrl+Win+←/→ to
step between desktops. *Reason:* the alternatives are undocumented COM interfaces that change between Windows builds.

### KI-004
**Super key opens Start when an elevated window has focus** · S4 · Overview · By design

With *Super key opens the overview* on, Windows doesn't let a non-elevated low-level keyboard hook see keys
sent to elevated windows, so Start opens instead. *Reason:* WinGnome deliberately runs without elevation.

### KI-005
**Some tray icons only appear in the Windows tray** · S4 · Tray · By design

Top-bar tray icons come from apps re-registering after the *TaskbarCreated* broadcast. An app whose icon
belongs to a message-only window, or that doesn't re-register, only shows in the Windows tray. Windows 11's
own network, volume and battery icons aren't tray icons; the top bar's indicators replace them.
Balloon notifications are left to Windows.

### KI-006
**No tray icons when running elevated or alongside another tray host** · S4 · Tray · By design

WinGnome hosts tray icons only when it runs without administrator rights, and only one instance hosts them
at a time (a second copy takes over when the first exits). If another tray host such as RetroBar is running,
WinGnome doesn't compete with it.

### KI-007
**Apps that draw their own title bars keep their own buttons** · S4 · Window buttons · By design

Chrome, Edge, VS Code, Windows Terminal, WinUI 3 apps and others draw custom title bars, so traffic-light
buttons can't be overlaid reliably. They're detected and left alone.

### KI-008
**The native taskbar isn't restyled** · S4 · Taskbar · By design

In native taskbar mode WinGnome doesn't make the Windows taskbar rounded, floating or translucent.
*Reason:* that needs code injected into Explorer, which breaks with Windows updates. Tools such as
Windhawk's *Taskbar Styler* can run alongside WinGnome in native taskbar mode.

### KI-009
**No CI workflow, although the README says `--selftest` is used by CI** · S4 · Repo · Open

There's no `.github/workflows` yet, so nothing builds or tests on push.
*Fix direction:* a Windows workflow that runs `dotnet build -c Release -warnaserror`, `dotnet test` and
the self-test, triggered on push to `main` and on pull requests only.

## Resolved

| ID | Severity | Area | Summary | Fixed in |
|---|---|---|---|---|
| — | | | | |
