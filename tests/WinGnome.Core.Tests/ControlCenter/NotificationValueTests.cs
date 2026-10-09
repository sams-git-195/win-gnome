using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class NotificationValueTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(-1, true)]
    public void IsOn_AbsentMeansOn(int? value, bool expected)
    {
        Assert.Equal(expected, NotificationValue.IsOn(value));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(null, 0, false)]
    [InlineData(null, 1, true)]
    [InlineData(0, null, false)]
    public void Read_ToastEnabledWinsAndTheNocValueIsTheFallback(int? toastEnabled, int? noc, bool expected)
    {
        Assert.Equal(expected, NotificationMaster.Read(toastEnabled, noc));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(null, 0, false)]
    [InlineData(null, 1, true)]
    [InlineData(0, null, false)]
    public void LockScreenRead_LockScreenToastEnabledWinsAndTheNocValueIsTheFallback(int? lockScreen, int? noc, bool expected)
    {
        Assert.Equal(expected, NotificationLockScreen.Read(lockScreen, noc));
    }

    [Fact]
    public void IsBlockedByPolicy_AnyPolicyValueOfOneBlocks()
    {
        Assert.True(NotificationLockScreen.IsBlockedByPolicy([null, 0, 1, null]));
        Assert.True(NotificationLockScreen.IsBlockedByPolicy([1]));
    }

    [Fact]
    public void IsBlockedByPolicy_AbsentOrZeroPolicyDoesNotBlock()
    {
        Assert.False(NotificationLockScreen.IsBlockedByPolicy([]));
        Assert.False(NotificationLockScreen.IsBlockedByPolicy([null, null, 0, 2]));
    }
}
