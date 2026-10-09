using System.Windows.Threading;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels;

/// <summary>
/// Applies system setting changes on a worker thread, in the order they were requested. SystemParametersInfo with
/// SPIF_SENDCHANGE and WM_SETTINGCHANGE broadcasts wait on every top-level window, so they must not run on the UI
/// thread. In read-only (safe) mode nothing is written; the request is only logged.
/// </summary>
internal sealed class SystemSettingWriter(Dispatcher dispatcher, bool readOnly)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>
    /// Queues <paramref name="write"/>, which returns false (after logging why) when Windows refused the change.
    /// <paramref name="onFailure"/> then runs on the UI thread.
    /// </summary>
    /// <param name="what">Short description for the log, e.g. "set the mouse speed to 12".</param>
    public void Run(string what, Func<bool> write, Action onFailure)
    {
        if (readOnly)
        {
            Log.Info($"Safe mode: did not {what}");
            return;
        }

        lock (_gate)
        {
            _tail = _tail.ContinueWith(_ => Execute(what, write, onFailure), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void Execute(string what, Func<bool> write, Action onFailure)
    {
        bool succeeded;
        try
        {
            succeeded = write();
        }
        catch (Exception ex)
        {
            // A worker-thread exception would otherwise vanish unobserved; every failure is logged and shown instead.
            Log.Warn($"Could not {what}", ex);
            succeeded = false;
        }

        if (succeeded)
        {
            Log.Info($"Settings: {what}");
        }
        else
        {
            dispatcher.BeginInvoke(onFailure);
        }
    }
}
