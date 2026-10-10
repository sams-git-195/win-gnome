using System.Runtime.InteropServices;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// Win32 side of the taskbar window regions (spec 0023): reads, empties and removes the region of an Explorer
/// taskbar window. What to do when is decided by <see cref="TaskbarRegionPolicy"/>. Every call is plain Win32, so
/// the crash path may use it.
/// </summary>
internal static class TaskbarRegions
{
    /// <summary>The window's region type (<see cref="TaskbarRegionPolicy"/> constants); <see cref="TaskbarRegionPolicy.ErrorRegion"/> when it has none.</summary>
    public static int GetRegionType(nint hwnd)
    {
        var probe = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (probe == 0)
        {
            return TaskbarRegionPolicy.ErrorRegion;
        }

        try
        {
            return NativeMethods.GetWindowRgn(hwnd, probe);
        }
        finally
        {
            NativeMethods.DeleteObject(probe);
        }
    }

    /// <summary>
    /// Gives <paramref name="hwnd"/> an empty region. Once SetWindowRgn accepts the region the system owns it; it is
    /// deleted here only when the call fails. Failures are logged, never thrown.
    /// </summary>
    public static bool TryEmpty(nint hwnd)
    {
        var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0)
        {
            ThrottledLog.Warn("taskbar-region-create", "CreateRectRgn failed while emptying a taskbar window region");
            return false;
        }

        if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            NativeMethods.DeleteObject(region);
            ThrottledLog.Warn("taskbar-region-set", $"SetWindowRgn failed on taskbar window 0x{hwnd:X}: error {error}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes the region from <paramref name="hwnd"/> if it is empty (ours); a region of any other type is Explorer's
    /// and stays. Returns true when a region was removed.
    /// </summary>
    public static bool RemoveIfEmpty(nint hwnd)
    {
        if (GetRegionType(hwnd) != TaskbarRegionPolicy.NullRegion)
        {
            return false;
        }

        if (NativeMethods.SetWindowRgn(hwnd, 0, true) == 0)
        {
            // Also runs on the crash path, where the UI-thread-only throttled log must not be used.
            Log.Warn($"SetWindowRgn(NULL) failed on taskbar window 0x{hwnd:X}: error {Marshal.GetLastPInvokeError()}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes the empty region from every recorded handle that is still a live Explorer taskbar window with an
    /// empty region (see <see cref="TaskbarRegionPolicy.RegionsToClear"/>). Returns how many were removed.
    /// </summary>
    public static int RemoveRecorded(IEnumerable<long> recorded)
    {
        var present = new Dictionary<long, int>();
        foreach (var handle in recorded)
        {
            if (!present.ContainsKey(handle) && TaskbarController.IsExplorerTaskbarWindow((nint)handle))
            {
                present[handle] = GetRegionType((nint)handle);
            }
        }

        var removed = 0;
        foreach (var handle in TaskbarRegionPolicy.RegionsToClear(recorded, present))
        {
            if (RemoveIfEmpty((nint)handle))
            {
                removed++;
            }
        }

        return removed;
    }
}
