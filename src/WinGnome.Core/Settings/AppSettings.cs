namespace WinGnome.Core.Settings;

/// <summary>
/// Root of the persisted user configuration. Every property has a safe default so a
/// missing or partial settings file always produces a usable configuration.
/// Call <see cref="Normalize"/> after deserialising to clamp out-of-range values.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Bumped when the settings shape changes in a way that needs migration.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public const int CurrentSchemaVersion = 1;

    public GeneralSettings General { get; set; } = new();
    public TopBarSettings TopBar { get; set; } = new();
    public DockSettings Dock { get; set; } = new();
    public WindowButtonSettings WindowButtons { get; set; } = new();
    public ActivitiesSettings Activities { get; set; } = new();

    /// <summary>Ids of streamline tweaks the user has switched on (see Tweaks.TweakCatalog).</summary>
    public List<string> EnabledTweaks { get; set; } = [];

    /// <summary>Clamps numbers into sane ranges and repairs null collections. Returns this instance.</summary>
    public AppSettings Normalize()
    {
        General ??= new();
        TopBar ??= new();
        Dock ??= new();
        WindowButtons ??= new();
        Activities ??= new();
        EnabledTweaks = (EnabledTweaks ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        TopBar.Normalize();
        Dock.Normalize();
        WindowButtons.Normalize();
        Activities.Normalize();
        SchemaVersion = CurrentSchemaVersion;
        return this;
    }

    /// <summary>Deep copy via the JSON serializer so callers can edit a draft safely.</summary>
    public AppSettings Clone() => SettingsSerializer.Deserialize(SettingsSerializer.Serialize(this));
}

public enum ThemeMode { System, Light, Dark }

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }

    /// <summary>Hide the native Windows taskbar while WinGnome runs (restored on exit).</summary>
    public bool HideWindowsTaskbar { get; set; } = true;

    /// <summary>Colour scheme for WinGnome's own surfaces (top bar menus, dock, overview, settings).</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>GNOME "Center new windows": centre newly opened top-level windows on their monitor.</summary>
    public bool CenterNewWindows { get; set; }

    /// <summary>X-Mouse style focus-follows-mouse. Applied for the session only and restored on exit.</summary>
    public bool FocusFollowsMouse { get; set; }
}

public enum ClockStyle { TwentyFourHour, TwelveHour }

public sealed class TopBarSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Height in device-independent pixels.</summary>
    public double Height { get; set; } = 32;

    /// <summary>0..1 background opacity. GNOME's bar is solid black (1.0).</summary>
    public double Opacity { get; set; } = 1.0;

    public string BackgroundColor { get; set; } = "#000000";

    public bool ShowActivitiesButton { get; set; } = true;
    public bool ShowWorkspaceIndicator { get; set; } = true;
    public bool ShowFocusedAppName { get; set; } = true;

    public ClockStyle ClockStyle { get; set; } = ClockStyle.TwentyFourHour;
    public bool ShowDate { get; set; } = true;
    public bool ShowWeekday { get; set; } = true;
    public bool ShowSeconds { get; set; }

    public bool ShowBatteryPercentage { get; set; } = true;

    internal void Normalize()
    {
        Height = Math.Clamp(double.IsFinite(Height) ? Height : 32, 24, 48);
        Opacity = Math.Clamp(double.IsFinite(Opacity) ? Opacity : 1, 0.3, 1);
        if (!Theming.HexColor.TryParse(BackgroundColor, out _))
        {
            BackgroundColor = "#000000";
        }
    }
}

public enum DockPosition { Bottom, Left, Right }

public enum DockVisibility
{
    /// <summary>Always shown; reserves screen space so maximised windows don't cover it.</summary>
    AlwaysVisible,
    /// <summary>Hides only when the focused window overlaps the dock (Dash-to-Dock / Ubuntu default).</summary>
    Intellihide,
    /// <summary>Hidden until the pointer touches the screen edge.</summary>
    Autohide,
}

public enum DockClickAction
{
    /// <summary>Focus the app; if it is already focused, minimise it.</summary>
    FocusOrMinimize,
    /// <summary>Focus the app; repeated clicks cycle through its windows.</summary>
    Cycle,
    /// <summary>Open the activities overview filtered to the app's windows when it has several.</summary>
    Previews,
}

public sealed class DockSettings
{
    public bool Enabled { get; set; } = true;
    public DockPosition Position { get; set; } = DockPosition.Bottom;
    public DockVisibility Visibility { get; set; } = DockVisibility.Intellihide;

    /// <summary>Icon size in device-independent pixels.</summary>
    public double IconSize { get; set; } = 48;

    /// <summary>macOS-style hover magnification. 1.0 disables it.</summary>
    public double Magnification { get; set; } = 1.0;

    /// <summary>0..1 background opacity.</summary>
    public double Opacity { get; set; } = 0.75;

    /// <summary>Stretch across the whole edge (Ubuntu panel mode) instead of a centred floating dock.</summary>
    public bool ExtendToEdges { get; set; }

    public bool ShowRunningIndicators { get; set; } = true;
    public bool ShowAppsButton { get; set; } = true;
    public bool ShowRecycleBin { get; set; }

    /// <summary>Only show running apps that are not pinned when true; when false the dock is a pure launcher.</summary>
    public bool ShowRunningApps { get; set; } = true;

    public DockClickAction ClickAction { get; set; } = DockClickAction.FocusOrMinimize;

    /// <summary>Pinned launchers in display order.</summary>
    public List<PinnedApp> PinnedApps { get; set; } = DefaultPinnedApps();

