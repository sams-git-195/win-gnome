namespace WinGnome.Core.Dock;

/// <summary>
/// One icon in the dock: a pinned launcher, a running app, or both. <paramref name="MostRecentWindow"/> is the app's
/// topmost window in z-order (0 when not running), which a plain click activates. <paramref name="Windows"/> carries
/// no recency: it is in the order <see cref="DockModelBuilder"/> received them, or in stable first-appearance order
/// once <see cref="DockRunningOrder"/> has run, and cycling walks it.
/// </summary>
public sealed record DockApp(
    string Identity,
    string Name,
    string? LaunchId,
    string? ProcessPath,
    string? AppUserModelId,
    bool IsPinned,
    IReadOnlyList<nint> Windows,
    bool IsFocused,
    nint MostRecentWindow = 0)
{
    /// <summary>True when the app has at least one open window.</summary>
    public bool IsRunning => Windows.Count > 0;
}
