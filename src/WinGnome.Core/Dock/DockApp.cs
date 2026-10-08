namespace WinGnome.Core.Dock;

/// <summary>One icon in the dock: a pinned launcher, a running app, or both.</summary>
public sealed record DockApp(
    string Identity,
    string Name,
    string? LaunchId,
    string? ProcessPath,
    string? AppUserModelId,
    bool IsPinned,
    IReadOnlyList<nint> Windows,
    bool IsFocused)
{
    /// <summary>True when the app has at least one open window.</summary>
    public bool IsRunning => Windows.Count > 0;
}
