using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>
/// The one queue every display change runs on, in order and off the UI thread: start-up recovery and the Displays
/// panel's apply, keep and revert share it, so a recovery can never interleave with a panel change.
/// </summary>
internal static class DisplayWork
{
    /// <summary>How long shutdown waits for queued display work before reverting anyway.</summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static Task _tail = Task.CompletedTask;

    /// <summary>The safety order (Core) wired to the real display APIs and the revert record in <paramref name="directory"/>.</summary>
    public static DisplayChangeFlow Flow(string directory) => new(
        DisplayService.Current,
        DisplayService.Test,
        DisplayService.ApplyForSession,
        DisplayService.Revert,
        DisplayService.Persist,
        revert => DisplayRevertFile.Write(directory, revert),
        () => DisplayRevertFile.Delete(directory));

    /// <summary>Queues <paramref name="work"/> after everything already queued. Exceptions are logged.</summary>
    public static void Enqueue(string what, Action work)
    {
        lock (Gate)
        {
            _tail = _tail.ContinueWith(_ => Run(what, work), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// At WinGnome shutdown only: waits (briefly) for queued display work, then runs <paramref name="work"/> on the
    /// calling thread, because the process may exit as soon as this returns.
    /// </summary>
    public static void RunAtShutdown(string what, Action work)
    {
        Task tail;
        lock (Gate)
        {
            tail = _tail;
        }

        if (!tail.Wait(ShutdownWait))
        {
            Log.Warn($"Display work still running at shutdown; going ahead to {what}");
        }

        Run(what, work);
    }

    private static void Run(string what, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            // A worker-thread exception would otherwise vanish unobserved.
            Log.Warn($"Could not {what}", ex);
        }
    }
}
