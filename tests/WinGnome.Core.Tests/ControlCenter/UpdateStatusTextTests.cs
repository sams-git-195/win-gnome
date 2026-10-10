using System.Globalization;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class UpdateStatusTextTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 16, 30, 0);
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo American = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void When_EarlierToday_SaysTodayAtTime()
    {
        Assert.Equal("Today at 14:05", UpdateStatusText.When(new DateTime(2026, 10, 9, 14, 5, 0), Now, British));
    }

    [Fact]
    public void When_EarlierToday_UsesTheCulturesTimeFormat()
    {
        Assert.Equal("Today at 2:05 PM", UpdateStatusText.When(new DateTime(2026, 10, 9, 14, 5, 0), Now, American));
    }

    [Fact]
    public void When_JustAfterMidnightYesterday_SaysYesterday()
    {
        Assert.Equal("Yesterday at 00:01", UpdateStatusText.When(new DateTime(2026, 10, 8, 0, 1, 0), Now, British));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void When_UnderAWeek_SaysDaysAgo(int days)
    {
        Assert.Equal($"{days} days ago", UpdateStatusText.When(Now.AddDays(-days), Now, British));
    }

    [Fact]
    public void When_ExactlyAWeek_SaysTheDate()
    {
        Assert.Equal("02/10/2026", UpdateStatusText.When(new DateTime(2026, 10, 2, 9, 0, 0), Now, British));
    }

    [Fact]
    public void When_Future_SaysDateAndTime()
    {
        Assert.Equal("10/10/2026 08:00", UpdateStatusText.When(new DateTime(2026, 10, 10, 8, 0, 0), Now, British));
    }

    [Fact]
    public void When_Null_SaysNever()
    {
        Assert.Equal("Never", UpdateStatusText.When(null, Now, British));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1899, 12, 30)]
    public void When_ZeroDate_SaysNever(int year, int month, int day)
    {
        Assert.Equal("Never", UpdateStatusText.When(new DateTime(year, month, day), Now, British));
    }

    [Fact]
    public void Build_NothingWaiting()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(Now.AddHours(-2), Now.AddDays(-3), [], [], false, null), Now, British);

        Assert.Equal("Today at 14:30", lines.LastChecked);
        Assert.Equal("3 days ago", lines.LastInstalled);
        Assert.Equal("No updates waiting", lines.Pending);
        Assert.Empty(lines.PendingTitles);
        Assert.Equal("No restart needed", lines.Restart);
        Assert.Null(lines.Error);
    }

    [Fact]
    public void Build_OneUpdate_UsesSingular()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, ["2026-10 Cumulative Update"], [], false, null), Now, British);

        Assert.Equal("1 update waiting", lines.Pending);
        Assert.Equal(["2026-10 Cumulative Update"], lines.PendingTitles);
    }

    [Fact]
    public void Build_ManyUpdates_UsesCount()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, ["a", "b", "c"], [], true, null), Now, British);

        Assert.Equal("3 updates waiting", lines.Pending);
        Assert.Equal("A restart is needed to finish updating", lines.Restart);
    }

    [Fact]
    public void Build_NothingOptional_HasNoOptionalLine()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, [], [], false, null), Now, British);

        Assert.Null(lines.Optional);
        Assert.Empty(lines.OptionalTitles);
    }

    [Fact]
    public void Build_OnlyOptional_NothingWaitingButOptionalListed()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, [], ["Driver A", "Driver B"], false, null), Now, British);

        Assert.Equal("No updates waiting", lines.Pending);
        Assert.Equal("2 optional updates available", lines.Optional);
        Assert.Equal(["Driver A", "Driver B"], lines.OptionalTitles);
    }

    [Fact]
    public void Build_OneOptional_UsesSingular()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, [], ["Driver A"], false, null), Now, British);

        Assert.Equal("1 optional update available", lines.Optional);
    }

    [Fact]
    public void Build_Mixed_CountsEachKindSeparately()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, ["Cumulative"], ["D1", "D2", "D3"], false, null), Now, British);

        Assert.Equal("1 update waiting", lines.Pending);
        Assert.Equal(["Cumulative"], lines.PendingTitles);
        Assert.Equal("3 optional updates available", lines.Optional);
    }

    [Fact]
    public void Build_Error_HasNoOptionalLine()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, [], ["Driver A"], false, unchecked((int)0x80070422)), Now, British);

        Assert.Null(lines.Optional);
    }

    [Fact]
    public void Build_NeverCheckedOrInstalled()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, [], [], false, null), Now, British);

        Assert.Equal("Never", lines.LastChecked);
        Assert.Equal("Never", lines.LastInstalled);
    }

    [Fact]
    public void Build_Error_PendingIsUnknownAndMessageSet()
    {
        var lines = UpdateStatusText.Build(new UpdateStatus(null, null, [], [], false, unchecked((int)0x80070422)), Now, British);

        Assert.Equal("Unknown", lines.Pending);
        Assert.Equal("The Windows Update service is turned off (0x80070422).", lines.Error);
    }

    [Theory]
    [InlineData(0x80070422u, "The Windows Update service is turned off (0x80070422).")]
    [InlineData(0x80070005u, "Windows doesn't let this account read update information (0x80070005).")]
    [InlineData(0x800705B4u, "Windows Update didn't answer in time.")]
    [InlineData(0x8024402Cu, "Windows Update can't be reached (0x8024402C).")]
    [InlineData(0x80240017u, "Windows Update reported a problem (0x80240017).")]
    [InlineData(0x8024FFFFu, "Windows Update reported a problem (0x8024FFFF).")]
    [InlineData(0x80240000u, "Windows Update reported a problem (0x80240000).")]
    [InlineData(0x80250000u, "Couldn't read the update status (0x80250000).")]
    [InlineData(0x8023FFFFu, "Couldn't read the update status (0x8023FFFF).")]
    [InlineData(0x80004005u, "Couldn't read the update status (0x80004005).")]
    public void ErrorMessage_MapsKnownCodes(uint code, string expected)
    {
        Assert.Equal(expected, UpdateStatusText.ErrorMessage(unchecked((int)code)));
    }

    [Fact]
    public void TimedOut_IsTheTimeoutHresult()
    {
        Assert.Equal("Windows Update didn't answer in time.", UpdateStatusText.ErrorMessage(UpdateStatusText.TimedOut));
    }
}
