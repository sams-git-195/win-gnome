using System.Runtime.InteropServices;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Behaviour;

/// <summary>
/// X-Mouse style "sloppy focus": the window under the pointer becomes active after a short pause,
/// without being raised. Uses the system's active-window-tracking parameters for this session only.
/// </summary>
/// <remarks>
/// Values are written with SPIF_SENDCHANGE but never SPIF_UPDATEINIFILE, so nothing is persisted to the
/// user profile: even if WinGnome is killed, signing out returns the previous behaviour. The original
/// values are read before the first change and written back on <see cref="Disable"/>.
/// </remarks>
internal sealed class FocusFollowsMouse
{
    /// <summary>Hover time before the window under the pointer is activated (GNOME feels instant; 300 ms avoids accidents).</summary>
    private const int TrackingTimeoutMs = 300;

    private SavedParameters? _saved;

    /// <summary>Turns focus-follows-mouse on, remembering the current system values first.</summary>
    public void Enable()
    {
        if (_saved is not null)
        {
            return;
        }

        if (!TryRead(NativeMethods.SPI_GETACTIVEWINDOWTRACKING, out var tracking)
            || !TryRead(NativeMethods.SPI_GETACTIVEWNDTRKZORDER, out var raise)
            || !TryRead(NativeMethods.SPI_GETACTIVEWNDTRKTIMEOUT, out var timeout))
        {
            Log.Warn("Focus follows mouse: could not read the current system settings; leaving them unchanged");
            return;
        }

        _saved = new SavedParameters(tracking, raise, timeout);

        // Sloppy focus: activate on hover, but do not raise the window above the others.
        var applied = Write(NativeMethods.SPI_SETACTIVEWINDOWTRACKING, 1)
            & Write(NativeMethods.SPI_SETACTIVEWNDTRKZORDER, 0)
            & Write(NativeMethods.SPI_SETACTIVEWNDTRKTIMEOUT, TrackingTimeoutMs);
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
        }
    }

    /// <summary>
    /// Crash-path variant of <see cref="Disable"/>: direct Win32 calls only (no logging, which takes a lock),
    /// safe to call from any thread.
    /// </summary>
    public void EmergencyDisable()
    {
        if (_saved is { } saved)
        {
            _saved = null;
            Restore(saved, logFailures: false);
        }
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

    private sealed record SavedParameters(int Tracking, int RaiseOnActivate, int TimeoutMs);
}
