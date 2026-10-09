using WinGnome.Core.Collections;
using Xunit;

namespace WinGnome.Core.Tests.Collections;

public sealed class DeadlineScheduleTests
{
    [Fact]
    public void NextDue_Empty_IsNull()
    {
        Assert.Null(new DeadlineSchedule<string>().NextDue);
    }

    [Fact]
    public void NextDue_IsTheEarliestDeadline()
    {
        var schedule = new DeadlineSchedule<string>();
        schedule.Set("a", 100);
        schedule.Set("b", 50);

        Assert.Equal(50, schedule.NextDue);
    }

    [Fact]
    public void Set_ExistingKey_ReplacesItsDeadline()
    {
        // Debouncing: every new event pushes the deadline back.
        var schedule = new DeadlineSchedule<string>();
        schedule.Set("a", 100);
        schedule.Set("a", 300);

        Assert.Equal(300, schedule.NextDue);
        Assert.Equal(1, schedule.Count);
    }

    [Fact]
    public void TakeDue_RemovesOnlyKeysThatAreDue_IncludingExactlyNow()
    {
        var schedule = new DeadlineSchedule<string>();
        schedule.Set("a", 100);
        schedule.Set("b", 50);
        schedule.Set("c", 101);
        var due = new List<string>();

        schedule.TakeDue(100, due);

        Assert.Equal(["a", "b"], due.Order());
        Assert.Equal(101, schedule.NextDue);
        Assert.False(schedule.Contains("a"));
        Assert.True(schedule.Contains("c"));
    }

    [Fact]
    public void TakeDue_NothingDue_LeavesTheQueue()
    {
        var schedule = new DeadlineSchedule<string>();
        schedule.Set("a", 100);
        var due = new List<string>();

        schedule.TakeDue(99, due);

        Assert.Empty(due);
        Assert.Equal(1, schedule.Count);
    }

    [Fact]
    public void Remove_DropsTheKey()
    {
        var schedule = new DeadlineSchedule<string>();
        schedule.Set("a", 100);

        Assert.True(schedule.Remove("a"));
        Assert.False(schedule.Remove("a"));
        Assert.Null(schedule.NextDue);
    }

    [Fact]
    public void Clear_EmptiesTheQueue()
    {
        var schedule = new DeadlineSchedule<string>();
        schedule.Set("a", 100);

        schedule.Clear();

        Assert.Equal(0, schedule.Count);
    }
}
