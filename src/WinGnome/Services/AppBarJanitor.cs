using System.Windows.Interop;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// Gives back screen strips that a killed WinGnome left reserved. A force-killed process cannot send ABM_REMOVE, so
/// Explorer is left with AppBar entries for dead windows; it drops them when it next re-checks its registrations.
/// Spike 0 of spec 0010 (Windows 11 26200) found that Explorer already reclaims a killed bar's strip within a few
/// hundred milliseconds on its own, and that a 1×1 ABM_NEW + ABM_REMOVE from another process reclaims anything
/// left. This relies on undocumented Explorer behaviour (KI-070), so it is isolated here, logs every monitor's work
/// area before and after, and never fails start-up.
/// </summary>
internal static class AppBarJanitor
{
    /// <summary>
    /// Registers a hidden 1×1 tool window as an AppBar and removes it at once. It never sends ABM_SETPOS, so it
    /// reserves nothing. Run once at start (after the taskbar restore, before any feature docks a bar) and on
    /// <c>--restore-taskbar</c>.
    /// </summary>
    public static void Nudge()
    {
        try
        {
            Log.Info($"AppBar janitor: before: {DisplayLayoutService.Describe(DisplayLayoutService.Read())}");
            var parameters = new HwndSourceParameters("WinGnome AppBar Janitor")
            {
                Width = 1,
                Height = 1,
                WindowStyle = unchecked((int)NativeMethods.WS_POPUP),
                ExtendedWindowStyle = (int)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE),
            };
            using (var window = new HwndSource(parameters))
            {
                if (!AppBar.RegisterAndRemove(window.Handle))
                {
                    Log.Warn("AppBar janitor: ABM_NEW failed; strips left by a killed instance may stay reserved");
                }
            }

            Log.Info($"AppBar janitor: after: {DisplayLayoutService.Describe(DisplayLayoutService.Read())}");
        }
        catch (Exception ex)
        {
            Log.Warn("AppBar janitor failed", ex);
        }
    }
}
