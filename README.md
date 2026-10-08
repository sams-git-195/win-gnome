# WinGnome

**Make Windows 11 feel like GNOME (with a dash of macOS).**

WinGnome is a lightweight shell companion for Windows 11. It adds a GNOME-style top bar, a dock in the
style of Dash-to-Dock or macOS, round "traffic light" window buttons, an Activities overview, and a set of
reversible "streamline" tweaks. Everything is optional and configurable, and WinGnome puts the system back
the way it was when it exits.

> Status: early (v0.1). Built for Windows 11 23H2 and later. Windows 10 mostly works, but it isn't tested.

---

## Features

### Top bar
- **Activities** button (or press **Alt+F1**, the Super key, or flick the pointer into the top-left hot corner) opens the overview.
- **Workspace dots** for Windows virtual desktops. Click a dot to switch desktops.
- **Focused app name** next to the dots, as in GNOME.
- **Centred clock**, formatted GNOME-style (`Wed 8 Oct  14:05`). Click it for a calendar.
- **System indicators**: network, volume (scroll on it to change the volume) and battery.
- **Quick settings** menu: volume slider, Wi-Fi and Bluetooth shortcuts, screenshot, Windows Settings, WinGnome settings, lock, and the power menu (sleep, restart, shut down, sign out). It also links to the hidden system tray and to Windows' own quick settings and notification centre.
- Registered as an AppBar, so maximised windows sit below the bar instead of under it. The bar hides automatically when a full-screen app runs.
- **Appearance**: you can set the background colour, text colour, opacity (0–100%), **blur or acrylic**, height, text size, and a *floating* mode with a margin and rounded corners. The default is the classic solid black GNOME bar.

### Dock
- Pinned apps plus running apps, with **running-indicator dots** (one per window, up to four).
- **Click** focuses an app, or minimises it if it's already focused. Other click actions: *cycle windows* or *show previews*.
- **Middle-click** opens a new window. **Right-click** lists the app's windows plus *New window*, *Pin/Unpin* and *Quit*.
- **Super+1…9** activates the n-th dock item.
- **Visibility modes**: *Always visible* (reserves screen space), *Intellihide* (hides only when a window overlaps it, the Ubuntu default) and *Autohide*.
- Optional macOS-style **hover magnification**, panel mode (stretch to the screen edges), a *Show Applications* button and a recycle bin.
- Bottom, left or right placement.
- **Appearance**: you can set the background colour (or follow the theme), opacity, **blur or acrylic** (the default), app icon size, icon spacing, distance from the screen edge, corner radius, magnification and the colour of the running-indicator dots. The settings pages show a live preview.
- Drag `.exe` or `.lnk` files onto the dock to pin them.

### Traffic-light window buttons
- Round **close, minimise and maximise** buttons drawn over normal windows' title bars.
- Colour **presets**: macOS, GNOME (Adwaita), Graphite, Pastel, or **Custom** with your own three colours.
- Left or right placement, macOS or Windows button order, adjustable size and spacing.
- Glyphs (× − +) appear on hover, and inactive windows can be dimmed, as on macOS.
- **Unified title bars**: optionally paints every decorated window's title bar in an Adwaita header colour so the round buttons blend in seamlessly. The original colours come back on exit.
- Per-app exclusions. Apps that draw their own title bars (Chrome, Edge, VS Code, Windows Terminal, WinUI 3 apps) are left alone automatically.

### Activities overview
- Full-screen overview with **live window thumbnails** (DWM). Click a thumbnail to focus that window, or hover and click × to close it.
- **Type to search** apps and windows. Press Enter to launch the top hit and Esc to close.
- **Application grid** (from the dock's *Show Applications* button). Right-click an app to pin it to the dock.
- **Hot corner**: top-left, with a configurable delay.
- Optional: **Super key alone opens the overview** instead of the Start menu. Win+X, Win+E and other Win+key shortcuts keep working.

### Streamline
- **Centre new windows** (GNOME behaviour).
- **Focus follows mouse** (X-Mouse style). This lasts for the session only and is restored on exit.
- **Taskbar mode** (Settings → General):
  - **WinGnome dock** (default): the Windows taskbar is hidden while WinGnome runs. *Show system tray* in quick settings temporarily reveals it so you can reach tray icons.
  - **Native Windows taskbar**: the Windows taskbar stays, with its tray icons, Start button and jump lists, and the WinGnome dock is turned off. You can optionally set the taskbar to auto-hide and streamline it with the *Taskbar* tweaks: hide Search, Task View and Copilot, and centre the icons. These use only supported Windows settings, with no code injected into Explorer, so they keep working across Windows updates.
- Reversible **registry tweaks** (current user only, no admin needed). Each tweak backs up the original value and can be reverted one at a time or all at once:
  - Dark mode for apps and the system
  - Classic (full) right-click context menu
  - No web results in Start search
  - No Start menu recommendations
  - No "suggestions", ads or auto-installed apps
  - Disable Aero Shake
  - Show file extensions
  - Disable the snap-layouts hover flyout
  - Open File Explorer to This PC

---

## Getting started

### Requirements
- Windows 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build, or the .NET 8 Desktop Runtime to run a build

### Build and run
```bash
dotnet build -c Release
```
```bash
dotnet run --project src/WinGnome -c Release
```

### Publish a single folder
```bash
dotnet publish src/WinGnome -c Release -r win-x64 --self-contained false -o publish
```
Then run `publish\WinGnome.exe`. Turn on **Settings → General → Start with Windows** to launch it at sign-in.

### Opening settings
WinGnome has no tray icon. Open settings from the top bar's quick-settings menu (the system indicators at the
top right), or run `WinGnome.exe` again: a second launch opens the running instance's settings.

