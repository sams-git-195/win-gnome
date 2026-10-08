using WinGnome.Interop;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// Opens Windows shell surfaces that have no public API by sending their keyboard shortcuts. The shell handles
/// these chords globally, whichever window is in front (except elevated windows, where UIPI drops injected input).
/// </summary>
internal static class ShellShortcuts
{
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_A = 0x41;
    private const ushort VK_N = 0x4E;
    private const ushort VK_S = 0x53;
    private const ushort VK_LWIN = 0x5B;

    /// <summary>
    /// Ctrl+Esc: the native Start menu. Not a Win key tap, which WinGnome's own Super-key option may turn into the
    /// Activities overview.
    /// </summary>
    public static void OpenStartMenu() => WindowActivator.SendChord(VK_CONTROL, VK_ESCAPE);

    /// <summary>Win+N: notification centre and calendar flyout.</summary>
    public static void OpenNotificationCenter() => WindowActivator.SendChord(VK_LWIN, VK_N);

    /// <summary>Win+A: Windows' own quick settings flyout.</summary>
    public static void OpenWindowsQuickSettings() => WindowActivator.SendChord(VK_LWIN, VK_A);

    /// <summary>Win+Shift+S: Snipping Tool's screen-capture overlay.</summary>
    public static void StartScreenshot() => WindowActivator.SendChord(VK_LWIN, VK_SHIFT, VK_S);

    /// <summary>Moves <paramref name="steps"/> desktops to the right (negative = left) with Ctrl+Win+arrow.</summary>
    public static void SwitchDesktop(int steps)
    {
        var arrow = steps < 0 ? VK_LEFT : VK_RIGHT;
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            WindowActivator.SendChord(VK_CONTROL, VK_LWIN, arrow);
        }
    }
}
