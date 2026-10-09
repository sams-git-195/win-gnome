using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using WinGnome.Core.Overview;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Overview;

/// <summary>
/// Drives the overview's <see cref="OverviewTransitionState"/> frame by frame from
/// <see cref="CompositionTarget.Rendering"/>.
/// </summary>
/// <remarks>
/// Each segment's clock starts at the first frame it actually draws (that frame's
/// <see cref="RenderingEventArgs.RenderingTime"/>), not when it is requested: with software rendering a
/// full-screen first frame can take several refreshes, and a clock started earlier loses most of the motion.
/// An opening can first wait for the window's content to be ready (see <see cref="Run"/>). The Rendering
/// handler and both timers only exist while a transition runs, so nothing happens at idle.
/// </remarks>
internal sealed class OverviewAnimator
{
    /// <summary>
    /// New frames to wait for before revealing the window. Rendering only says a frame is about to be drawn; WPF's
    /// render thread presents it later. Measured: revealing on the second frame still showed the bare backdrop
    /// for ~350 ms on a cold first open (the third frame waits for that presentation); warm opens cost one more
    /// frame, about 16 ms.
    /// </summary>
    private const int FramesBeforeReveal = 3;

    /// <summary>Reveal anyway after this long, so the overview can never stay invisible while it holds focus.</summary>
    private static readonly TimeSpan RevealTimeout = TimeSpan.FromMilliseconds(250);

    private readonly OverviewTransitionState _state = new();
    private readonly DispatcherTimer _revealTimer;
    private readonly DispatcherTimer _watchdog;
    private Action<TransitionFrame>? _onFrame;
    private Action? _onCompleted;
    private Action? _reveal;
    private TimeSpan _lastRenderingTime;
    private int _framesSeen;
    private int _framesAnimated;
    private long _requestedAt;

    public OverviewAnimator(Dispatcher dispatcher)
    {
        _revealTimer = new DispatcherTimer(RevealTimeout, DispatcherPriority.Normal, OnRevealTimeout, dispatcher);
        _revealTimer.Stop();
        _watchdog = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _watchdog.Tick += OnWatchdog;
    }

    /// <summary>The overview's position between the windows and the grid, and its deferred window changes.</summary>
    public OverviewTransitionState State => _state;

    /// <summary>True from <see cref="Run"/> until the transition completes or is stopped.</summary>
    public bool IsRunning => _onFrame is not null;

    /// <summary>True while a transition waits for its first frame before revealing the window.</summary>
    public bool IsWaitingToReveal => _reveal is not null;

    /// <summary>
    /// Starts opening or closing from the current position, replacing any running transition (whose completion
    /// callback is not called).
    /// </summary>
    /// <param name="opening">Head for the grid (true) or the windows.</param>
    /// <param name="animationsEnabled">False makes it instant: the final frame is applied on the first frame.</param>
    /// <param name="onFrame">Applies a frame to the screen.</param>
    /// <param name="onCompleted">Called once, after the final frame.</param>
    /// <param name="reveal">
    /// When set, called on the third new frame after this call, or after <see cref="RevealTimeout"/> if frames don't
    /// come: the window is shown then, and the clock starts at that frame. Call this after preparing the window, so
    /// the timeout doesn't count the preparation.
    /// </param>
    /// <param name="requestedAt">Stopwatch timestamp of the request, for the Debug timing log (0: now).</param>
    public void Run(bool opening, bool animationsEnabled, Action<TransitionFrame> onFrame, Action onCompleted, Action? reveal = null, long requestedAt = 0)
    {
        Stop();
        _state.Begin(opening, animationsEnabled);
        _onFrame = onFrame;
        _onCompleted = onCompleted;
        _reveal = reveal;
        _framesSeen = 0;
        _framesAnimated = 0;
        _requestedAt = requestedAt != 0 ? requestedAt : Stopwatch.GetTimestamp();
        if (reveal is not null)
        {
            _revealTimer.Start();
        }
        else
        {
            StartWatchdog();
        }

        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Ends the running transition where it is, without calling its completion callback.</summary>
    /// <remarks>A pending reveal is carried out, so stopping can never leave the window hidden.</remarks>
    public void Stop()
    {
        RevealNow();
        _watchdog.Stop();
        if (_onFrame is null)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _onFrame = null;
        _onCompleted = null;
        Trace("Rendering unsubscribed (stopped)");
    }

    /// <summary>Stops and returns to closed (the overview has hidden).</summary>
    public void Reset()
    {
        Stop();
        _state.Reset();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can be raised more than once for the same frame; only new frames count.
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastRenderingTime)
        {
            return;
        }

        _lastRenderingTime = time;
        _framesSeen++;
        if (_reveal is not null)
        {
            if (_framesSeen < FramesBeforeReveal)
            {
                return;
            }

            RevealNow();
        }

        _framesAnimated++;
        Apply(_state.Advance(time));
    }

    private void Apply(TransitionFrame frame)
    {
        _onFrame?.Invoke(frame);
        if (frame.Finished)
        {
            Complete();
        }
    }

    private void Complete()
    {
        var onCompleted = _onCompleted;
        _watchdog.Stop();
        CompositionTarget.Rendering -= OnRendering;
        _onFrame = null;
        _onCompleted = null;
        Trace($"{_framesAnimated} frames in {Stopwatch.GetElapsedTime(_requestedAt).TotalMilliseconds:F0} ms since the request "
            + $"(duration {_state.Duration.TotalMilliseconds:F0} ms); Rendering unsubscribed");
        onCompleted?.Invoke();
    }

    private void StartWatchdog()
    {
        _watchdog.Interval = _state.WatchdogDelay;
        _watchdog.Start();
    }

    /// <summary>
    /// Frames stopped coming (session locked, remote desktop minimised, a render stall): finish, so a closing
    /// overview can't stay on screen ignoring input.
    /// </summary>
    private void OnWatchdog(object? sender, EventArgs e)
    {
        _watchdog.Stop();
        if (IsRunning)
        {
            Log.Info("Overview: no frames to finish the transition; finishing it now");
            Apply(_state.Finish());
        }
    }

    private void OnRevealTimeout(object? sender, EventArgs e)
    {
        if (_reveal is not null)
        {
            Log.Info($"Overview: no frame within {RevealTimeout.TotalMilliseconds:F0} ms; showing it anyway");
            RevealNow();
        }
    }

    private void RevealNow()
    {
        _revealTimer.Stop();
        var reveal = _reveal;
        if (reveal is null)
        {
            return;
        }

        _reveal = null;
        Trace($"revealed {Stopwatch.GetElapsedTime(_requestedAt).TotalMilliseconds:F0} ms after the request, frame {_framesSeen}");
        reveal();

        // The clock starts at the next frame; from now on frames must keep coming.
        if (IsRunning)
        {
            StartWatchdog();
        }
    }

    /// <summary>Timing details for tuning; Debug builds only, so release builds don't log every open.</summary>
    [Conditional("DEBUG")]
    private static void Trace(string message) => Log.Info($"Overview transition: {message}");
}
