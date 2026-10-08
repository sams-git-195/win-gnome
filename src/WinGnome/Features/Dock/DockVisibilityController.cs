using System.Windows.Threading;
using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// Decides when the dock is on screen (always visible, intellihide, autohide, full-screen apps) and tracks the
/// pointer for edge reveal. The decision itself is <see cref="DockVisibilityPolicy"/>; this class gathers its inputs.
/// </summary>
/// <remarks>
/// Edge reveal uses a cheap GetCursorPos poll that only runs while the dock is hidden or held open by the pointer,
/// rather than a thin always-on-top trigger window. A trigger window would sit above every other window along the
/// screen edge, swallowing clicks on the bottom row of pixels (scroll bars, maximised windows' edges), fighting
/// other topmost windows and full-screen apps over z-order, and staying on top of games. The poll costs one
/// syscall every 75 ms and stops entirely while the dock is shown and the pointer is elsewhere.
/// </remarks>
internal sealed class DockVisibilityController : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EvaluationDelay = TimeSpan.FromMilliseconds(80);

    private readonly ShellContext _context;
    private readonly DockWindow _window;
    private readonly ExternalForeground _foreground;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _evaluationTimer;

    private DockVisibility _mode = DockVisibility.AlwaysVisible;
    private DockFrame? _frame;
    private PixelRect _monitor;
    private nint _monitorHandle;
    private bool _enabled;
    private bool _shown;
    private bool _hasShownState;
    private bool _interacting;
    private bool _pointerEngaged;
    private bool _fullScreen;
    private DateTime _lastInsideUtc;

    public DockVisibilityController(ShellContext context, DockWindow window, ExternalForeground foreground)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));

        _pollTimer = new DispatcherTimer(PollInterval, DispatcherPriority.Input, OnPoll, context.Dispatcher) { IsEnabled = false };
        _evaluationTimer = new DispatcherTimer(EvaluationDelay, DispatcherPriority.Background, OnEvaluationTimer, context.Dispatcher) { IsEnabled = false };

        _context.Windows.RawWindowEvent += OnRawWindowEvent;
        _context.Windows.WindowsChanged += OnWindowsChanged;
        _foreground.Changed += OnForegroundChanged;
        _window.PointerEntered += OnPointerEntered;
    }

    /// <summary>Applies the mode and geometry and re-evaluates.</summary>
    public void Configure(DockVisibility mode, DockFrame frame, PixelRect monitor, nint monitorHandle)
    {
        _mode = mode;
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _monitor = monitor;
        _monitorHandle = monitorHandle;
        Evaluate(animate: _hasShownState);
    }

    /// <summary>Turns the dock on or off; off hides it immediately and stops all tracking.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _pollTimer.Stop();
            _evaluationTimer.Stop();
            _pointerEngaged = false;
            _shown = false;
            _hasShownState = false;
            _window.SetRevealed(false, animate: false);
            return;
        }

        Evaluate(animate: false);
    }

    /// <summary>A context menu is open or a reordering drag runs: keep the dock on screen.</summary>
    public void SetInteracting(bool interacting)
    {
        if (_interacting != interacting)
        {
            _interacting = interacting;
            if (!interacting)
            {
                // The pointer usually ends up over the dock; give it the normal grace period before hiding.
                _lastInsideUtc = DateTime.UtcNow;
                _pointerEngaged = true;
            }

            Evaluate(animate: true);
        }
    }

    private void OnPointerEntered(object? sender, EventArgs e)
    {
        _pointerEngaged = true;
        _lastInsideUtc = DateTime.UtcNow;
        Evaluate(animate: true);
    }

    private void OnRawWindowEvent(uint eventType, nint hwnd) => RequestEvaluation();

    private void OnWindowsChanged(object? sender, EventArgs e) => RequestEvaluation();

    private void OnForegroundChanged(object? sender, EventArgs e) => RequestEvaluation();

    /// <summary>Coalesces bursts of window events (drags fire location changes continuously) into one evaluation.</summary>
    private void RequestEvaluation()
    {
        if (_enabled && !_evaluationTimer.IsEnabled)
        {
            _evaluationTimer.Start();
        }
    }

    private void OnEvaluationTimer(object? sender, EventArgs e)
    {
        _evaluationTimer.Stop();
        Evaluate(animate: true);
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        try
        {
            if (_frame is null || !NativeMethods.GetCursorPos(out var cursor))
            {
                return;
            }

            // Hidden: the thin strip along the edge reveals. Shown: anywhere over the dock window keeps it.
            var zone = _shown ? _frame.Window : _frame.Geometry.TriggerZone;
            var now = DateTime.UtcNow;
            if (zone.Contains(cursor.X, cursor.Y) || _window.IsPointerOver)
            {
                _lastInsideUtc = now;
                if (!_pointerEngaged || !_shown)
                {
                    _pointerEngaged = true;
                    Evaluate(animate: true);
                }
            }
            else if (_pointerEngaged && now - _lastInsideUtc >= LeaveGrace)
            {
                _pointerEngaged = false;
                Evaluate(animate: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: pointer poll failed", ex);
        }
    }

    private void Evaluate(bool animate)
    {
        if (!_enabled || _frame is null)
        {
            return;
        }

        try
        {
            _fullScreen = IsFullScreenForeground();
            var obstructed = _mode == DockVisibility.Intellihide && IsObstructed();
            var show = DockVisibilityPolicy.ShouldShow(_mode, new DockVisibilityInputs(_pointerEngaged, _interacting, _fullScreen, obstructed));
            _shown = show;
            _hasShownState = true;
            _window.SetRevealed(show, animate);
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: visibility evaluation failed", ex);
        }

        var poll = DockVisibilityPolicy.NeedsPointerPolling(_mode, _shown, _pointerEngaged, _fullScreen);
        if (poll != _pollTimer.IsEnabled)
        {
            _pollTimer.IsEnabled = poll;
        }
    }

    private bool IsFullScreenForeground()
    {
        var hwnd = _foreground.Handle;
        return IsCandidate(hwnd)
            && NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST) == _monitorHandle
            && DockVisibilityPolicy.IsFullScreen(NativeMethods.GetWindowBounds(hwnd), _monitor, NativeMethods.IsZoomed(hwnd));
    }

    private bool IsObstructed()
    {
        var hwnd = _foreground.Handle;
        PixelRect? focused = IsCandidate(hwnd) ? NativeMethods.GetVisibleBounds(hwnd) : null;
        return DockVisibilityPolicy.IsObstructed(_frame!.Geometry.Bounds, focused, AnyMaximizedOnMonitor());
    }

    /// <summary>
    /// Live checks rather than the tracker's snapshot: maximising only raises location changes, which do not
    /// refresh the snapshot. Cloaked windows are on other virtual desktops.
    /// </summary>
    private bool AnyMaximizedOnMonitor()
    {
        foreach (var window in _context.Windows.Windows)
        {
            var hwnd = window.Handle;
            if (NativeMethods.IsZoomed(hwnd) && !NativeMethods.IsIconic(hwnd) && NativeMethods.IsWindowVisible(hwnd)
                && !NativeMethods.IsCloaked(hwnd)
                && NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST) == _monitorHandle)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A visible, on-screen app window; never the desktop or the taskbar.</summary>
    private static bool IsCandidate(nint hwnd) =>
        hwnd != 0 && NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd)
        && !NativeMethods.IsIconic(hwnd) && !NativeMethods.IsCloaked(hwnd)
        && NativeMethods.GetClassName(hwnd) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");

    public void Dispose()
    {
        _pollTimer.Stop();
        _evaluationTimer.Stop();
        _context.Windows.RawWindowEvent -= OnRawWindowEvent;
        _context.Windows.WindowsChanged -= OnWindowsChanged;
        _foreground.Changed -= OnForegroundChanged;
        _window.PointerEntered -= OnPointerEntered;
    }
}
