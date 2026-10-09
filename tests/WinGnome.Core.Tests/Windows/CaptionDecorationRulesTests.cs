using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class CaptionDecorationRulesTests
{
    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("Chrome_WidgetWin_0")]   // Docker Desktop
    [InlineData("Chrome_WidgetWin_2")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("WinUIDesktopWin32WindowClass")]
    [InlineData("MozillaWindowClass")]
    [InlineData("Ghost")]
    public void SkipsAppsThatDrawTheirOwnTitleBar(string className)
    {
        Assert.True(CaptionDecorationRules.IsSkippedClass(className));
    }

    [Theory]
    [InlineData("CabinetWClass")]
    [InlineData("Notepad")]
    [InlineData("ApplicationFrameWindow")] // decorated whenever DWM reports real caption buttons
    [InlineData("chrome_widgetwin_1")]     // class names are case-sensitive
    [InlineData("Chrome_WidgetWin")]       // not the Chromium family prefix
    [InlineData("")]
    [InlineData(null)]
    public void DecoratesOtherClasses(string? className)
    {
        Assert.False(CaptionDecorationRules.IsSkippedClass(className));
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1", true)]
    [InlineData("Chrome_WidgetWin_0", true)]
    [InlineData("WinUIDesktopWin32WindowClass", true)]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", true)]
    [InlineData("Ghost", false)]          // DWM's hung-window stand-in
    [InlineData("CabinetWClass", false)]  // not skipped, so decorated the normal way
    [InlineData(null, false)]
    public void CanProbeSkippedClass_AllSkippedClassesButGhost(string? className, bool expected)
    {
        Assert.Equal(expected, CaptionDecorationRules.CanProbeSkippedClass(className));
    }

    [Theory]
    [InlineData("GitHubDesktop", "Chrome_WidgetWin_1", 45.0)]
    [InlineData("githubdesktop", "Chrome_WidgetWin_1", 45.0)]  // process names are not case-sensitive
    [InlineData("GitHubDesktop", "Chrome_WidgetWin_0", null)]  // another window class of the same app
    [InlineData("Code", "Chrome_WidgetWin_1", null)]
    [InlineData(null, "Chrome_WidgetWin_1", null)]
    [InlineData("GitHubDesktop", null, null)]
    public void WebButtonWidth_OnlyForProfiledApps(string? processName, string? className, double? expected)
    {
        Assert.Equal(expected, CaptionDecorationRules.WebButtonWidth(processName, className));
    }

    [Theory]
    [InlineData(@"C:\Users\sam\AppData\Local\GitHubDesktop\app-3.6.6\GitHubDesktop.exe", 45.0)]
    [InlineData(@"C:\Users\sam\AppData\Local\Programs\Microsoft VS Code\Code.exe", null)]
    [InlineData(null, null)]
    public void WebButtonWidth_ForAWindow_UsesItsExecutableName(string? processPath, double? expected)
    {
        var window = new WindowInfo(
            1, "GitHub Desktop", "Chrome_WidgetWin_1", 42, processPath, null, IsVisible: true, IsCloaked: false,
            IsMinimized: false, IsMaximized: false, IsToolWindow: false, IsAppWindow: false, IsNoActivate: false,
            HasOwner: false, HasCaption: true, HasSystemMenu: false, IsElevated: false, new(0, 0, 1262, 917));

        Assert.Equal(expected, CaptionDecorationRules.WebButtonWidth(window));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void HasButtonTrio_NeedsAMinimiseOrMaximiseBox(bool minimize, bool maximize, bool expected)
    {
        Assert.Equal(expected, CaptionDecorationRules.HasButtonTrio(minimize, maximize));
    }

    [Theory]
    [InlineData(120u, 120u, false)]
    [InlineData(96u, 120u, true)]  // DPI-unaware app on a 125 % monitor
    [InlineData(144u, 96u, true)]  // system-aware app on a monitor with a lower DPI
    [InlineData(0u, 120u, false)]  // unknown: do not reject
    [InlineData(120u, 0u, false)]
    public void IsDpiVirtualized_WhenWindowAndMonitorDpiDiffer(uint windowDpi, uint monitorDpi, bool expected)
    {
        Assert.Equal(expected, CaptionDecorationRules.IsDpiVirtualized(windowDpi, monitorDpi));
    }
}
