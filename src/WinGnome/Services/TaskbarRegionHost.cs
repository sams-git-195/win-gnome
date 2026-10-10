using System.Runtime.InteropServices;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// The Win32 and marker side of <see cref="TaskbarRegionManager"/> (spec 0023). Plain Win32 and file I/O, so the crash
/// path may use it; the throttled log members are for the UI thread only.
/// </summary>
internal sealed class TaskbarRegionHost : ITaskbarRegionHost
{
    private readonly string _settingsDirectory;

    public TaskbarRegionHost(string settingsDirectory)
    {
        _settingsDirectory = settingsDirectory;
    }

    public bool IsExplorerTaskbarWindow(long hwnd) => TaskbarController.IsExplorerTaskbarWindow((nint)hwnd);

    public IReadOnlyList<long> FindExplorerTaskbarWindows() =>
        TaskbarController.FindExplorerTaskbarWindows().Select(h => (long)h).ToList();

    public bool IsHung(long hwnd) => NativeMethods.IsHungAppWindow((nint)hwnd);

    public int GetRegionType(long hwnd)
    {
        var probe = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (probe == 0)
        {
            return TaskbarRegionPolicy.ErrorRegion;
        }

        try
        {
            return NativeMethods.GetWindowRgn((nint)hwnd, probe);
        }
        finally
        {
            NativeMethods.DeleteObject(probe);
        }
    }

    /// <summary>
    /// Once SetWindowRgn accepts the region the system owns it; it is deleted here only when the call fails. No redraw:
    /// the window is hidden anyway, and a repaint is more work for Explorer.
    /// </summary>
    public bool TryEmpty(long hwnd)
    {
        var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0)
        {
            ThrottledLog.Warn("taskbar-region-create", "CreateRectRgn failed while emptying a taskbar window region");
            return false;
        }

        if (NativeMethods.SetWindowRgn((nint)hwnd, region, false) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            NativeMethods.DeleteObject(region);
            ThrottledLog.Warn("taskbar-region-set", $"SetWindowRgn failed on taskbar window 0x{hwnd:X}: error {error}");
            return false;
        }

        return true;
    }

    public bool RemoveIfEmpty(long hwnd)
    {
        if (GetRegionType(hwnd) != TaskbarRegionPolicy.NullRegion)
        {
            return false;
        }

        if (NativeMethods.SetWindowRgn((nint)hwnd, 0, true) == 0)
        {
            // Also runs on the crash path, where the UI-thread-only throttled log must not be used.
            Log.Warn($"SetWindowRgn(NULL) failed on taskbar window 0x{hwnd:X}: error {Marshal.GetLastPInvokeError()}");
            return false;
        }

        return true;
    }

    public IReadOnlyList<long> ReadRecords() => TaskbarController.ReadEmptiedRegions(_settingsDirectory);

    public bool Record(long hwnd) => TaskbarController.RecordEmptiedRegion(_settingsDirectory, (nint)hwnd);

    public void ReplaceRecords(IReadOnlyList<long> hwnds) => TaskbarController.ReplaceEmptiedRegions(_settingsDirectory, hwnds);

    public void Info(string message) => Log.Info(message);

    public void Warn(string message) => Log.Warn(message);

    public void ThrottledInfo(string key, string message) => ThrottledLog.Info(key, message);

    public void ThrottledWarn(string key, string message) => ThrottledLog.Warn(key, message);
}
