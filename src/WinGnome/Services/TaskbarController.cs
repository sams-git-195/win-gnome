using System.IO;
using System.Runtime.InteropServices;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Tray;

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
            if (shellProcess != 0 ? NativeMethods.GetProcessId(tray) == shellProcess : !IsTrayHost(tray))
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
        NativeMethods.GetClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" && !IsTrayHost(hwnd);

    /// <summary>
    /// A taskbar window that Explorer (the process of the shell's desktop window) owns. Stricter than
    /// <see cref="IsTaskbarWindow"/>: window regions are only ever touched on windows this accepts, never on a tray
    /// host's look-alike. False while there is no shell window.
    /// </summary>
    public static bool IsExplorerTaskbarWindow(nint hwnd)
    {
        if (hwnd == 0 || !IsTaskbarWindow(hwnd))
        {
            return false;
        }

        var shell = NativeMethods.GetShellWindow();
        var shellProcess = shell == 0 ? 0 : NativeMethods.GetProcessId(shell);
        return shellProcess != 0 && NativeMethods.GetProcessId(hwnd) == shellProcess;
    }

    /// <summary>The primary and secondary taskbar windows that <see cref="IsExplorerTaskbarWindow"/> accepts.</summary>
    public static IReadOnlyList<nint> FindExplorerTaskbarWindows() =>
        FindTaskbarWindows().Where(IsExplorerTaskbarWindow).ToList();

    /// <summary>A WinGnome tray host (this process's, or another WinGnome's that currently hosts the tray).</summary>
    private static bool IsTrayHost(nint hwnd) => NativeMethods.IsOwnWindow(hwnd) || TrayHost.IsHostWindow(hwnd);

    /// <summary>
    /// Hides the taskbar, recording the original state in <paramref name="settingsDirectory"/> first.
    /// Returns false, and leaves the taskbar alone, when that recovery marker cannot be written: without it
    /// nothing could restore the taskbar after a crash.
    /// </summary>
    public static bool Hide(string settingsDirectory)
    {
        // Only record the state the user had before WinGnome ever touched it.
        if (!File.Exists(MarkerPath(settingsDirectory)) && !WriteMarker(settingsDirectory, new TaskbarMarker { WasAutoHide = IsAutoHide() }))
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
        if (!File.Exists(MarkerPath(settingsDirectory)) && !WriteMarker(settingsDirectory, new TaskbarMarker { WasAutoHide = IsAutoHide() }))
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
    /// <c>--restore-taskbar</c> undo this. Returns how many windows were actually hidden.
    /// </summary>
    public static int HideWindows()
    {
        var hidden = 0;
        foreach (var hwnd in FindTaskbarWindows())
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                hidden++;
            }
        }

        return hidden;
    }

    /// <summary>
    /// Shows the taskbar windows without changing the auto-hide setting. Returns how many were made visible
    /// (windows that were already visible are re-shown but not counted, so the peek's "showed N" line means what
    /// <see cref="HideWindows"/>'s count means: N windows changed).
    /// </summary>
    public static int ShowWindows()
    {
        var shown = 0;
        foreach (var hwnd in FindTaskbarWindows())
        {
            var wasVisible = NativeMethods.IsWindowVisible(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNA);
            if (!wasVisible)
            {
                shown++;
            }
        }

        return shown;
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
        // Explorer never sets an empty region, so any live taskbar window with one is ours to give back, recorded or not.
        TaskbarRegionManager.Sweep(new TaskbarRegionHost(settingsDirectory), marker?.EmptiedRegions ?? []);
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

    /// <summary>
    /// Records in the marker that <paramref name="handle"/> is about to get an empty region, so a crash or kill
    /// still lets the next start or <c>--restore-taskbar</c> undo it. Returns false, and the region must then not
    /// be applied, when there is no marker or it cannot be written.
    /// </summary>
    public static bool RecordEmptiedRegion(string settingsDirectory, nint handle)
    {
        var marker = ReadExistingMarker(settingsDirectory);
        return marker is not null && WriteMarker(settingsDirectory, marker.WithEmptiedRegion(handle));
    }

    /// <summary>The window handles the marker records as given an empty region (none when there is no marker).</summary>
    public static IReadOnlyList<long> ReadEmptiedRegions(string settingsDirectory) =>
        ReadExistingMarker(settingsDirectory)?.EmptiedRegions ?? [];

    /// <summary>Replaces the recorded regions (those whose removal failed stay). Does nothing when there is no marker or nothing changes.</summary>
    public static void ReplaceEmptiedRegions(string settingsDirectory, IReadOnlyList<long> handles)
    {
        var marker = ReadExistingMarker(settingsDirectory);
        if (marker is not null && !marker.EmptiedRegions.SequenceEqual(handles))
        {
            WriteMarker(settingsDirectory, marker.WithEmptiedRegions(handles));
        }
    }

    private static TaskbarMarker? ReadExistingMarker(string settingsDirectory)
    {
        var path = MarkerPath(settingsDirectory);
        return File.Exists(path) ? ReadMarker(path) : null;
    }

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

    private static bool WriteMarker(string directory, TaskbarMarker marker)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // Written beside and moved over, so a kill mid-write never leaves a truncated marker (which would read as "restore to always visible").
            var path = MarkerPath(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, marker.Serialize());
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not write taskbar marker", ex);
            return false;
        }
    }

    private static TaskbarMarker? ReadMarker(string path)
    {
        try
        {
            var marker = TaskbarMarker.Parse(File.ReadAllText(path));
            if (marker is null)
            {
                Log.Warn("Taskbar marker unreadable; restoring to always visible");
            }

            return marker;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Taskbar marker unreadable; restoring to always visible", ex);
            return null;
        }
    }
}
