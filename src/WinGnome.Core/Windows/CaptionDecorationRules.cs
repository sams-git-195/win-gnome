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
        "Chrome_WidgetWin_1",            // Chrome, Edge, Electron apps (VS Code, Teams, Slack, ...)
        "CASCADIA_HOSTING_WINDOW_CLASS", // Windows Terminal
        "WinUIDesktopWin32WindowClass",  // WinUI 3 apps
        "MozillaWindowClass",            // Firefox, Thunderbird
        "Ghost",                         // DWM's "Not responding" stand-in window
    };

    /// <summary>True when windows of this class must keep their own caption buttons.</summary>
    public static bool IsSkippedClass(string? className) =>
        className is not null && SkippedClasses.Contains(className);

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
