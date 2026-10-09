using WinGnome.Core.Monitors;
using static WinGnome.Core.Tests.Monitors.MonitorLayoutTests;

namespace WinGnome.Core.Tests.Monitors;

public class MonitorFocusTrackerTests
{
    private static MonitorFocusTracker With(params (nint Hwnd, string Key, bool Minimized)[] windows)
    {
        var tracker = new MonitorFocusTracker();
        tracker.OnWindowsChanged(windows.ToDictionary(w => w.Hwnd, w => new TrackedWindow(w.Key, w.Minimized)));
        return tracker;
    }

    [Fact]
    public void Current_NothingFocused_IsZero()
    {
        var tracker = With((1, "A", false));

        Assert.Equal(0, tracker.Current("A"));
    }

    [Fact]
    public void Current_IsTheMostRecentlyFocusedWindowOnEachMonitor()
    {
        var tracker = With((1, "A", false), (2, "A", false), (3, "B", false));

        tracker.OnForeground(1, "A");
        tracker.OnForeground(3, "B");
        tracker.OnForeground(2, "A");

        Assert.Equal(2, tracker.Current("A"));
        Assert.Equal(3, tracker.Current("B"));
    }

    [Fact]
    public void Current_KeyIsCaseInsensitive()
    {
        var tracker = With((1, @"\\.\DISPLAY1", false));
        tracker.OnForeground(1, @"\\.\DISPLAY1");

        Assert.Equal(1, tracker.Current(@"\\.\display1"));
    }

    [Fact]
    public void Current_SkipsMinimisedWindows()
    {
        var tracker = With((1, "A", false), (2, "A", false));
        tracker.OnForeground(1, "A");
        tracker.OnForeground(2, "A");

        tracker.OnWindowsChanged(new Dictionary<nint, TrackedWindow> { [1] = new("A", false), [2] = new("A", true) });

        Assert.Equal(1, tracker.Current("A"));
    }

    [Fact]
    public void WindowMovedToAnotherMonitor_TakesItsRecencyWithIt()
    {
        var tracker = With((1, "A", false), (2, "B", false));
        tracker.OnForeground(2, "B");
        tracker.OnForeground(1, "A");

        // Win+Shift+Right moves window 1 onto B.
        tracker.OnWindowsChanged(new Dictionary<nint, TrackedWindow> { [1] = new("B", false), [2] = new("B", false) });

        Assert.Equal(0, tracker.Current("A"));
        Assert.Equal(1, tracker.Current("B"));
    }

    [Fact]
    public void OnForeground_OnAnotherMonitor_UpdatesWhereTheWindowIs()
    {
        var tracker = With((1, "A", false));
        tracker.OnForeground(1, "A");

        tracker.OnForeground(1, "B");

        Assert.Equal(0, tracker.Current("A"));
        Assert.Equal(1, tracker.Current("B"));
    }

    [Fact]
    public void ClosedWindow_IsPruned_AndAReusedHandleStartsFresh()
    {
        var tracker = With((1, "A", false), (2, "A", false));
        tracker.OnForeground(2, "A");
        tracker.OnForeground(1, "A");

        tracker.OnWindowsChanged(new Dictionary<nint, TrackedWindow> { [2] = new("A", false) });
        Assert.Equal(2, tracker.Current("A"));

        // A new window gets handle 1 again: it has no recency until it is focused.
        tracker.OnWindowsChanged(new Dictionary<nint, TrackedWindow> { [1] = new("A", false), [2] = new("A", false) });
        Assert.Equal(2, tracker.Current("A"));
    }

    [Fact]
    public void LayoutChange_DropsWindowsOnRemovedMonitors()
    {
        var tracker = With((1, "A", false), (2, "B", false));
        tracker.OnForeground(1, "A");
        tracker.OnForeground(2, "B");

        tracker.OnLayoutChanged(MonitorLayout.Create([Mon("A", 0, 0, 1920, 1080, primary: true)]));

        Assert.Equal(0, tracker.Current("B"));
        Assert.Equal(1, tracker.Current("A"));
    }

    [Fact]
    public void OnForeground_IgnoresTheNullWindowAndEmptyKeys()
    {
        var tracker = With((1, "A", false));
        tracker.OnForeground(1, "A");

        tracker.OnForeground(0, "A");
        tracker.OnForeground(1, "");

        Assert.Equal(1, tracker.Current("A"));
    }
}
