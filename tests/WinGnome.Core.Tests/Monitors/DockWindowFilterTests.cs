using WinGnome.Core.Dock;
using WinGnome.Core.Monitors;

namespace WinGnome.Core.Tests.Monitors;

public class DockWindowFilterTests
{
    private static RunningWindow Window(nint hwnd) => new(hwnd, "app" + hwnd, "Title", "App", null, null);

    private static readonly IReadOnlyList<RunningWindow> Windows = [Window(1), Window(2), Window(3)];

    private static string MonitorOf(nint hwnd) => hwnd == 2 ? "B" : "A";

    [Fact]
    public void ForMonitor_NotIsolated_KeepsEveryWindow()
    {
        Assert.Equal(Windows, DockWindowFilter.ForMonitor(Windows, MonitorOf, "B", isolate: false));
    }

    [Fact]
    public void ForMonitor_Isolated_KeepsOnlyThatMonitorsWindows()
    {
        Assert.Equal([Window(1), Window(3)], DockWindowFilter.ForMonitor(Windows, MonitorOf, "A", isolate: true));
        Assert.Equal([Window(2)], DockWindowFilter.ForMonitor(Windows, MonitorOf, "B", isolate: true));
    }

    [Fact]
    public void ForMonitor_Isolated_KeyIsCaseInsensitive()
    {
        Assert.Equal([Window(2)], DockWindowFilter.ForMonitor(Windows, MonitorOf, "b", isolate: true));
    }

    [Fact]
    public void ForMonitor_Isolated_MonitorWithoutWindows_IsEmpty()
    {
        Assert.Empty(DockWindowFilter.ForMonitor(Windows, MonitorOf, "C", isolate: true));
    }
}
