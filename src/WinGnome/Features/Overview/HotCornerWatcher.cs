using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Core.Input;
using WinGnome.Core.Monitors;
using WinGnome.Core.Windows;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Overview;

/// <summary>
/// Watches the top-left corner of the primary monitor and of every other monitor where that is a real screen corner
/// (<see cref="HotCornerRules"/>), and raises <see cref="Triggered"/> when the pointer rests there for the
/// configured delay. Where another monitor continues past the primary's corner, the pointer must rest in a small box
/// there for longer (a guarded corner), so passing through to that monitor never triggers. The overview opens on the
/// primary monitor whichever corner fired (KI-072). Polls the cursor instead of installing a mouse hook: a 50 ms
/// timer is cheap, cannot slow down system-wide mouse input, and needs no special privileges.
/// </summary>
internal sealed class HotCornerWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherTimer _timer;
    private readonly DisplayLayoutService _displays;
    private readonly uint _ownProcessId = NativeMethods.GetCurrentProcessId();
    private HotCornerDetector _detector = new(0);
    private HotCornerDetector _guarded = new(HotCornerRules.GuardedDwellMs(0), HotCornerRules.GuardedSizePx);
    private int _delayMs;

    public HotCornerWatcher(Dispatcher dispatcher, DisplayLayoutService displays)
    {
        _displays = displays;
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Input, (_, _) => Poll(), dispatcher)
        {
            IsEnabled = false,
        };
    }

    /// <summary>Raised on the UI thread when the corner is triggered.</summary>
    public event EventHandler? Triggered;

    /// <summary>Starts or stops watching and applies the dwell delay.</summary>
    public void Configure(bool enabled, int delayMs)
    {
        // A fresh detector forgets that the pointer may already be resting in the corner, so only
        // replace it when the delay really changed (settings changes arrive for every option).
        if (delayMs != _delayMs)
        {
            _delayMs = delayMs;
            _detector = new HotCornerDetector(delayMs);
            _guarded = new HotCornerDetector(HotCornerRules.GuardedDwellMs(delayMs), HotCornerRules.GuardedSizePx);
        }

        if (enabled)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Poll()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        // A sample on a monitor without that kind of corner resets its detector, so leaving for another monitor
        // (or passing through a guarded corner on the way there) always starts the dwell afresh.
        var layout = _displays.Current;
        var under = layout.At(cursor.X, cursor.Y);
        var kind = under is null ? HotCornerKind.None : HotCornerRules.KindOf(under, layout);
        if (kind != HotCornerKind.Corner)
        {
            _detector.Reset();
        }

        if (kind != HotCornerKind.Guarded)
        {
            _guarded.Reset();
        }

        if (under is null || kind == HotCornerKind.None)
        {
            return;
        }

        var monitor = under.Bounds;
        var detector = kind == HotCornerKind.Guarded ? _guarded : _detector;
        if (detector.Update(cursor.X, cursor.Y, monitor, Environment.TickCount64) && ShouldTrigger(monitor))
        {            Triggered?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stays quiet while the user is dragging something into the corner, and when a full-screen app
    /// (game, video, presentation) is in front on that monitor (there the corner is part of the app).
    /// </summary>
    private bool ShouldTrigger(PixelRect monitor)
    {
        if (NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON))
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || NativeMethods.GetProcessId(foreground) == _ownProcessId)
        {
            return true;
        }

        // The desktop itself (Progman/WorkerW) covers the monitor but is not a full-screen app.
        var className = NativeMethods.GetClassName(foreground);
        if (className is "Progman" or "WorkerW")
        {
            return true;
        }

        var hasCaption = (NativeMethods.GetStyle(foreground) & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
        return !WindowGeometry.IsFullScreenApp(
            NativeMethods.GetWindowBounds(foreground), monitor, NativeMethods.IsZoomed(foreground), hasCaption);
    }

    public void Dispose() => _timer.Stop();
}
