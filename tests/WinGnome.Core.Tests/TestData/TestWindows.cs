using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.TestData;

/// <summary>Builds <see cref="WindowInfo"/> values for tests. Use <c>with</c> to tweak individual flags.</summary>
internal static class TestWindows
{
    /// <summary>A normal, decorated, visible 800x600 application window.</summary>
    public static WindowInfo Normal() => new(
        Handle: 100,
        Title: "Untitled - Notepad",
        ClassName: "Notepad",
        ProcessId: 4242,
        ProcessPath: @"C:\Windows\System32\notepad.exe",
        AppUserModelId: null,
        IsVisible: true,
        IsCloaked: false,
        IsMinimized: false,
        IsMaximized: false,
        IsToolWindow: false,
        IsAppWindow: false,
        IsNoActivate: false,
        HasOwner: false,
        HasCaption: true,
        HasSystemMenu: true,
        IsElevated: false,
        Bounds: PixelRect.FromSize(100, 100, 800, 600));
}
