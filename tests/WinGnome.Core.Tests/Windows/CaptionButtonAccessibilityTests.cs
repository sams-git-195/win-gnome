using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class CaptionButtonAccessibilityTests
{
    [Theory]
    [InlineData(CaptionButtonKind.Close, false, "Close")]
    [InlineData(CaptionButtonKind.Close, true, "Close")]
    [InlineData(CaptionButtonKind.Minimize, false, "Minimise")]
    [InlineData(CaptionButtonKind.Minimize, true, "Minimise")]
    [InlineData(CaptionButtonKind.Maximize, false, "Maximise")]
    [InlineData(CaptionButtonKind.Maximize, true, "Restore")]
    public void Name_FollowsKindAndWindowState(CaptionButtonKind kind, bool maximized, string expected)
    {
        Assert.Equal(expected, CaptionButtonAccessibility.Name(kind, maximized));
    }

    [Theory]
    [InlineData(CaptionButtonKind.Close, "Close")]
    [InlineData(CaptionButtonKind.Minimize, "Minimize")]
    [InlineData(CaptionButtonKind.Maximize, "Maximize")]
    public void AutomationId_IsStable(CaptionButtonKind kind, string expected)
    {
        Assert.Equal(expected, CaptionButtonAccessibility.AutomationId(kind));
    }

    [Theory]
    [InlineData(-1, 1, 0)]    // first available
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 2)]     // stops at the end
    [InlineData(2, -1, 1)]
    [InlineData(0, -1, 0)]    // stops at the start
    public void MoveFocus_AllAvailable_StepsWithoutWrapping(int current, int step, int expected)
    {
        Assert.Equal(expected, CaptionButtonAccessibility.MoveFocus([true, true, true], current, step));
    }

    [Theory]
    [InlineData(0, 1, 2)]     // skips the unavailable middle circle
    [InlineData(2, -1, 0)]
    [InlineData(-1, 1, 0)]
    public void MoveFocus_SkipsUnavailable(int current, int step, int expected)
    {
        Assert.Equal(expected, CaptionButtonAccessibility.MoveFocus([true, false, true], current, step));
    }

    [Fact]
    public void MoveFocus_FirstUnavailable_StartsAtTheNextOne()
    {
        Assert.Equal(1, CaptionButtonAccessibility.MoveFocus([false, true, true], -1, 1));
    }

    [Fact]
    public void MoveFocus_NothingFurther_StaysPut()
    {
        Assert.Equal(1, CaptionButtonAccessibility.MoveFocus([false, true, false], 1, 1));
        Assert.Equal(1, CaptionButtonAccessibility.MoveFocus([false, true, false], 1, -1));
    }

    [Fact]
    public void MoveFocus_NoneAvailable_ReturnsMinusOne()
    {
        Assert.Equal(-1, CaptionButtonAccessibility.MoveFocus([false, false, false], -1, 1));
        Assert.Equal(-1, CaptionButtonAccessibility.MoveFocus([], -1, 1));
    }

    [Fact]
    public void MoveFocus_CurrentPastTheEnd_FindsTheLastAvailable()
    {
        // A layout that lost circles (settings changed to close-only) while one past the new end was focused.
        Assert.Equal(0, CaptionButtonAccessibility.MoveFocus([true], 2, -1));
    }
}
