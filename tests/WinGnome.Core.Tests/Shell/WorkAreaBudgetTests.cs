using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class WorkAreaBudgetTests
{
    [Fact]
    public void TrySpend_ThreeInsideTheWindow_TheFourthIsRefused()
    {
        var budget = new WorkAreaBudget();

        Assert.True(budget.TrySpend(0));
        Assert.True(budget.TrySpend(1000));
        Assert.True(budget.TrySpend(2000));
        Assert.False(budget.TrySpend(3000));
        Assert.Equal(3, budget.Spent);
    }

    [Fact]
    public void TrySpend_ClockDoesNotMove_StillRefusesTheFourth()
    {
        var budget = new WorkAreaBudget();

        Assert.True(budget.TrySpend(500));
        Assert.True(budget.TrySpend(500));
        Assert.True(budget.TrySpend(500));
        Assert.False(budget.TrySpend(500));
    }

    [Theory]
    [InlineData(59_999, false)] // One millisecond before the first application leaves the window.
    [InlineData(60_000, true)]  // Exactly 60 s after it.
    [InlineData(60_001, true)]  // And after.
    public void TrySpend_WindowBoundary(long nowMs, bool expected)
    {
        var budget = new WorkAreaBudget();
        budget.TrySpend(0);
        budget.TrySpend(1000);
        budget.TrySpend(2000);

        Assert.Equal(expected, budget.TrySpend(nowMs));
    }

    [Fact]
    public void TrySpend_WindowSlides_OneApplicationLeavesItPerSecond()
    {
        var budget = new WorkAreaBudget();
        budget.TrySpend(0);
        budget.TrySpend(1000);
        budget.TrySpend(2000);

        Assert.True(budget.TrySpend(60_000));  // The one at 0 has left.
        Assert.True(budget.TrySpend(61_000));  // The one at 1000 has left.
        Assert.True(budget.TrySpend(62_000));  // The one at 2000 has left.
        Assert.False(budget.TrySpend(63_000)); // The window now holds 60000, 61000 and 62000.
        Assert.Equal(3, budget.Spent);
    }

    [Fact]
    public void TrySpend_RefusedApplication_DoesNotExtendTheWindow()
    {
        var budget = new WorkAreaBudget();
        budget.TrySpend(0);
        budget.TrySpend(0);
        budget.TrySpend(0);
        Assert.False(budget.TrySpend(1000));

        // The refusal was not recorded, so the window still empties 60 s after the three real applications.
        Assert.True(budget.TrySpend(60_000));
    }
}
