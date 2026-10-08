using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// Hides and restores the native Windows taskbar(s).
/// </summary>
/// <remarks>
/// Hiding is two-step: the taskbar is switched to auto-hide (which frees the work area; hiding the
/// window alone would leave a dead strip) and its windows are hidden. The previous auto-hide state is
/// written to a marker file before anything changes, so <see cref="RestoreFromMarker"/> can undo the
/// change after a crash or via <c>WinGnome.exe --restore-taskbar</c>.
/// </remarks>
internal static partial class TaskbarController
{
    private const string MarkerFileName = "taskbar.state";
    private const uint ABM_GETSTATE = 0x04;
    private const uint ABM_SETSTATE = 0x0A;
    private const int ABS_AUTOHIDE = 0x01;
    private const int ABS_ALWAYSONTOP = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    private static partial nuint SHAppBarMessage(uint message, ref APPBARDATA data);

    private sealed record Marker(bool WasAutoHide);

    /// <summary>
    /// Explorer's own primary taskbar window, or 0. Not simply <c>FindWindow("Shell_TrayWnd")</c>: WinGnome's tray host
    /// (and tools like RetroBar) register windows of that class in front of Explorer's so that apps' tray icons reach
    /// them first. Explorer's is the one in the same process as the shell's desktop window.
    /// </summary>
    public static nint FindExplorerTray()
    {
        var shell = NativeMethods.GetShellWindow();
        var shellProcess = shell == 0 ? 0 : NativeMethods.GetProcessId(shell);
        nint tray = 0;
        while ((tray = NativeMethods.FindWindowEx(0, tray, "Shell_TrayWnd", null)) != 0)
        {
            // Without a desktop window (Explorer still starting), take the first tray that is not one of ours.
            if (shellProcess != 0 ? NativeMethods.GetProcessId(tray) == shellProcess : !NativeMethods.IsOwnWindow(tray))
            {
                return tray;
            }
        }

        return 0;
    }

    /// <summary>The primary and secondary taskbar window handles currently present.</summary>
    public static IReadOnlyList<nint> FindTaskbarWindows()
    {
        var result = new List<nint>();
        var primary = FindExplorerTray();
        if (primary != 0)
        {
            result.Add(primary);
        }

        nint secondary = 0;
        while ((secondary = NativeMethods.FindWindowEx(0, secondary, "Shell_SecondaryTrayWnd", null)) != 0)
        {
            result.Add(secondary);
        }

        return result;
    }

    public static bool IsTaskbarWindow(nint hwnd) =>
        NativeMethods.GetClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" && !NativeMethods.IsOwnWindow(hwnd);

    /// <summary>
    /// Hides the taskbar, recording the original state in <paramref name="settingsDirectory"/> first.
    /// Returns false, and leaves the taskbar alone, when that recovery marker cannot be written: without it
    /// nothing could restore the taskbar after a crash.
    /// </summary>
    public static bool Hide(string settingsDirectory)
    {
        // Only record the state the user had before WinGnome ever touched it.
        if (!File.Exists(MarkerPath(settingsDirectory)) && !WriteMarker(settingsDirectory, new Marker(IsAutoHide())))
        {
            Log.Warn("Not hiding the taskbar because its restore marker could not be written");
            return false;
        }

        SetAutoHide(true);
        HideWindows();
        return true;
    }

    /// <summary>
    /// "Native taskbar" mode: switches the taskbar to auto-hide but leaves (or makes) its windows visible,
    /// recording the original state in the same marker as <see cref="Hide"/>, so <see cref="RestoreFromMarker"/>
    /// undoes it. Returns false, and changes nothing, when that marker cannot be written.
    /// </summary>
    public static bool SetAutoHideOnly(string settingsDirectory)
    {
        if (!File.Exists(MarkerPath(settingsDirectory)) && !WriteMarker(settingsDirectory, new Marker(IsAutoHide())))
        {
            Log.Warn("Not auto-hiding the taskbar because its restore marker could not be written");
            return false;
        }

        ShowWindows();
        SetAutoHide(true);
        return true;
    }

    /// <summary>
    /// Hides the taskbar windows again (Explorer re-shows them on some events). Only call while a successful
    /// <see cref="Hide"/> is in effect (<see cref="HasMarker"/>): its marker is what lets a crash or
    /// <c>--restore-taskbar</c> undo this.
    /// </summary>
    public static void HideWindows()
    {
        foreach (var hwnd in FindTaskbarWindows())
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            }
        }
    }

    /// <summary>Shows the taskbar windows without changing the auto-hide setting.</summary>
    public static void ShowWindows()
    {
        foreach (var hwnd in FindTaskbarWindows())
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
        }
    }

    /// <summary>
    /// Restores the taskbar to the state recorded in the marker file (if any) and deletes the marker.
    /// Safe to call at any time, including from crash handlers. Returns true when a restore happened.
    /// </summary>
    public static bool RestoreFromMarker(string settingsDirectory)
    {
        var path = MarkerPath(settingsDirectory);
        if (!File.Exists(path))
        {
            return false;
        }

        var marker = ReadMarker(path);
        ShowWindows();
        SetAutoHide(marker?.WasAutoHide ?? false);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not delete taskbar marker", ex);
        }

        Log.Info("Taskbar restored");
        return true;
    }

    /// <summary>
    /// True while a restore marker exists, i.e. while WinGnome is allowed to keep the taskbar hidden or auto-hidden.
    /// It disappears when anything restores the taskbar (settings page, <c>--restore-taskbar</c>, a crash handler).
    /// </summary>
    public static bool HasMarker(string settingsDirectory) => File.Exists(MarkerPath(settingsDirectory));

    public static bool IsAutoHide()
    {
        var data = NewData();
        return ((int)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    private static void SetAutoHide(bool autoHide)
    {
        var data = NewData();
        data.lParam = autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP;
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    private static APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = FindExplorerTray(),
    };

    private static string MarkerPath(string directory) => Path.Combine(directory, MarkerFileName);

    private static bool WriteMarker(string directory, Marker marker)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(MarkerPath(directory), JsonSerializer.Serialize(marker));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not write taskbar marker", ex);
            return false;
        }
    }

    private static Marker? ReadMarker(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("Taskbar marker unreadable; restoring to always visible", ex);
            return null;
        }
    }
}
