using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Core.Input;
using WinGnome.Core.Windows;
using WinGnome.Interop;

namespace WinGnome.Features.Overview;

/// <summary>
/// Watches the primary monitor's top-left corner and raises <see cref="Triggered"/> when the pointer rests
/// there for the configured delay. Polls the cursor instead of installing a mouse hook: a 50 ms timer is
/// cheap, cannot slow down system-wide mouse input, and needs no special privileges.
/// </summary>
internal sealed class HotCornerWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherTimer _timer;
    private readonly uint _ownProcessId = NativeMethods.GetCurrentProcessId();
    private HotCornerDetector _detector = new(0);
    private int _delayMs;

    public HotCornerWatcher(Dispatcher dispatcher)
    {
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

        var (monitor, _) = NativeMethods.GetPrimaryMonitorRects();
        if (_detector.Update(cursor.X, cursor.Y, monitor, Environment.TickCount64) && ShouldTrigger(monitor))
        {
            Triggered?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stays quiet while the user is dragging something into the corner, or when a full-screen app
    /// (game, video, presentation) is in front: there the corner is part of the app.
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

        return !WindowGeometry.IsFullScreen(NativeMethods.GetWindowBounds(foreground), monitor);
    }

    public void Dispose() => _timer.Stop();
}
