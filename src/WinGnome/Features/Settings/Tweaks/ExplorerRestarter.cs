using System.ComponentModel;
using System.Diagnostics;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Tweaks;

/// <summary>Restarts the Windows shell (explorer.exe) so tweaks that need it take effect.</summary>
internal static class ExplorerRestarter
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RespawnWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Ends every explorer.exe in the current session, then makes sure one is running again. Windows usually
    /// relaunches the shell on its own, so a new instance is only started when none appears shortly afterwards.
    /// A process that cannot be ended is skipped; the shell is restored either way.
    /// </summary>
    public static async Task<OperationResult> RestartAsync()
    {
        int session;
        using (var current = Process.GetCurrentProcess())
        {
            session = current.SessionId;
        }

        var allEnded = true;
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session)
                    {
                        continue;
                    }

                    process.Kill();
                    allEnded &= process.WaitForExit(ExitTimeout);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // InvalidOperationException: it already exited. Win32Exception: access denied.
                    allEnded &= ex is InvalidOperationException;
                    Log.Warn($"Could not end explorer.exe (pid {process.Id})", ex);
                }
            }
        }

        try
        {
            if (!await ExplorerReappearsAsync(session))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Error("Could not start Explorer again", ex);
            return OperationResult.Failure("Explorer did not start again. Press Ctrl+Shift+Esc, choose Run new task and enter explorer.exe.");
        }

        if (!allEnded)
        {
            return OperationResult.Failure("Windows would not let WinGnome restart Explorer. Sign out and back in to apply the change.");
        }

        Log.Info("Explorer restarted");
        return OperationResult.Success;
    }

    private static async Task<bool> ExplorerReappearsAsync(int session)
    {
        var deadline = DateTime.UtcNow + RespawnWindow;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval);
            if (ExplorerIsRunning(session))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExplorerIsRunning(int session)
    {
        var running = false;
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    running |= process.SessionId == session;
                }
                catch (InvalidOperationException)
                {
                    // Exited while we looked.
                }
            }
        }

        return running;
    }
}
