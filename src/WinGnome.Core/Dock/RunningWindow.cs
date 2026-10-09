namespace WinGnome.Core.Dock;

/// <summary>
/// A running top-level window as the dock sees it. <paramref name="Identity"/> comes from
/// <see cref="WinGnome.Core.Windows.AppIdentity"/>. A minimised window never counts as the focused one.
/// </summary>
public sealed record RunningWindow(
    nint Handle,
    string Identity,
    string Title,
    string AppName,
    string? ProcessPath,
    string? AppUserModelId,
    bool IsMinimized = false);
