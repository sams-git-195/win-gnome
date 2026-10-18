using WinGnome.Core.Dock;

namespace WinGnome.Core.Tests.Dock;

public class DockWindowCloseTests
{
    private static CloseTarget Target(nint handle = 100, int pid = 4242, bool elevated = false) => new(handle, pid, elevated);

    [Fact]
    public void Decide_SameWindowNotElevated_Posts()
    {
        Assert.Equal(CloseDecision.Post, DockWindowClose.Decide(Target(), Target()));
    }

    [Fact]
    public void Decide_NoLiveWindow_IsGone()
    {
        Assert.Equal(CloseDecision.Gone, DockWindowClose.Decide(Target(), live: null));
    }

    [Fact]
    public void Decide_LiveWindowOfAnotherProcess_IsReused()
    {
        Assert.Equal(CloseDecision.Reused, DockWindowClose.Decide(Target(pid: 4242), Target(pid: 4243)));
    }

    [Fact]
    public void Decide_CapturedElevated_IsElevated()
    {
        Assert.Equal(CloseDecision.Elevated, DockWindowClose.Decide(Target(elevated: true), Target(elevated: true)));
    }

    [Fact]
    public void Decide_CapturedElevatedAndLiveNotElevated_IsStillElevated()
    {
        // The dock never offered a close for it, so a stale request must not be honoured either.
        Assert.Equal(CloseDecision.Elevated, DockWindowClose.Decide(Target(elevated: true), Target(elevated: false)));
    }

    [Fact]
    public void Decide_CapturedElevatedAndGone_IsElevated()
    {
        Assert.Equal(CloseDecision.Elevated, DockWindowClose.Decide(Target(elevated: true), live: null));
    }

    [Fact]
    public void Decide_LiveBecameElevated_IsElevated()
    {
        Assert.Equal(CloseDecision.Elevated, DockWindowClose.Decide(Target(elevated: false), Target(elevated: true)));
    }

    [Fact]
    public void Decide_ReusedAndElevated_IsReused()
    {
        // Another process owns the handle now; whatever it is, this is not the window the user chose.
        Assert.Equal(CloseDecision.Reused, DockWindowClose.Decide(Target(pid: 1), Target(pid: 2, elevated: true)));
    }

    [Fact]
    public void Decide_LiveHandleDiffers_IsGone()
    {
        // A live read of some other window must never be mistaken for the captured one.
        Assert.Equal(CloseDecision.Gone, DockWindowClose.Decide(Target(handle: 100), Target(handle: 101)));
    }
}
