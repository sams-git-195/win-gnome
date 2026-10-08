using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using WinGnome.Core.Theming;
using WinGnome.Interop;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Paints other apps' DWM caption (title bar background and title text) and remembers every window it touched,
/// so the system colours can always be put back: when a window stops being decorated, when the feature is
/// switched off, on shutdown, from the crash handler, and on the next start if WinGnome was killed.
/// </summary>
/// <remarks>
/// <para>
/// DWM cannot report a window's current caption colour (DwmGetWindowAttribute returns E_INVALIDARG for
/// DWMWA_CAPTION_COLOR), so "restore" means DWMWA_COLOR_DEFAULT: the system-drawn colour.
/// </para>
/// <para>
/// A killed process runs no handler, and DWM keeps the colours for as long as the windows live. So every
/// recoloured window (with its process id, because handles are recycled) is also listed in a marker file before
/// it is touched; a clean exit deletes the file and the next start resets whatever it still lists.
/// </para>
/// </remarks>
internal sealed class CaptionColorizer
{
    private const string MarkerFileName = "caption-colors.state";

    // Window -> caption COLORREF we applied. Concurrent because EmergencyRestore may run on any thread
    // while the UI thread is wedged; enumerating a ConcurrentDictionary never blocks.
    private readonly ConcurrentDictionary<nint, uint> _applied = new();

    // Every window recoloured since the marker was last cleared (UI thread only), in marker-file form.
    private readonly Dictionary<nint, uint> _marked = [];
    private readonly string? _markerPath;

    /// <param name="settingsDirectory">Profile folder for the marker file, or null to keep no marker.</param>
    public CaptionColorizer(string? settingsDirectory)
    {
        _markerPath = settingsDirectory is null ? null : Path.Combine(settingsDirectory, MarkerFileName);
    }

    /// <summary>
    /// Resets the caption colours a previous, killed WinGnome left behind (windows that still exist and still
    /// belong to the same process), then deletes the marker.
    /// </summary>
    /// <returns>How many windows were reset.</returns>
    public int RestoreAfterUncleanExit()
    {
        if (_markerPath is null || !File.Exists(_markerPath))
        {
            return 0;
        }

        var restored = 0;
        try
        {
            foreach (var line in File.ReadAllLines(_markerPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle)
                    && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                    && NativeMethods.IsWindow((nint)handle)
                    && NativeMethods.GetProcessId((nint)handle) == pid)
                {
                    Reset((nint)handle);
                    restored++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThrottledLog.Warn("caption-marker", $"Could not read {_markerPath}: {ex.Message}");
        }

        DeleteMarker();
        _marked.Clear();
        return restored;
    }

    /// <summary>Sets the caption and title colours of <paramref name="hwnd"/>; a no-op when already applied.</summary>
    /// <returns>False when DWM refused (the window is gone or does not support caption colours).</returns>
    public bool Apply(nint hwnd, HexColor caption, HexColor text)
    {
        var captionRef = TitleBarPalette.ToColorRef(caption);
        if (_applied.TryGetValue(hwnd, out var current) && current == captionRef)
        {
            return true;
        }

        var textRef = TitleBarPalette.ToColorRef(text);

        // Record the window before touching it so a crash or kill between the two calls still restores it.
        _applied[hwnd] = captionRef;
        Mark(hwnd);
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref captionRef, sizeof(uint));
        if (hr == 0)
        {
            hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref textRef, sizeof(uint));
        }

        if (hr != 0)
        {
            Reset(hwnd);
            _applied.TryRemove(hwnd, out _);
            ThrottledLog.Warn("caption-color", $"DwmSetWindowAttribute(caption colour) failed for 0x{hwnd:X}: 0x{hr:X8}");
            return false;
        }

        return true;
    }

    /// <summary>Puts back the system caption colours of <paramref name="hwnd"/> if this class changed them.</summary>
    public void Restore(nint hwnd)
    {
        if (_applied.TryRemove(hwnd, out _) && NativeMethods.IsWindow(hwnd))
        {
            Reset(hwnd);
        }
    }

    /// <summary>Forgets a destroyed window without calling DWM.</summary>
    public void Forget(nint hwnd) => _applied.TryRemove(hwnd, out _);

    /// <summary>Restores every recoloured window and deletes the marker. UI thread.</summary>
    /// <returns>How many windows were restored.</returns>
    public int RestoreAll()
    {
        var restored = EmergencyRestore();
        _marked.Clear();
        return restored;
    }

    /// <summary>
    /// Crash-path variant of <see cref="RestoreAll"/>, safe on any thread: Win32 and file calls only, no logging,
    /// no locks, never throws.
    /// </summary>
    /// <returns>How many windows were restored.</returns>
    public int EmergencyRestore()
    {
        var restored = 0;
        foreach (var hwnd in _applied.Keys)
        {
            if (_applied.TryRemove(hwnd, out _) && NativeMethods.IsWindow(hwnd))
            {
                Reset(hwnd);
                restored++;
            }
        }

        DeleteMarker();
        return restored;
    }

    private void Mark(nint hwnd)
    {
        if (_markerPath is null || _marked.ContainsKey(hwnd))
        {
            return;
        }

        // Drop windows that have since closed, so the marker stays as small as the set of open windows.
        foreach (var stale in _marked.Keys.Where(handle => !NativeMethods.IsWindow(handle)).ToList())
        {
            _marked.Remove(stale);
        }

        _marked[hwnd] = NativeMethods.GetProcessId(hwnd);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllLines(_markerPath, _marked.Select(entry =>
                string.Create(CultureInfo.InvariantCulture, $"{(long)entry.Key:X} {entry.Value}")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThrottledLog.Warn("caption-marker", $"Could not write {_markerPath}: {ex.Message}");
        }
    }

    private void DeleteMarker()
    {
        if (_markerPath is null)
        {
            return;
        }

        try
        {
            File.Delete(_markerPath);
        }
        catch (Exception)
        {
            // Crash path: never throw. A stale marker only resets windows that still match it on the next start.
        }
    }

    private static void Reset(nint hwnd)
    {
        var systemDefault = NativeMethods.DWMWA_COLOR_DEFAULT;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref systemDefault, sizeof(uint));
        systemDefault = NativeMethods.DWMWA_COLOR_DEFAULT;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref systemDefault, sizeof(uint));
    }
}
