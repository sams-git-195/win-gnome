using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Behaviour;

/// <summary>
/// X-Mouse style "sloppy focus": the window under the pointer becomes active after a short pause,
/// without being raised. Uses the system's active-window-tracking parameters for this session only.
/// </summary>
/// <remarks>
/// <para>
/// Values are written with SPIF_SENDCHANGE but never SPIF_UPDATEINIFILE, so nothing is persisted to the
/// user profile: even if WinGnome is killed, signing out returns the previous behaviour. The original
/// values are read before the first change and written back on <see cref="Disable"/>.
/// </para>
/// <para>
/// They are also recorded in a marker file before anything changes (like the taskbar state). If WinGnome is
/// killed and started again in the same session, the live values are WinGnome's own, so the originals are
/// taken from the marker instead; otherwise the next run would "restore" focus-follows-mouse forever.
/// </para>
/// </remarks>
internal sealed class FocusFollowsMouse
{
    /// <summary>Hover time before the window under the pointer is activated (GNOME feels instant; 300 ms avoids accidents).</summary>
    private const int TrackingTimeoutMs = 300;

    private const string MarkerFileName = "focus-follows-mouse.state";

    /// <summary>Sloppy focus: activate on hover, but do not raise the window above the others.</summary>
    private static readonly SavedParameters Applied = new(1, 0, TrackingTimeoutMs);

    private readonly string _markerPath;
    private SavedParameters? _saved;

    /// <param name="settingsDirectory">Profile folder that holds the marker file.</param>
    public FocusFollowsMouse(string settingsDirectory)
    {
        _markerPath = Path.Combine(settingsDirectory, MarkerFileName);
    }

    /// <summary>Turns focus-follows-mouse on, remembering the original system values first.</summary>
    public void Enable()
    {
        if (_saved is not null)
        {
            return;
        }

        if (!TryReadCurrent(out var current))
        {
            Log.Warn("Focus follows mouse: could not read the current system settings; leaving them unchanged");
            return;
        }

        var original = OriginalLeftByUncleanExit(current) ?? current;
        WriteMarker(original);
        _saved = original;

        var applied = Write(NativeMethods.SPI_SETACTIVEWINDOWTRACKING, Applied.Tracking)
            & Write(NativeMethods.SPI_SETACTIVEWNDTRKZORDER, Applied.RaiseOnActivate)
            & Write(NativeMethods.SPI_SETACTIVEWNDTRKTIMEOUT, Applied.TimeoutMs);
        if (!applied)
        {
            Disable();
            return;
        }

        Log.Info("Focus follows mouse enabled for this session");
    }

    /// <summary>Restores the values that were in effect before <see cref="Enable"/>.</summary>
    public void Disable()
    {
        if (_saved is { } saved)
        {
            _saved = null;
            Restore(saved, logFailures: true);
            DeleteMarker(logFailures: true);
        }
    }

    /// <summary>
    /// Undoes focus-follows-mouse left on by a previous run that was killed, when the option is now off.
    /// Does nothing when there is no marker or the live values are no longer WinGnome's.
    /// </summary>
    public void RestoreAfterUncleanExit()
    {
        if (_saved is not null || !File.Exists(_markerPath))
        {
            return;
        }

        if (TryReadCurrent(out var current) && OriginalLeftByUncleanExit(current) is { } original)
        {
            Restore(original, logFailures: true);
            Log.Info("Focus follows mouse: restored the system settings left by a previous run");
        }

        DeleteMarker(logFailures: true);
    }

    /// <summary>
    /// Crash-path variant of <see cref="Disable"/>: direct Win32 and file calls only (no logging, which takes a
    /// lock), safe to call from any thread.
    /// </summary>
    public void EmergencyDisable()
    {
        if (_saved is { } saved)
        {
            _saved = null;
            Restore(saved, logFailures: false);
            DeleteMarker(logFailures: false);
        }
    }

    /// <summary>
    /// The originals recorded by a previous run, if WinGnome's values are still in effect. After signing out
    /// (the values are session-only) or once the user changed the setting, the live values are the truth.
    /// </summary>
    private SavedParameters? OriginalLeftByUncleanExit(SavedParameters current) =>
        current == Applied ? ReadMarker() : null;

    private static bool TryReadCurrent(out SavedParameters current)
    {
        current = Applied;
        if (!TryRead(NativeMethods.SPI_GETACTIVEWINDOWTRACKING, out var tracking)
            || !TryRead(NativeMethods.SPI_GETACTIVEWNDTRKZORDER, out var raise)
            || !TryRead(NativeMethods.SPI_GETACTIVEWNDTRKTIMEOUT, out var timeout))
        {
            return false;
        }

        // The BOOL parameters may come back as any non-zero value; normalise so they compare with Applied.
        current = new SavedParameters(tracking != 0 ? 1 : 0, raise != 0 ? 1 : 0, timeout);
        return true;
    }

    private static void Restore(SavedParameters saved, bool logFailures)
    {
        Write(NativeMethods.SPI_SETACTIVEWINDOWTRACKING, saved.Tracking, logFailures);
        Write(NativeMethods.SPI_SETACTIVEWNDTRKZORDER, saved.RaiseOnActivate, logFailures);
        Write(NativeMethods.SPI_SETACTIVEWNDTRKTIMEOUT, saved.TimeoutMs, logFailures);
    }

    private static bool TryRead(uint action, out int value) =>
        NativeMethods.SystemParametersInfoGet(action, 0, out value, 0);

    private static bool Write(uint action, int value, bool logFailures = true)
    {
        // These SPI_SET* actions take the value itself in pvParam (cast to a pointer), not a pointer to it.
        if (NativeMethods.SystemParametersInfoSet(action, 0, value, NativeMethods.SPIF_SENDCHANGE))
        {
            return true;
        }

        if (logFailures)
        {
            Log.Warn($"SystemParametersInfo(0x{action:X}) failed (error {Marshal.GetLastPInvokeError()})");
        }

        return false;
    }

    private void WriteMarker(SavedParameters original)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, JsonSerializer.Serialize(original));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the values are session-only, so signing out still restores them.
            Log.Warn("Focus follows mouse: could not write the restore marker", ex);
        }
    }

    private SavedParameters? ReadMarker()
    {
        try
        {
            return File.Exists(_markerPath) ? JsonSerializer.Deserialize<SavedParameters>(File.ReadAllText(_markerPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("Focus follows mouse: restore marker unreadable; ignoring it", ex);
            return null;
        }
    }

    private void DeleteMarker(bool logFailures)
    {
        try
        {
            File.Delete(_markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (logFailures)
            {
                Log.Warn("Focus follows mouse: could not delete the restore marker", ex);
            }
        }
    }

    private sealed record SavedParameters(int Tracking, int RaiseOnActivate, int TimeoutMs);
}
