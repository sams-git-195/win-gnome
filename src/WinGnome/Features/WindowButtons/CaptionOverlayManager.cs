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

    /// <summary>
    /// How long a window moved or resized by code must hold still before its patch colour is sampled again
    /// (KI-016): the same 450 ms the activation samples above add up to, so Mica has settled too.
    /// </summary>
    private const long ResampleDelayMs = 450;

    /// <summary>How long a custom title bar's size must hold still before it is probed (spec 0009).</summary>
    private const long ProbeSettleMs = 300;

    /// <summary>When to probe again after a probe found nothing (once per size; the app may still be drawing).</summary>
    private const long ProbeRetryMs = 2000;

    private readonly WindowTracker _tracker;
    private readonly CaptionColorizer _colorizer;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<nint, DecoratedWindow> _windows = [];

    // Windows UIPI isolates from us: never decorated. Forgotten when the window is destroyed (handles are recycled).
    private readonly HashSet<nint> _undecoratable = [];

    // Windows of classes that draw their own title bar, left alone under the current settings (cleared when the
    // settings change, so turning on custom title bars reconsiders them).
    private readonly HashSet<nint> _skippedClass = [];

    // Custom title bars being probed off the UI thread, how often each was probed and at which size it last
    // failed (see CaptionProbeThrottle), and those waiting to be probed once their size settles.
    private readonly Dictionary<nint, int> _probing = [];
    private readonly Dictionary<nint, CaptionProbeThrottle> _throttles = [];
    private readonly DeadlineTimer _probeTimer;

    // Decorated windows moved or resized by code, waiting to be re-sampled once they hold still.
    private readonly DeadlineTimer _resampleTimer;
    private readonly HashSet<DecoratedWindow> _pendingSamples = [];
    private readonly List<nint> _scratch = [];
    private readonly DispatcherTimer _sampleTimer;
    private readonly DispatcherTimer _restackTimer;
    private DecorationStyle _style;
    private nint _foreground;
    private int _restackPass;
    private int _samplePass;
    private int _probeToken;
    private bool _started;

    public CaptionOverlayManager(WindowTracker tracker, CaptionColorizer colorizer, Dispatcher dispatcher, DecorationStyle style)
    {
        _tracker = tracker;
        _colorizer = colorizer;
        _dispatcher = dispatcher;
        _style = style;
        _sampleTimer = new DispatcherTimer(SampleDelays[0], DispatcherPriority.Background, OnSampleTimer, dispatcher) { IsEnabled = false };
        _restackTimer = new DispatcherTimer(RestackDelays[0], DispatcherPriority.Normal, OnRestackTimer, dispatcher) { IsEnabled = false };
        _probeTimer = new DeadlineTimer(dispatcher, DispatcherPriority.Background, OnProbeDue);
        _resampleTimer = new DeadlineTimer(dispatcher, DispatcherPriority.Background, OnResampleDue);
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
        var webButtonsChanged = style.Settings.DecorateWebTitleBarButtons != _style.Settings.DecorateWebTitleBarButtons;
        _style = style;
        _scratch.Clear();
        _scratch.AddRange(_windows.Keys);
        foreach (var hwnd in _scratch)
        {
            var window = _windows[hwnd];
            if (IsStillDecoratable(hwnd, window.IsProbed) && IsProbeAllowed(window))
            {
                window.ApplyStyle(style);
            }
            else
            {
                Remove(hwnd);
            }
        }

        // The custom title bar settings may have changed: reconsider every skipped window, and retry failed
        // probes after custom title bars were switched off and on again or web buttons were switched.
        _skippedClass.Clear();
        if (!style.Settings.DecorateCustomTitleBars)
        {
            _probeTimer.Clear();
        }

        if (!style.Settings.DecorateCustomTitleBars || webButtonsChanged)
        {
            foreach (var throttle in _throttles.Values)
            {
                throttle.ForgetFailures();
            }
        }

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
        _probeTimer.Dispose();
        _resampleTimer.Dispose();
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
                && !_skippedClass.Contains(info.Handle) && !_probing.ContainsKey(info.Handle)
                && !_probeTimer.Contains(info.Handle))
            {
                TryDecorate(info);
            }
        }

        // A missed EVENT_OBJECT_DESTROY must not leave an overlay (or probe state) behind.
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

        _scratch.Clear();
        foreach (var hwnd in _throttles.Keys)
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                _scratch.Add(hwnd);
            }
        }

        foreach (var hwnd in _scratch)
        {
            ForgetProbeState(hwnd);
        }
    }

    private void TryDecorate(WindowInfo info)
    {
        if (CaptionDecorationRules.IsSkippedClass(info.ClassName))
        {
            // Mirrored (right-to-left) windows have their buttons on the left, where the probe never looks.
            if (_style.Settings.DecorateCustomTitleBars && CaptionDecorationRules.CanProbeSkippedClass(info.ClassName)
                && (NativeMethods.GetExStyle(info.Handle) & NativeMethods.WS_EX_LAYOUTRTL) == 0)
            {
                OnProbeDue(info.Handle);
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
            window = new DecoratedWindow(hwnd, _style, _colorizer, _dispatcher, hwnd == _foreground, probe);
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
    /// Probes a window that draws its own title bar now, unless it is not decoratable, two probes at its current
    /// size and DPI already failed, or it has used up its probes for the minute (then it is scheduled for when it
    /// may be probed again). The overlay is created when the probe finds the buttons.
    /// </summary>
    private void OnProbeDue(nint hwnd)
    {
        _probeTimer.Remove(hwnd);
        _windows.TryGetValue(hwnd, out var window);
        if (!_started || !_style.Settings.DecorateCustomTitleBars || _probing.ContainsKey(hwnd)
            || (window is null && !CanProbe(hwnd)) || !CaptionMetrics.TryReadFrame(hwnd, out var frame))
        {
            window?.AllowReprobe();
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        var throttle = ThrottleFor(hwnd);
        if (throttle.IsBlocked(new ProbeSize(frame.Width, frame.Height, dpi)))
        {
            window?.AllowReprobe();
            return;
        }

        var now = DeadlineTimer.Now;
        var next = throttle.NextStartAt(now);
        if (next > now)
        {
            _probeTimer.SetAt(hwnd, next);
            return;
        }

        throttle.RecordStart(now);
        StartProbe(hwnd, frame, dpi, WebButtonWidth(hwnd), AllowsMaximiseAnchor(hwnd));
    }

    /// <summary>
    /// True for a window with Windows App SDK caption controls (Dia), when web buttons are enabled: such windows
    /// may report only their maximise button (spec 0009). Plain HTCLIENT beside one zone is never accepted
    /// elsewhere.
    /// </summary>
    private bool AllowsMaximiseAnchor(nint hwnd)
    {
        if (!_style.Settings.DecorateWebTitleBarButtons)
        {
            return false;
        }

        // A local walk of the window tree (no messages to the app), done once per probe.
        var found = false;
        NativeMethods.EnumChildWindows(hwnd, (child, _) =>
        {
            found = NativeMethods.GetClassName(child) == CaptionDecorationRules.WindowsAppSdkCaptionControlsClass
                && NativeMethods.IsWindowVisible(child);
            return !found;
        }, 0);
        return found;
    }

    private bool CanProbe(nint hwnd) =>
        _tracker.Inspect(hwnd) is { } info
        && WindowFilter.CanDecorate(info, _style.Settings.ExcludedProcesses, drawsOwnButtons: true);

    /// <summary>The HTML button width of a profiled app (GitHub Desktop), when web buttons are enabled.</summary>
    private double? WebButtonWidth(nint hwnd) =>
        _style.Settings.DecorateWebTitleBarButtons && _tracker.Inspect(hwnd) is { } info
            ? CaptionDecorationRules.WebButtonWidth(info)
            : null;

    private CaptionProbeThrottle ThrottleFor(nint hwnd)
    {
        if (!_throttles.TryGetValue(hwnd, out var throttle))
        {
            throttle = new CaptionProbeThrottle();
            _throttles.Add(hwnd, throttle);
        }

        return throttle;
    }

    private void ForgetProbeState(nint hwnd)
    {
        _throttles.Remove(hwnd);
        _probing.Remove(hwnd);
        _probeTimer.Remove(hwnd);
    }

    private void StartProbe(nint hwnd, PixelRect frame, uint dpi, double? webButtonWidth, bool allowMaximiseAnchor)
    {
        // The token ties the answer to this window: a window destroyed meanwhile drops its entry, so an answer for
        // a recycled handle is ignored.
        var token = ++_probeToken;
        _probing[hwnd] = token;

        // WM_NCHITTEST goes to another process: never from the dispatcher (see CustomCaptionProbe).
        Task.Run(() =>
        {
            var result = CustomCaptionProbe.Probe(hwnd, frame, dpi, webButtonWidth, allowMaximiseAnchor, out var error);
            _dispatcher.BeginInvoke(() => OnProbed(hwnd, token, frame, dpi, result, error));
        });
    }

    private void OnProbed(nint hwnd, int token, PixelRect probedFrame, uint probedDpi, ProbedCaption? result, string? error)
    {
        if (!_probing.TryGetValue(hwnd, out var current) || current != token)
        {
            return;
        }

        _probing.Remove(hwnd);
        if (error is not null)
        {
            ThrottledLog.Warn("probe", $"Probing the caption buttons of 0x{hwnd:X} failed: {error}");
        }

        if (!_started || !NativeMethods.IsWindow(hwnd) || !_style.Settings.DecorateCustomTitleBars)
        {
            return;
        }

        _windows.TryGetValue(hwnd, out var window);
        if (!CaptionMetrics.TryReadFrame(hwnd, out var frame) || frame.Width != probedFrame.Width
            || frame.Height != probedFrame.Height || NativeMethods.GetDpiForWindow(hwnd) != probedDpi)
        {
            // Resized while probing (e.g. mid-drag): the answer may be for the old size. Probe again once the size
            // has held still (every location change pushes this back), rather than waiting for another event,
            // which may never come once the drag has ended.
            _probeTimer.Set(hwnd, ProbeSettleMs);
            return;
        }

        if (result is { NeedsWebButtonsSetting: true } && !_style.Settings.DecorateWebTitleBarButtons)
        {
            // Web buttons were switched off while probing.
            window?.AllowReprobe();
            return;
        }

        var throttle = ThrottleFor(hwnd);
        if (result is not { } probe)
        {
            var size = new ProbeSize(frame.Width, frame.Height, probedDpi);
            throttle.RecordFailure(size);
            if (window is not null)
            {
                Log.Info($"Window 0x{hwnd:X} no longer reports its caption buttons; its own buttons stay");
                Remove(hwnd);
            }

            // One more try at this size: the app may still have been laying out its title bar.
            if (!throttle.IsBlocked(size))
            {
                _probeTimer.Set(hwnd, ProbeRetryMs);
            }

            return;
        }

        throttle.RecordSuccess();
        if (window is not null)
        {
            window.SetProbe(probe);
        }
        else
        {
            Log.Info($"Decorating 0x{hwnd:X}, which draws its own title bar (buttons found by {Describe(probe.Source)})");
            Decorate(hwnd, probe);
        }
    }

    private static string Describe(ProbeSource source) => source switch
    {
        ProbeSource.ChildHitTest => "hit-testing a child window",
        ProbeSource.Profile => "its built-in web button profile",
        ProbeSource.MaximiseAnchored => "its maximise zone beside client area (Windows App SDK caption controls)",
        _ => "hit-testing",
    };

    private void OnReprobeRequested(DecoratedWindow window) => _probeTimer.Set(window.Target, ProbeSettleMs);

    /// <summary>
    /// False when <paramref name="window"/>'s buttons were found in a way the current settings no longer allow.
    /// </summary>
    private bool IsProbeAllowed(DecoratedWindow window) =>
        !window.IsProbed
        || (_style.Settings.DecorateCustomTitleBars && (!window.NeedsWebButtonsSetting || _style.Settings.DecorateWebTitleBarButtons));

    private void OnResampleDue(nint hwnd)
    {
        if (_windows.TryGetValue(hwnd, out var window))
        {
            window.SampleTitleBar();
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
            ForgetProbeState(hwnd);
            if (_windows.ContainsKey(hwnd))
            {
                _colorizer.Forget(hwnd);
                Remove(hwnd);
            }

            return;
        }

        if (eventType == WinEventHook.EVENT_OBJECT_LOCATIONCHANGE && _probeTimer.Contains(hwnd))
        {
            // Still moving: probe once the size holds still.
            _probeTimer.Set(hwnd, ProbeSettleMs);
        }

        if (!_windows.TryGetValue(hwnd, out var window))
        {
            // A custom title bar whose probe failed may report its buttons at the size it was resized to.
            if (eventType == WinEventHook.EVENT_SYSTEM_MOVESIZEEND && _throttles.TryGetValue(hwnd, out var throttle)
                && throttle.HasFailed && !_probeTimer.Contains(hwnd))
            {
                OnProbeDue(hwnd);
            }

            return;
        }

        switch (eventType)
        {
            case WinEventHook.EVENT_OBJECT_LOCATIONCHANGE:
                window.UpdatePlacement();

                // Moved or resized by code (snap, maximise, an app restoring its position) gets no
                // EVENT_SYSTEM_MOVESIZEEND: sample the patch colour again once it holds still (KI-016).
                _resampleTimer.Set(hwnd, ResampleDelayMs);
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
                // A user drag ended: the sample request below covers the location changes it raised.
                _resampleTimer.Remove(hwnd);
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
        _resampleTimer.Remove(hwnd);
        window.RemovalRequested -= OnRemovalRequested;
        window.SampleRequested -= OnSampleRequested;
        window.ReprobeRequested -= OnReprobeRequested;
        window.Dispose();
    }
}
