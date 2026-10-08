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
    /// </summary>
    public static async Task<OperationResult> RestartAsync()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("explorer").Where(p => p.SessionId == session))
            {
                using (process)
                {
                    process.Kill();
                    process.WaitForExit(ExitTimeout);
                }
            }

            if (!await ExplorerReappearsAsync(session))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
            }

            Log.Info("Explorer restarted");
            return OperationResult.Success;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            Log.Error("Could not restart Explorer", ex);
            return OperationResult.Failure("Windows would not let WinGnome restart Explorer. Sign out and back in to apply the change.");
        }
    }

    private static async Task<bool> ExplorerReappearsAsync(int session)
    {
        var deadline = DateTime.UtcNow + RespawnWindow;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval);
            var found = Process.GetProcessesByName("explorer").Where(p => p.SessionId == session).ToList();
            foreach (var process in found)
            {
                process.Dispose();
            }

            if (found.Count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
