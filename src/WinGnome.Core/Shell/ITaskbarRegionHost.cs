namespace WinGnome.Core.Shell;

/// <summary>
/// What <see cref="TaskbarRegionManager"/> needs from the system: the Win32 calls on Explorer's taskbar windows, the
/// restore marker's list of emptied windows, and logging. Handles are window handles as <see cref="long"/>.
/// </summary>
public interface ITaskbarRegionHost
{
    /// <summary>True only for a live taskbar window owned by Explorer (the shell's process): never WinGnome's own tray host window, never a reused handle.</summary>
    bool IsExplorerTaskbarWindow(long hwnd);

    /// <summary>The live Explorer taskbar windows (primary and secondary).</summary>
    IReadOnlyList<long> FindExplorerTaskbarWindows();

    /// <summary>True when the window's thread has stopped pumping messages; cross-process region calls on it would block.</summary>
    bool IsHung(long hwnd);

    /// <summary>The window's region type (<see cref="TaskbarRegionPolicy"/> constants).</summary>
    int GetRegionType(long hwnd);

    /// <summary>Gives the window an empty region. False (and logged) on failure.</summary>
    bool TryEmpty(long hwnd);

    /// <summary>Removes the window's region if it is empty. True when one was removed.</summary>
    bool RemoveIfEmpty(long hwnd);

    /// <summary>The windows the restore marker records as emptied (none when there is no marker).</summary>
    IReadOnlyList<long> ReadRecords();

    /// <summary>Adds the window to the marker's record. False when it could not be written; the region must then not be applied.</summary>
    bool Record(long hwnd);

    /// <summary>Replaces the marker's record.</summary>
    void ReplaceRecords(IReadOnlyList<long> hwnds);

    void Info(string message);

    void Warn(string message);

    /// <summary>Rate-limited by <paramref name="key"/>.</summary>
    void ThrottledInfo(string key, string message);

    /// <summary>Rate-limited by <paramref name="key"/>.</summary>
    void ThrottledWarn(string key, string message);
}
