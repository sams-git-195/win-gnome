namespace WinGnome.Core.Windows;

/// <summary>
/// Traffic-light specific rules that complement <see cref="WindowFilter.CanDecorate"/>: which windows have
/// native caption buttons we can faithfully cover.
/// </summary>
public static class CaptionDecorationRules
{
    /// <summary>
    /// Window classes that are never decorated. These apps draw their own title bar and caption buttons, so the
    /// rectangle DWM reports (if any) does not match what is on screen, or the window is DWM's stand-in for a
    /// hung app.
    /// </summary>
    private static readonly HashSet<string> SkippedClasses = new(StringComparer.Ordinal)
    {
        "CASCADIA_HOSTING_WINDOW_CLASS", // Windows Terminal
        "WinUIDesktopWin32WindowClass",  // WinUI 3 apps
        "MozillaWindowClass",            // Firefox, Thunderbird
        "Ghost",                         // DWM's "Not responding" stand-in window
    };

    /// <summary>
    /// Every Chromium window class (Chrome_WidgetWin_0, _1, ...): Chrome, Edge and Electron apps (VS Code, Teams,
    /// Docker Desktop). Their DWM caption-button rectangle is empty or stale (Docker reports 146×22).
    /// </summary>
    private const string ChromiumClassPrefix = "Chrome_WidgetWin_";

    /// <summary>Apps that draw their caption buttons in HTML, with the width of one button in DIPs.</summary>
    private static readonly WebButtonProfile[] WebButtonProfiles =
    [
        new("GitHubDesktop", "Chrome_WidgetWin_1", 45),
    ];

    /// <summary>True when windows of this class must keep their own caption buttons.</summary>
    public static bool IsSkippedClass(string? className) =>
        className is not null
        && (SkippedClasses.Contains(className) || className.StartsWith(ChromiumClassPrefix, StringComparison.Ordinal));

    /// <summary>
    /// True when a skipped window may still be decorated after asking it where its buttons are
    /// (<see cref="CaptionHitTestProbe"/>), with the experimental custom-title-bar setting on. DWM's hung-window
    /// stand-in is never probed.
    /// </summary>
    public static bool CanProbeSkippedClass(string? className) =>
        IsSkippedClass(className) && !string.Equals(className, "Ghost", StringComparison.Ordinal);

    /// <summary>
    /// The width in DIPs of each caption button an app draws in HTML, for the few apps WinGnome knows (spec 0009).
    /// Such apps answer HTCLIENT over their buttons, so only a known button size makes their hit-test row usable
    /// (see <see cref="CaptionHoleProbe"/>).
    /// </summary>
    /// <param name="processName">The executable's file name without extension (case-insensitive).</param>
    /// <param name="className">The top-level window class (case-sensitive).</param>
    /// <returns>The button width, or null when the app has no profile.</returns>
    public static double? WebButtonWidth(string? processName, string? className)
    {
        foreach (var profile in WebButtonProfiles)
        {
            if (string.Equals(profile.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(profile.ClassName, className, StringComparison.Ordinal))
            {
                return profile.ButtonWidth;
            }
        }

        return null;
    }

    /// <summary><see cref="WebButtonWidth(string?, string?)"/> for a tracked window (by its executable's file name).</summary>
    public static double? WebButtonWidth(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return WebButtonWidth(Shell.PathText.FileNameWithoutExtension(window.ProcessPath), window.ClassName);
    }

    /// <summary>
    /// True when Windows draws the full minimise/maximise/close trio. With neither WS_MINIMIZEBOX nor
    /// WS_MAXIMIZEBOX only a lone close button is drawn, and three circles would not fit its space.
    /// </summary>
    public static bool HasButtonTrio(bool hasMinimizeBox, bool hasMaximizeBox) => hasMinimizeBox || hasMaximizeBox;

    /// <summary>
    /// True when Windows bitmap-stretches the window (a DPI-unaware or system-aware app on a monitor whose DPI
    /// differs from the one the app renders at). DWM reports such windows' caption buttons in a mix of logical
    /// and physical units, so an overlay cannot be placed over them reliably.
    /// </summary>
    /// <param name="windowDpi">GetDpiForWindow of the window (0 when unknown).</param>
    /// <param name="monitorDpi">Effective DPI of the monitor the window is on (0 when unknown).</param>
    public static bool IsDpiVirtualized(uint windowDpi, uint monitorDpi) =>
        windowDpi != 0 && monitorDpi != 0 && windowDpi != monitorDpi;
}

/// <summary>An app whose caption buttons are HTML: matched by executable name and window class.</summary>
internal readonly record struct WebButtonProfile(string ProcessName, string ClassName, double ButtonWidth);
