using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class FirstDayOfWeekTests
{
    [Theory]
    [InlineData(0, DayOfWeek.Monday)]
    [InlineData(1, DayOfWeek.Tuesday)]
    [InlineData(2, DayOfWeek.Wednesday)]
    [InlineData(3, DayOfWeek.Thursday)]
    [InlineData(4, DayOfWeek.Friday)]
    [InlineData(5, DayOfWeek.Saturday)]
    [InlineData(6, DayOfWeek.Sunday)]
    public void FromWindows_MapsMondayFirst(int value, DayOfWeek expected)
    {
        Assert.Equal(expected, FirstDayOfWeek.FromWindows(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void FromWindows_OutOfRange_ReturnsNull(int value)
    {
        Assert.Null(FirstDayOfWeek.FromWindows(value));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 0)]
    [InlineData(DayOfWeek.Tuesday, 1)]
    [InlineData(DayOfWeek.Wednesday, 2)]
    [InlineData(DayOfWeek.Thursday, 3)]
    [InlineData(DayOfWeek.Friday, 4)]
    [InlineData(DayOfWeek.Saturday, 5)]
    [InlineData(DayOfWeek.Sunday, 6)]
    public void ToWindows_MapsMondayFirst(DayOfWeek day, int expected)
    {
        Assert.Equal(expected, FirstDayOfWeek.ToWindows(day));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Saturday)]
    public void ToWindows_ThenFromWindows_ReturnsTheSameDay(DayOfWeek day)
    {
        Assert.Equal(day, FirstDayOfWeek.FromWindows(FirstDayOfWeek.ToWindows(day)));
    }

    [Theory]
    [InlineData("0", DayOfWeek.Monday)]
    [InlineData("6", DayOfWeek.Sunday)]
    public void FromWindowsText_Digit_Maps(string text, DayOfWeek expected)
    {
        Assert.Equal(expected, FirstDayOfWeek.FromWindows(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("x")]
    [InlineData("1 ")]
    public void FromWindowsText_Malformed_ReturnsNull(string? text)
    {
        Assert.Null(FirstDayOfWeek.FromWindows(text));
    }

    [Fact]
    public void All_IsMondayFirst()
    {
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday],
            FirstDayOfWeek.All);
    }
}
