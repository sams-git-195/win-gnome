using System.ComponentModel;
using System.Windows.Threading;
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

    // Windows that must never be decorated: classes that draw their own title bar, and windows UIPI isolates
    // from us. Forgotten when the window is destroyed (handles are recycled).
    private readonly HashSet<nint> _undecoratable = [];
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
            if (IsStillDecoratable(hwnd))
            {
                _windows[hwnd].ApplyStyle(style);
            }
            else
            {
                Remove(hwnd);
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
            if (!_windows.ContainsKey(info.Handle) && !_undecoratable.Contains(info.Handle))
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
            _undecoratable.Add(info.Handle);
            return;
        }

        // Windows that report no caption buttons (Chrome-style title bars, fullscreen) are re-checked on the next
        // window-list change rather than remembered: leaving fullscreen brings the buttons back.
        if (!WindowFilter.CanDecorate(info, _style.Settings.ExcludedProcesses) || !CaptionMetrics.TryRead(info.Handle, out _))
        {
            return;
        }

        DecoratedWindow? window = null;
        try
        {
            window = new DecoratedWindow(info.Handle, _style, _colorizer, info.Handle == _foreground);
            window.RemovalRequested += OnRemovalRequested;
            window.SampleRequested += OnSampleRequested;
            _windows.Add(info.Handle, window);
            window.UpdatePlacement();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ThrottledLog.Warn("decorate", $"Could not create window buttons for 0x{info.Handle:X}: {ex.Message}");
            if (window is not null)
            {
                _windows.Remove(info.Handle);
                window.Dispose();
            }
        }
    }

    private bool IsStillDecoratable(nint hwnd)
    {
        // Minimised or cloaked windows keep their (hidden) overlay; only settings-driven reasons drop it here.
        var info = _tracker.Inspect(hwnd);
        return info is not null
            && WindowFilter.CanDecorate(info with { IsMinimized = false, IsCloaked = false }, _style.Settings.ExcludedProcesses);
    }

    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        if (eventType == WinEventHook.EVENT_OBJECT_DESTROY)
        {
            _undecoratable.Remove(hwnd);
            if (_windows.ContainsKey(hwnd))
            {
                _colorizer.Forget(hwnd);
                Remove(hwnd);
            }

            return;
        }

        if (!_windows.TryGetValue(hwnd, out var window))
        {
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
        window.Dispose();
    }
}