    public static List<PinnedApp> DefaultPinnedApps() =>
    [
        new() { Name = "File Explorer", LaunchId = "Microsoft.Windows.Explorer" },
        new() { Name = "Microsoft Edge", LaunchId = "MSEdge" },
        new() { Name = "Terminal", LaunchId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" },
        new() { Name = "Settings", LaunchId = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel" },
    ];

    internal void Normalize()
    {
        IconSize = Math.Clamp(double.IsFinite(IconSize) ? IconSize : 48, 24, 128);
        Magnification = Math.Clamp(double.IsFinite(Magnification) ? Magnification : 1, 1, 2);
        Opacity = Math.Clamp(double.IsFinite(Opacity) ? Opacity : 0.75, 0, 1);
        PinnedApps = (PinnedApps ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.LaunchId))
            .DistinctBy(p => p.LaunchId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var app in PinnedApps)
        {
            app.Name = string.IsNullOrWhiteSpace(app.Name) ? app.LaunchId : app.Name.Trim();
        }
    }
}

/// <summary>A launcher pinned to the dock.</summary>
public sealed class PinnedApp
{
    public string Name { get; set; } = "";

    /// <summary>
    /// What to launch. Either an AppUserModelID / shell:AppsFolder parsing name
    /// (e.g. "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App") or a file-system path to an
    /// executable or shortcut. Known-folder prefixed paths ("{GUID}\app.exe") are allowed.
    /// </summary>
    public string LaunchId { get; set; } = "";

    /// <summary>Optional command-line arguments (file-system launches only).</summary>
    public string? Arguments { get; set; }
}

public enum TrafficLightPreset { MacOS, Gnome, Graphite, Pastel, Custom }

public enum ButtonSide { Right, Left }

public enum ButtonOrder
{
    /// <summary>minimise, maximise, close — Windows / GNOME order.</summary>
    MinimizeMaximizeClose,
    /// <summary>close, minimise, maximise — macOS order.</summary>
    CloseMinimizeMaximize,
}

public sealed class WindowButtonSettings
{
    public bool Enabled { get; set; } = true;
    public TrafficLightPreset Preset { get; set; } = TrafficLightPreset.MacOS;

    /// <summary>Colours used when <see cref="Preset"/> is Custom ("#RRGGBB").</summary>
    public string CloseColor { get; set; } = "#FF5F57";
    public string MinimizeColor { get; set; } = "#FEBC2E";
    public string MaximizeColor { get; set; } = "#28C840";

    /// <summary>Which side of the title bar the buttons sit on. Right replaces the native buttons in place.</summary>
    public ButtonSide Side { get; set; } = ButtonSide.Right;
    public ButtonOrder Order { get; set; } = ButtonOrder.MinimizeMaximizeClose;

    /// <summary>Circle diameter in device-independent pixels.</summary>
    public double Diameter { get; set; } = 14;

    /// <summary>Gap between circles in device-independent pixels.</summary>
    public double Spacing { get; set; } = 8;

    /// <summary>Show ×, −, + glyphs only while hovering the group (macOS behaviour).</summary>
    public bool ShowSymbolsOnHover { get; set; } = true;

    /// <summary>Grey out the buttons of windows that are not focused (macOS behaviour).</summary>
    public bool DimInactiveWindows { get; set; } = true;

    /// <summary>
    /// Paint every decorated window's title bar a uniform Adwaita-style colour (via DWM caption colour).
    /// Gives a consistent GNOME look and lets the round buttons blend in perfectly. Restored on exit.
    /// </summary>
    public bool UnifyTitleBarColor { get; set; } = true;

    /// <summary>Process names (without .exe, case-insensitive) that keep their native buttons.</summary>
    public List<string> ExcludedProcesses { get; set; } = [];

    internal void Normalize()
    {
        Diameter = Math.Clamp(double.IsFinite(Diameter) ? Diameter : 14, 8, 24);
        Spacing = Math.Clamp(double.IsFinite(Spacing) ? Spacing : 8, 2, 20);
        CloseColor = NormalizeColor(CloseColor, "#FF5F57");
        MinimizeColor = NormalizeColor(MinimizeColor, "#FEBC2E");
        MaximizeColor = NormalizeColor(MaximizeColor, "#28C840");
        ExcludedProcesses = (ExcludedProcesses ?? [])
            .Select(p => p?.Trim() ?? "")
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeColor(string? value, string fallback) =>
        Theming.HexColor.TryParse(value, out var color) ? color.ToString() : fallback;
}

public sealed class ActivitiesSettings
{
    /// <summary>Open the overview when the pointer hits the top-left corner.</summary>
    public bool HotCorner { get; set; } = true;

    /// <summary>Milliseconds the pointer must rest in the corner before triggering.</summary>
    public int HotCornerDelayMs { get; set; } = 150;

    /// <summary>Pressing and releasing the Windows key alone opens the overview instead of Start.</summary>
    public bool SuperKeyOpensOverview { get; set; }

    /// <summary>Super+1..9 focuses or launches the n-th dock item (GNOME behaviour).</summary>
    public bool SuperNumberActivatesDock { get; set; } = true;

    /// <summary>Global hotkey, e.g. "Alt+F1" (always active in addition to the Super key option).</summary>
    public string Hotkey { get; set; } = "Alt+F1";

    /// <summary>Dim/blur strength of the overview backdrop, 0..1.</summary>
    public double BackdropOpacity { get; set; } = 0.85;

    internal void Normalize()
    {
        HotCornerDelayMs = Math.Clamp(HotCornerDelayMs, 0, 2000);
        BackdropOpacity = Math.Clamp(double.IsFinite(BackdropOpacity) ? BackdropOpacity : 0.85, 0.2, 1);
        if (string.IsNullOrWhiteSpace(Hotkey) || !Input.Hotkey.TryParse(Hotkey, out _))
        {
            Hotkey = "Alt+F1";
        }
    }
}
