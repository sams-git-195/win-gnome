using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class TrayFrontCheckScheduleTests
{
    [Fact]
    public void OnBroadcast_StartsTheFastBurst()
    {
        var schedule = new TrayFrontCheckSchedule();

        Assert.Equal(15u, schedule.OnBroadcast(1000));
    }

    [Theory]
    [InlineData(4999, null)]
    [InlineData(5000, 1000u)]
    public void OnTick_BurstEndsAfterFourSeconds(long nowMs, uint? expected)
    {
        var schedule = new TrayFrontCheckSchedule();
        schedule.OnBroadcast(1000);

        Assert.Equal(expected, schedule.OnTick(nowMs));
    }

    [Fact]
    public void OnShellActivity_AtRest_SwitchesToTheActiveRate()
    {
        var schedule = new TrayFrontCheckSchedule();
        schedule.OnBroadcast(0);
        schedule.OnTick(10_000);

        Assert.Equal(250u, schedule.OnShellActivity(20_000));
    }

    [Theory]
    [InlineData(21_999, null)]
    [InlineData(22_000, 1000u)]
    public void OnTick_ActiveRateEndsAfterTwoSeconds(long nowMs, uint? expected)
    {
        var schedule = new TrayFrontCheckSchedule();
        schedule.OnShellActivity(20_000);

        Assert.Equal(expected, schedule.OnTick(nowMs));
    }

    [Fact]
    public void OnShellActivity_Repeated_ExtendsWithoutRescheduling()
    {
        var schedule = new TrayFrontCheckSchedule();
        schedule.OnShellActivity(20_000);

        Assert.Null(schedule.OnShellActivity(21_500));
        Assert.Null(schedule.OnTick(23_000));
        Assert.Equal(1000u, schedule.OnTick(23_500));
    }

    [Fact]
    public void OnShellActivity_DuringTheBurst_KeepsTheBurstRate()
    {
        var schedule = new TrayFrontCheckSchedule();
        schedule.OnBroadcast(0);

        Assert.Null(schedule.OnShellActivity(1000));
        Assert.Equal(15u, schedule.IntervalAt(3999));
    }

    [Fact]
    public void OnTick_ActivityOutlastingTheBurst_FallsBackToTheActiveRate()
    {
        var schedule = new TrayFrontCheckSchedule();
        schedule.OnBroadcast(0);
        schedule.OnShellActivity(3000);

        Assert.Equal(250u, schedule.OnTick(4000));
        Assert.Equal(1000u, schedule.OnTick(5000));
    }

    [Theory]
    [InlineData(1, true)]       // HSHELL_WINDOWCREATED
    [InlineData(2, true)]       // HSHELL_WINDOWDESTROYED
    [InlineData(4, true)]       // HSHELL_WINDOWACTIVATED
    [InlineData(13, true)]      // HSHELL_WINDOWREPLACED
    [InlineData(16, true)]      // HSHELL_MONITORCHANGED
    [InlineData(0x8004, true)]  // HSHELL_RUDEAPPACTIVATED
    [InlineData(6, false)]      // HSHELL_REDRAW (title changes)
    [InlineData(0x8006, false)] // HSHELL_FLASH
    [InlineData(12, false)]     // HSHELL_APPCOMMAND
    [InlineData(0, false)]
    public void IsShellActivity_OnlyWindowLifecycleAndActivation(int code, bool expected)
    {
        Assert.Equal(expected, TrayFrontCheckSchedule.IsShellActivity(code));
    }

    [Theory]
    [InlineData(0x02u, false)] // ABM_QUERYPOS
    [InlineData(0x04u, false)] // ABM_GETSTATE
    [InlineData(0x05u, false)] // ABM_GETTASKBARPOS
    [InlineData(0x07u, false)] // ABM_GETAUTOHIDEBAR
    [InlineData(0x0Bu, false)] // ABM_GETAUTOHIDEBAREX
    [InlineData(0x00u, true)]  // ABM_NEW
    [InlineData(0x01u, true)]  // ABM_REMOVE
    [InlineData(0x03u, true)]  // ABM_SETPOS
    [InlineData(0x06u, true)]  // ABM_ACTIVATE
    [InlineData(0x08u, true)]  // ABM_SETAUTOHIDEBAR
    [InlineData(0x09u, true)]  // ABM_WINDOWPOSCHANGED
    [InlineData(0x0Au, true)]  // ABM_SETSTATE
    [InlineData(0x0Cu, true)]  // ABM_SETAUTOHIDEBAREX
    [InlineData(null, true)]   // unreadable block
    public void IsAppBarActivity_ReadOnlyQueriesAreNot(uint? message, bool expected)
    {
        Assert.Equal(expected, TrayFrontCheckSchedule.IsAppBarActivity(message));
    }
}
