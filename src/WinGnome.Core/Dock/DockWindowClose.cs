namespace WinGnome.Core.Dock;

/// <summary>A window as seen by the dock: its handle, the process it belongs to, and whether that process is elevated.</summary>
/// <param name="Handle">The HWND.</param>
/// <param name="ProcessId">
/// The owning process id as <c>WindowTracker</c> reports it (for UWP frames, the app process, not the frame host).
/// </param>
/// <param name="IsElevated">True when the window's process runs with a higher integrity level than WinGnome may act on.</param>
public readonly record struct CloseTarget(nint Handle, int ProcessId, bool IsElevated);

/// <summary>What to do when the user asks to close a window the dock showed earlier.</summary>
public enum CloseDecision
{
    /// <summary>The window is still the same and may be asked to close.</summary>
    Post,
    /// <summary>The window no longer exists; nothing to do.</summary>
    Gone,
    /// <summary>The handle now belongs to a different process; closing it would close the wrong window.</summary>
    Reused,
    /// <summary>The window is elevated; WinGnome never acts on elevated windows.</summary>
    Elevated,
}

/// <summary>Pure decision for closing one window from the dock, taken against a fresh read of the window.</summary>
public static class DockWindowClose
{
    /// <summary>
    /// Decides whether to post the close request. <paramref name="captured"/> is what the dock showed when the user
    /// picked the window; <paramref name="live"/> is a fresh read of the same handle, or null when it no longer exists.
    /// Elevation of the captured window wins over everything, so a window that was never offered a close stays safe.
    /// </summary>
    public static CloseDecision Decide(CloseTarget captured, CloseTarget? live)
    {
        if (captured.IsElevated)
        {
            return CloseDecision.Elevated;
        }

        if (live is not { } now || now.Handle != captured.Handle)
        {
            return CloseDecision.Gone;
        }

        if (now.ProcessId != captured.ProcessId)
        {
            return CloseDecision.Reused;
        }

        return now.IsElevated ? CloseDecision.Elevated : CloseDecision.Post;
    }
}
