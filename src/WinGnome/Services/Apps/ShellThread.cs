using WinGnome.Infrastructure;

namespace WinGnome.Services.Apps;

/// <summary>
/// Runs blocking shell work on its own short-lived STA thread. Shell calls such as an elevated ShellExecuteEx (waits
/// until the UAC prompt is answered), ID-list launches (go through shell extensions) and packaged activation (waits for
/// a cold start) can block and pump messages, so none may run on the dispatcher; the shell wants STA.
/// </summary>
internal static class ShellThread
{
    /// <summary>
    /// Runs <paramref name="work"/> on a new background STA thread. When it returns true, <paramref name="done"/> is
    /// posted back to the caller's synchronisation context (the dispatcher), or run on the thread when there is none.
    /// Every exception is logged: one escaping the thread would end the shell process.
    /// </summary>
    /// <param name="what">Names the work in the log, for example "AppLauncher: could not launch 'notepad.exe'".</param>
    public static void Run(string what, Func<bool> work, Action? done = null)
    {
        var context = SynchronizationContext.Current;
        var thread = new Thread(() =>
        {
            try
            {
                if (work() && done is not null)
                {
                    if (context is null)
                    {
                        done();
                    }
                    else
                    {
                        context.Post(_ => done(), null);
                    }
                }
            }
            catch (Exception ex)
            {
                // Thread boundary: an exception escaping here would end the shell process, so everything is logged.
                Log.Warn(what, ex);
            }
        })
        {
            IsBackground = true,
            Name = "WinGnome shell",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
