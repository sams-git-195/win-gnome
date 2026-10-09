using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using WinGnome.Core.Overview;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Overview;

/// <summary>
/// Runs one overview transition at a time, frame by frame, from <see cref="CompositionTarget.Rendering"/>.
/// </summary>
/// <remarks>
/// The clock starts at the first frame the transition actually draws (that frame's
/// <see cref="RenderingEventArgs.RenderingTime"/>), not when it is requested: with software rendering a
/// full-screen first frame can take several refreshes, and a clock started earlier loses most of the motion.
/// An opening transition can first wait for the window's content to be ready (see <see cref="Run"/>). The
/// Rendering handler is only subscribed while a transition runs, so nothing happens per frame at idle.
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

    private readonly DispatcherTimer _revealTimer;
    private Action<double>? _onFrame;
    private Action? _onCompleted;
    private Action? _reveal;
    private TimeSpan _duration;
    private TimeSpan? _start;
    private TimeSpan _lastRenderingTime;
    private int _framesSeen;
    private int _framesAnimated;
    private long _requestedAt;

    public OverviewAnimator(Dispatcher dispatcher)
    {
        _revealTimer = new DispatcherTimer(RevealTimeout, DispatcherPriority.Normal, OnRevealTimeout, dispatcher);
        _revealTimer.Stop();
    }

    /// <summary>True from <see cref="Run"/> until the transition completes or is stopped.</summary>
    public bool IsRunning => _onFrame is not null;

    /// <summary>True while a transition waits for its first frame before revealing the window.</summary>
    public bool IsWaitingToReveal => _reveal is not null;

    /// <summary>Eased progress (0..1) of the last frame drawn; 1 when nothing is running.</summary>
    public double Eased { get; private set; } = 1;

    /// <summary>
    /// Starts a transition, replacing any running one (whose completion callback is not called).
    /// </summary>
    /// <param name="duration">Length of the transition; zero applies the final frame on the first frame.</param>
    /// <param name="onFrame">Applies the eased progress (0..1) to the screen.</param>
    /// <param name="onCompleted">Called once, after the final frame.</param>
    /// <param name="reveal">
    /// When set, called on the second frame after this call, or after <see cref="RevealTimeout"/> if frames don't
    /// come: the window is shown then, and the clock starts at that frame.
    /// </param>
    public void Run(TimeSpan duration, Action<double> onFrame, Action onCompleted, Action? reveal = null)
    {
        Stop();
        _duration = duration;
        _onFrame = onFrame;
        _onCompleted = onCompleted;
        _reveal = reveal;
        _start = null;
        _framesSeen = 0;
        _framesAnimated = 0;
        _requestedAt = Stopwatch.GetTimestamp();
        Eased = 0;
        if (reveal is not null)
        {
            _revealTimer.Start();
        }

        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Ends the running transition where it is, without calling its completion callback.</summary>
    /// <remarks>A pending reveal is carried out, so stopping can never leave the window hidden.</remarks>
    public void Stop()
    {
        RevealNow();
        if (_onFrame is null)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _onFrame = null;
        _onCompleted = null;
        Eased = 1;
        Trace("Rendering unsubscribed (stopped)");
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

        _start ??= time;
        var progress = OverviewTransition.Progress(time - _start.Value, _duration);
        Eased = OverviewTransition.EaseOutQuad(progress);
        _framesAnimated++;
        _onFrame?.Invoke(Eased);
        if (progress >= 1)
        {
            Complete(time);
        }
    }

    private void Complete(TimeSpan lastFrame)
    {
        var onCompleted = _onCompleted;
        CompositionTarget.Rendering -= OnRendering;
        _onFrame = null;
        _onCompleted = null;
        Eased = 1;
        Trace($"{_framesAnimated} frames in {(lastFrame - _start.GetValueOrDefault(lastFrame)).TotalMilliseconds:F0} ms "
            + $"(duration {_duration.TotalMilliseconds:F0} ms); Rendering unsubscribed");
        onCompleted?.Invoke();
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
    }

    /// <summary>Timing details for tuning; Debug builds only, so release builds don't log every open.</summary>
    [Conditional("DEBUG")]
    private static void Trace(string message) => Log.Info($"Overview transition: {message}");
}
