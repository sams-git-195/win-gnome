using System.Windows.Threading;

namespace WinGnome.Infrastructure;

/// <summary>
/// A job only one WinGnome process in the session may do at a time (decorating other apps' windows), held as a
/// named mutex. Another process holding it is waited for on a background thread, so taking the role over when
/// that process quits (or dies: an abandoned mutex counts as acquired) never blocks the dispatcher. A Win32 mutex
/// must be released by the thread that owns it, so the same thread waits, holds and releases it.
/// </summary>
internal sealed class SessionRole : IDisposable
{
    private readonly string _name;
    private readonly string _job;
    private readonly Dispatcher _dispatcher;
    private readonly Action _acquired;

    // Signalled to make the thread release the role and exit. Shared with the thread, which may still be waking
    // up after Dispose returns, so whichever of the two finishes last disposes it.
    private readonly ManualResetEvent _stop = new(initialState: false);
    private int _stopUsers = 2;
    private bool _disposed;

    /// <summary>Starts waiting for the role named <paramref name="name"/>.</summary>
    /// <param name="name">The mutex name, e.g. Local\WinGnome-WindowButtons.</param>
    /// <param name="job">What the role does, for the log ("decorates windows").</param>
    /// <param name="dispatcher">The dispatcher <paramref name="acquired"/> runs on.</param>
    /// <param name="acquired">Called once, on the dispatcher, when this process holds the role.</param>
    public SessionRole(string name, string job, Dispatcher dispatcher, Action acquired)
    {
        _name = name;
        _job = job;
        _dispatcher = dispatcher;
        _acquired = acquired;
        var thread = new Thread(Run) { IsBackground = true, Name = $"WinGnome session role {name}" };
        thread.Start();
    }

    /// <summary>True once this process holds the role. Dispatcher thread only.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>Gives the role up (another process can take it over at once). Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsHeld = false;

        // No Join: the thread only waits on _stop, so it releases the mutex and ends straight away on its own.
        _stop.Set();
        ReleaseStopEvent();
    }

    private void Run()
    {
        try
        {
            using var mutex = new Mutex(initiallyOwned: false, _name);
            if (!WaitForRole(mutex))
            {
                return;
            }

            try
            {
                _dispatcher.BeginInvoke(OnAcquired);
                _stop.WaitOne();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // The mutex exists but we may not open it: an instance at another integrity level (elevated) created
            // it, so another instance holds the role. It can't be waited for either; this instance stays without
            // it until it is restarted or the feature is switched off and on (KI-042).
            Log.Warn($"Another WinGnome instance (running elevated?) {_job}; this one doesn't: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Without the mutex nothing says whether another instance holds the role: behave as before roles
            // existed and do the job, rather than silently doing nothing (fail open, KI-042).
            Log.Warn($"Could not check whether another WinGnome instance {_job}; this one does too", ex);
            _dispatcher.BeginInvoke(OnAcquired);
        }
        finally
        {
            ReleaseStopEvent();
        }
    }

    /// <summary>Waits until no other process holds the role (true, now owned) or until disposed (false).</summary>
    private bool WaitForRole(Mutex mutex)
    {
        try
        {
            if (mutex.WaitOne(0))
            {
                return true;
            }

            Log.Info($"Another WinGnome instance {_job}; this one takes over when that one stops");
            return WaitHandle.WaitAny([_stop, mutex]) == 1;
        }
        catch (AbandonedMutexException)
        {
            // The previous holder was killed: we own the mutex now.
            return true;
        }
    }

    private void OnAcquired()
    {
        if (_disposed || IsHeld)
        {
            return;
        }

        IsHeld = true;
        _acquired();
    }

    private void ReleaseStopEvent()
    {
        if (Interlocked.Decrement(ref _stopUsers) == 0)
        {
            _stop.Dispose();
        }
    }
}
