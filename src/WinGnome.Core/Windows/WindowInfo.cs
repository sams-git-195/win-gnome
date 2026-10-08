using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>A snapshot of a top-level window, gathered by the app and consumed by pure-logic filters.</summary>
public sealed record WindowInfo(
    nint Handle,
    string Title,
    string? ClassName,
    int ProcessId,
    string? ProcessPath,
    string? AppUserModelId,
    bool IsVisible,
    bool IsCloaked,
    bool IsMinimized,
    bool IsMaximized,
    bool IsToolWindow,
    bool IsAppWindow,
    bool IsNoActivate,
    bool HasOwner,
    bool HasCaption,
    bool HasSystemMenu,
    bool IsElevated,
    PixelRect Bounds);
