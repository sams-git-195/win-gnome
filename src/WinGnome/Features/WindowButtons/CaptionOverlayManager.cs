using System.ComponentModel;
using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Keeps exactly one <see cref="DecoratedWindow"/> per decoratable top-level window, driven by the shared
/// <see cref="WindowTracker"/> events: new windows are picked up from the (debounced) window list, and the
/// high-frequency events are routed to the one affected window through a dictionary lookup, so a drag never
/// touches any other window and nothing runs while the desktop is idle.
/// </summary>
internal sealed class CaptionOverlayManager : IDisposable
{
    /// <summary>
    /// When to sample a title bar after a request: first once the app has repainted it (quick feedback), then
    /// again once DWM's activation cross-fade has settled. Mica title bars (Explorer, Notepad) fade between their
    /// active and inactive colours over about 300 ms, so the first sample is usually an in-between colour.
    /// </summary>
    private static readonly TimeSpan[] SampleDelays = [TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300)];

    /// <summary>
    /// When to re-check the stacking after a foreground change. EVENT_SYSTEM_FOREGROUND is raised as soon as the
    /// foreground queue changes, but when another process activates the window (taskbar, Alt+Tab, restore) the
    /// target's own thread raises it in the z-order later, when it processes the activation; the immediate restack
    /// then sees the old order and the raised target ends up covering its overlay.
    /// </summary>
    private static readonly TimeSpan[] RestackDelays =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(1000)];

    private readonly WindowTracker _tracker;
    private readonly CaptionColorizer _colorizer;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<nint, DecoratedWindow> _windows = [];

    // Windows UIPI isolates from us: never decorated. Forgotten when the window is destroyed (handles are recycled).
    private readonly HashSet<nint> _undecoratable = [];

    // Windows of classes that draw their own title bar, left alone under the current settings (cleared when the
    // settings change, so turning on custom title bars reconsiders them).
    private readonly HashSet<nint> _skippedClass = [];

    // Custom title bars being probed off the UI thread, and those whose probe failed at a given size and DPI
    // (probed again only once that changes).
    private readonly HashSet<nint> _probing = [];
    private readonly Dictionary<nint, (int Width, int Height, uint Dpi)> _probeFailed = [];
    private readonly HashSet<DecoratedWindow> _pendingSamples = [];
    private readonly List<nint> _scratch = [];
    private readonly DispatcherTimer _sampleTimer;
    private readonly DispatcherTimer _restackTimer;
    private DecorationStyle _style;
    private nint _foreground;
    private int _restackPass;
    private int _samplePass;
    private bool _started;

    public CaptionOverlayManager(WindowTracker tracker, CaptionColorizer colorizer, Dispatcher dispatcher, DecorationStyle style)
    {
        _tracker = tracker;
        _colorizer = colorizer;
        _dispatcher = dispatcher;
        _style = style;
        _sampleTimer = new DispatcherTimer(SampleDelays[0], DispatcherPriority.Background, OnSampleTimer, dispatcher) { IsEnabled = false };
        _restackTimer = new DispatcherTimer(RestackDelays[0], DispatcherPriority.Normal, OnRestackTimer, dispatcher) { IsEnabled = false };
    }

    /// <summary>Number of windows currently decorated.</summary>
    public int Count => _windows.Count;

    /// <summary>Subscribes to window events and decorates the windows that are already open.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _foreground = _tracker.Foreground;
        _tracker.RawWindowEvent += OnRawWindowEvent;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _tracker.WindowsChanged += OnWindowsChanged;
        Reconcile();
    }

    /// <summary>Applies new settings or theme colours to every decorated window, dropping newly excluded ones.</summary>
    public void ApplyStyle(DecorationStyle style)
    {
        _style = style;
        _scratch.Clear();
        _scratch.AddRange(_windows.Keys);
        foreach (var hwnd in _scratch)
        {
            var window = _windows[hwnd];
            if (IsStillDecoratable(hwnd, window.IsProbed) && (!window.IsProbed || style.Settings.DecorateCustomTitleBars))
            {
                window.ApplyStyle(style);
            }
            else
            {
                Remove(hwnd);
            }
        }

        // The custom title bar setting may have changed: reconsider every skipped window.
        _skippedClass.Clear();
        Reconcile();
    }

    public void Dispose()
    {
        if (_started)
        {
            _started = false;
            _tracker.RawWindowEvent -= OnRawWindowEvent;
            _tracker.ForegroundChanged -= OnForegroundChanged;
            _tracker.WindowsChanged -= OnWindowsChanged;
        }

        _sampleTimer.Stop();
        _restackTimer.Stop();
        _scratch.Clear();
        _scratch.AddRange(_windows.Keys);
        foreach (var hwnd in _scratch)
        {
            Remove(hwnd);
        }
    }

    private void OnWindowsChanged(object? sender, EventArgs e) => Reconcile();

    /// <summary>Decorates task windows that are not decorated yet and drops entries whose window has vanished.</summary>
    private void Reconcile()
    {
        foreach (var info in _tracker.Windows)
        {
            if (!_windows.ContainsKey(info.Handle) && !_undecoratable.Contains(info.Handle)
                && !_skippedClass.Contains(info.Handle) && !_probing.Contains(info.Handle))
            {
                TryDecorate(info);
            }
        }

        // A missed EVENT_OBJECT_DESTROY must not leave an overlay behind.
        _scratch.Clear();
        foreach (var hwnd in _windows.Keys)
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                _scratch.Add(hwnd);
            }
        }

        foreach (var hwnd in _scratch)
        {
            _colorizer.Forget(hwnd);
            Remove(hwnd);
        }
    }

    private void TryDecorate(WindowInfo info)
    {
        if (CaptionDecorationRules.IsSkippedClass(info.ClassName))
        {
            if (_style.Settings.DecorateCustomTitleBars && CaptionDecorationRules.CanProbeSkippedClass(info.ClassName))
            {
                TryProbe(info);
            }
            else
            {
                _skippedClass.Add(info.Handle);
            }

            return;
        }

        // Windows that report no caption buttons (Chrome-style title bars, fullscreen) are re-checked on the next
        // window-list change rather than remembered: leaving fullscreen brings the buttons back.
        if (!WindowFilter.CanDecorate(info, _style.Settings.ExcludedProcesses) || !CaptionMetrics.TryRead(info.Handle, out _))
        {
            return;
        }

        Decorate(info.Handle, probe: null);
    }

    private void Decorate(nint hwnd, ProbedCaption? probe)
    {
        DecoratedWindow? window = null;
        try
        {
            window = new DecoratedWindow(hwnd, _style, _colorizer, hwnd == _foreground, probe);
            window.RemovalRequested += OnRemovalRequested;
            window.SampleRequested += OnSampleRequested;
            window.ReprobeRequested += OnReprobeRequested;
            _windows.Add(hwnd, window);
            window.UpdatePlacement();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ThrottledLog.Warn("decorate", $"Could not create window buttons for 0x{hwnd:X}: {ex.Message}");
            if (window is not null)
            {
                _windows.Remove(hwnd);
                window.Dispose();
            }
        }
    }

    /// <summary>
    /// Starts probing a window that draws its own title bar, unless a probe at its current size and DPI already
    /// failed. The overlay is created when the probe finds the buttons.
    /// </summary>
    private void TryProbe(WindowInfo info)
    {
        if (!WindowFilter.CanDecorate(info, _style.Settings.ExcludedProcesses, drawsOwnButtons: true)
            || !CaptionMetrics.TryReadFrame(info.Handle, out var frame))
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(info.Handle);
        if (_probeFailed.TryGetValue(info.Handle, out var failed) && failed == (frame.Width, frame.Height, dpi))
        {
            return;
        }

        StartProbe(info.Handle, frame, dpi);
    }

    private void StartProbe(nint hwnd, PixelRect frame, uint dpi)
    {
        if (!_probing.Add(hwnd))
        {
            return;
        }

        // WM_NCHITTEST goes to another process: never from the dispatcher (see CustomCaptionProbe).
        Task.Run(() => CustomCaptionProbe.Probe(hwnd, frame, dpi))
            .ContinueWith(task => _dispatcher.BeginInvoke(() => OnProbed(hwnd, frame, dpi, task.Result)), TaskScheduler.Default);
    }

    private void OnProbed(nint hwnd, PixelRect probedFrame, uint probedDpi, ProbedCaption? result)
    {
        _probing.Remove(hwnd);
        if (!_started || !NativeMethods.IsWindow(hwnd) || !_style.Settings.DecorateCustomTitleBars)
        {
            return;
        }

        _windows.TryGetValue(hwnd, out var window);
        if (!CaptionMetrics.TryReadFrame(hwnd, out var frame) || frame.Width != probedFrame.Width
            || frame.Height != probedFrame.Height || NativeMethods.GetDpiForWindow(hwnd) != probedDpi)
        {
            // Resized while probing (e.g. mid-drag): the answer may be for the old size. Probe again right away at
            // the current size rather than waiting for another event, which may never come once the drag has
            // ended; this repeats, one probe at a time, until the size holds still for a whole probe.
            if (!frame.IsEmpty)
            {
                StartProbe(hwnd, frame, NativeMethods.GetDpiForWindow(hwnd));
            }
            else
            {
                window?.AllowReprobe();
            }

            return;
        }

        if (result is not { } probe)
        {
            _probeFailed[hwnd] = (frame.Width, frame.Height, probedDpi);
            if (window is not null)
            {
                Log.Info($"Window 0x{hwnd:X} no longer reports its caption buttons; its own buttons stay");
                Remove(hwnd);
            }

            return;
        }

        _probeFailed.Remove(hwnd);
        if (window is not null)
        {
            window.SetProbe(probe);
        }
        else
        {
            Log.Info($"Decorating 0x{hwnd:X}, which draws its own title bar (buttons found by hit-testing)");
            Decorate(hwnd, probe);
        }
    }

    private void OnReprobeRequested(DecoratedWindow window)
    {
        if (CaptionMetrics.TryReadFrame(window.Target, out var frame))
        {
            StartProbe(window.Target, frame, NativeMethods.GetDpiForWindow(window.Target));
        }
        else
        {
            window.AllowReprobe();
        }
    }

    private bool IsStillDecoratable(nint hwnd, bool drawsOwnButtons)
    {
        // Minimised or cloaked windows keep their (hidden) overlay; only settings-driven reasons drop it here.
        var info = _tracker.Inspect(hwnd);
        return info is not null
            && WindowFilter.CanDecorate(info with { IsMinimized = false, IsCloaked = false }, _style.Settings.ExcludedProcesses, drawsOwnButtons);
    }

    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        if (eventType == WinEventHook.EVENT_OBJECT_DESTROY)
        {
            _undecoratable.Remove(hwnd);
            _skippedClass.Remove(hwnd);
            _probeFailed.Remove(hwnd);
            if (_windows.ContainsKey(hwnd))
            {
                _colorizer.Forget(hwnd);
                Remove(hwnd);
            }

            return;
        }

        if (!_windows.TryGetValue(hwnd, out var window))
        {
            // A custom title bar whose probe failed may report its buttons at the size it was resized to.
            if (eventType == WinEventHook.EVENT_SYSTEM_MOVESIZEEND && _probeFailed.ContainsKey(hwnd)
                && _tracker.Inspect(hwnd) is { } info)
            {
                TryProbe(info);
            }

            return;
        }

        switch (eventType)
        {
            case WinEventHook.EVENT_OBJECT_LOCATIONCHANGE:
                window.UpdatePlacement();
                break;
            case WinEventHook.EVENT_SYSTEM_MINIMIZESTART:
                window.SetMinimized(true);
                break;
            case WinEventHook.EVENT_SYSTEM_MINIMIZEEND:
                window.SetMinimized(false);
                break;
            case WinEventHook.EVENT_OBJECT_CLOAKED:
                window.SetCloaked(true);
                break;
            case WinEventHook.EVENT_OBJECT_UNCLOAKED:
                window.SetCloaked(NativeMethods.IsCloaked(hwnd));
                break;
            case WinEventHook.EVENT_OBJECT_SHOW:
                window.UpdatePlacement();
                window.Restack();
                break;
            case WinEventHook.EVENT_OBJECT_HIDE:
                // Hidden windows (closing, or minimised to the tray) lose their overlay; it is recreated from the
                // window list if the window comes back. Closing it now, before the app destroys the window,
                // keeps teardown on our terms.
                if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    Remove(hwnd);
                }

                break;
            case WinEventHook.EVENT_SYSTEM_MOVESIZEEND:
                window.UpdatePlacement();
                window.Restack();
                OnSampleRequested(window);
                break;
        }
    }

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        var previous = _foreground;
        _foreground = hwnd;
        if (_windows.TryGetValue(previous, out var deactivated))
        {
            deactivated.SetActive(false);
        }

        // Activation raises the window (with its owned windows) to the top of the z-order, above its overlay.
        // When a dialog is what got activated, its owner was raised too.
        if (_windows.TryGetValue(hwnd, out var activated))
        {
            activated.SetActive(true);
            activated.Restack();
        }
        else if (_windows.TryGetValue(NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER), out var owner))
        {
            owner.Restack();
        }

        // The raise may still be pending (see RestackDelays), and a foreground change can also lower windows
        // (Alt+Esc sends the active window to the bottom), so check every overlay again shortly.
        _restackPass = 0;
        _restackTimer.Stop();
        _restackTimer.Interval = RestackDelays[0];
        _restackTimer.Start();
    }

    private void OnRestackTimer(object? sender, EventArgs e)
    {
        _restackTimer.Stop();

        // Restack is a few cheap GetWindow calls and no SetWindowPos when the overlay is already in place.
        foreach (var window in _windows.Values)
        {
            window.Restack();
        }

        if (++_restackPass < RestackDelays.Length)
        {
            _restackTimer.Interval = RestackDelays[_restackPass];
            _restackTimer.Start();
        }
    }

    private void OnSampleRequested(DecoratedWindow window)
    {
        _pendingSamples.Add(window);

        // A request while waiting for the settle pass starts over, so the new window also gets its quick sample;
        // one while the first pass is pending simply joins it.
        if (!_sampleTimer.IsEnabled || _samplePass > 0)
        {
            _samplePass = 0;
            _sampleTimer.Stop();
            _sampleTimer.Interval = SampleDelays[0];
            _sampleTimer.Start();
        }
    }

    private void OnSampleTimer(object? sender, EventArgs e)
    {
        _sampleTimer.Stop();
        foreach (var window in _pendingSamples)
        {
            window.SampleTitleBar();
        }

        if (++_samplePass < SampleDelays.Length)
        {
            _sampleTimer.Interval = SampleDelays[_samplePass];
            _sampleTimer.Start();
            return;
        }

        _pendingSamples.Clear();
    }

    private void OnRemovalRequested(DecoratedWindow window, bool permanent)
    {
        // Deferred: the request can come from inside the overlay's own mouse or Closed handler.
        _dispatcher.BeginInvoke(() =>
        {
            if (!_windows.TryGetValue(window.Target, out var current) || current != window)
            {
                return;
            }

            Remove(window.Target);
            if (permanent)
            {
                _undecoratable.Add(window.Target);
                Log.Info($"Window 0x{window.Target:X} rejects caption commands (elevated?); its native buttons stay");
            }
        });
    }

    private void Remove(nint hwnd)
    {
        if (!_windows.Remove(hwnd, out var window))
        {
            return;
        }

        _pendingSamples.Remove(window);
        window.RemovalRequested -= OnRemovalRequested;
        window.SampleRequested -= OnSampleRequested;
        window.ReprobeRequested -= OnReprobeRequested;
        window.Dispose();
    }
}
