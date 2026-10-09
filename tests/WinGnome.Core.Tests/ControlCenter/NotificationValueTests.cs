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
    public void Read_NocValueWinsAndToastEnabledIsTheFallback(int? noc, int? toast, bool expected)
    {
        Assert.Equal(expected, NotificationMaster.Read(noc, toast));
    }
}