---

## Command line

| Switch | Effect |
|---|---|
| `--settings` | Open the settings window on start |
| `--settings-dir <path>` | Use a separate profile folder (settings, tweak backups, log) |
| `--safe` | Safe mode: no taskbar hiding, no registry writes, no keyboard hooks |
| `--selftest` | Start every feature in safe mode, run for 5 s, exit with code 0 on success (used by CI and QA) |
| `--restore-taskbar` | Restore the Windows taskbar (after a crash, for example) and exit |

## Safety and recovery

WinGnome changes as little as possible, and it undoes everything it changes:

- **Taskbar**: the original auto-hide state is saved to `%APPDATA%\WinGnome\taskbar.state` *before* the taskbar is hidden. It is restored on exit, on crash, and on the next start if WinGnome was killed. As a last resort, run `WinGnome.exe --restore-taskbar`, or restart Explorer from Task Manager.
- **Title-bar colours and focus-follows-mouse** last for the session only. They are restored on exit or crash. If WinGnome is force-killed, they are restored on its next start, because WinGnome records what it changed before changing it.
- **Quitting**: use *Quit WinGnome* in quick settings or Settings → About. `taskkill /im WinGnome.exe` (without `/f`) also quits cleanly.
- **Registry tweaks** only touch `HKEY_CURRENT_USER`. The original values (including "value did not exist") are stored in `tweaks-backup.json`, and **Settings → Streamline → Revert all** restores them.
- WinGnome never touches elevated (administrator) windows.
- Log file: `%APPDATA%\WinGnome\wingnome.log`.

## Known limitations

- Tray icons can't be re-hosted in the top bar. Use *Show system tray* in the quick-settings menu to peek at the native tray, or switch to native taskbar mode.
- WinGnome doesn't restyle the native taskbar itself (rounded, floating or translucent). That would mean injecting code into Explorer, which breaks with Windows updates. If you want that, tools like Windhawk's *Taskbar Styler* can run alongside WinGnome in native taskbar mode.
- Apps that draw their own title bars keep their own buttons.
- The dock and top bar appear on the primary monitor only (multi-monitor support is on the roadmap).
- Workspace switching works by sending Ctrl+Win+←/→, because Windows has no public API for switching virtual desktops.
- If the Super-key option is on and an elevated window has focus, Windows opens Start instead. Windows blocks hooks from seeing keys sent to elevated windows.

## Recommendations and roadmap

Ideas for making Windows feel even more like GNOME, roughly in order of value:

1. **Multi-monitor**: a top bar and dock on every monitor, with per-monitor window lists.
2. **Workspace thumbnails** in the overview, plus drag-a-window-to-another-desktop.
3. **Super+arrow quarter tiling** and Super+drag to move or resize windows (GNOME and KDE muscle memory).
4. **Notification list** inside the calendar popup, as in GNOME's message tray.
5. **App folders** in the application grid, and drag-to-reorder in the dock.
6. **File search** in the overview (Windows Search via `search-ms:`).
7. **Wallpaper-aware accent colour** and an Adwaita accent palette for the top bar.
8. **Night Light and Do Not Disturb toggles** in quick settings.
9. An **MSIX package** and winget manifest for one-click install and auto-update.

Tips that pair well with WinGnome:
- Install the **Cantarell** or **Inter** font if you want the full GNOME typography. WinGnome uses Segoe UI Variable by default.
- Set a dark wallpaper and switch on the *Dark mode* tweak for the classic Adwaita-dark look.
- Use PowerToys **FancyZones** if you want more tiling than the built-in snap layouts.

---

## Architecture

```
src/WinGnome.Core          Pure logic (net8.0): settings, layout maths, filtering, search,
                           tweak bookkeeping, state machines. No Win32, fully unit-tested.
src/WinGnome               WPF app (net8.0-windows): interop, services and features.
  Infrastructure/          Settings service, logging, feature discovery, MVVM helpers
  Interop/                 P/Invoke (NativeMethods.*.cs), WinEvent hooks, AppBar, COM interfaces
  Services/                WindowTracker, TaskbarController, Apps (catalogue, icons, launcher)
  Features/                TopBar, Dock, Taskbar, WindowButtons, Overview, Behaviour, Settings
  Theme/                   Adwaita light and dark palettes and shared control styles
tests/WinGnome.Core.Tests  xUnit tests for Core
docs/PLAN.md               Engineering plan and conventions
```

Each feature implements `IFeature` and is discovered automatically. Features never reference each other:
they talk through `ShellCommands`, so a crash in one feature can't take the others down.

### Running the tests
```bash
dotnet test
```

## Contributing

Issues and pull requests are welcome. Please keep platform calls in `src/WinGnome` and logic in
`WinGnome.Core` with tests, and run `dotnet build -warnaserror` and `dotnet test` before submitting.
