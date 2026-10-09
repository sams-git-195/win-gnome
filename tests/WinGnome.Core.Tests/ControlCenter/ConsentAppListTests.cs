using System.Globalization;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class ConsentAppListTests
{
    // 2026-10-09 15:00 UTC, in UTC with the invariant culture so the texts don't depend on this machine.
    private static readonly ConsentClock Clock = new(new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc, CultureInfo.InvariantCulture);

    private static long At(int year, int month, int day, int hour, int minute) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc).ToFileTimeUtc();

    private static ConsentKeySnapshot Packaged(string name, string? value = null, long start = 0, long stop = 0) => new(name, false, value, start, stop);

    private static ConsentKeySnapshot Desktop(string name, long start = 0, long stop = 0) => new(name, true, null, start, stop);

    private static string? Names(string family) => family switch
    {
        "Microsoft.WindowsCamera_8wekyb3d8bbwe" => "Camera",
        "Claude_pzs8sxrjxfjjc" => "Claude",
        _ => null,
    };

    [Fact]
    public void Build_NoKeys_IsEmpty()
    {
        Assert.Empty(ConsentAppList.Build([], Names, Clock));
    }

    [Fact]
    public void Build_PackagedAppsComeFirstThenDesktopAppsEachSortedByName()
    {
        var rows = ConsentAppList.Build(
            [
                Desktop(@"C:#Games#zed.exe"),
                Packaged("Microsoft.WindowsCamera_8wekyb3d8bbwe", "Allow"),
                Desktop(@"C:#Tools#Alpha.exe"),
                Packaged("Claude_pzs8sxrjxfjjc", "Deny"),
            ],
            Names, Clock);

        Assert.Equal(["Camera", "Claude", "Alpha", "zed"], rows.Select(r => r.Name));
        Assert.Equal([false, false, true, true], rows.Select(r => r.IsDesktop));
    }

    [Fact]
    public void Build_ReadsTheAppsOwnValue()
    {
        var rows = ConsentAppList.Build([Packaged("Claude_pzs8sxrjxfjjc", "Deny"), Packaged("Microsoft.WindowsCamera_8wekyb3d8bbwe")], Names, Clock);

        Assert.Equal(["Camera", "Claude"], rows.Select(r => r.Name));
        Assert.Equal([ConsentState.Absent, ConsentState.Deny], rows.Select(r => r.Value));
    }

    [Fact]
    public void Build_HidesUnnamedPackagesAndTheNonPackagedKey()
    {
        var rows = ConsentAppList.Build([Packaged("Microsoft.BioEnrollment_cw5n1h2txyewy"), Packaged("NonPackaged", "Allow")], Names, Clock);

        Assert.Empty(rows);
    }

    [Theory]
    [InlineData(@"C:#Program Files (x86)#Steam#steamapps#common#FC 26#FC26.exe", "FC26")]
    [InlineData(@"C:#Users#samhe#AppData#Local#Programs#@granolaelectron#Granola.exe", "Granola")]
    [InlineData(@"C:#Windows#System32#rundll32.exe", "rundll32")]
    [InlineData(@"D:#tool.v2.exe", "tool.v2")]
    [InlineData(@"D:#noextension", "noextension")]
    public void Build_NamesADesktopAppAfterItsFile(string key, string expectedName)
    {
        var rows = ConsentAppList.Build([Desktop(key)], Names, Clock);

        Assert.Equal(expectedName, Assert.Single(rows).Name);
        Assert.Equal(key, rows[0].Key);
    }

    [Theory]
    [InlineData("Executables")]
    [InlineData(@"C:#folder#")]
    public void Build_SkipsDesktopKeysWithoutAFile(string key)
    {
        Assert.Empty(ConsentAppList.Build([Desktop(key)], Names, Clock));
    }

    [Fact]
    public void Build_StartedAndNotStopped_IsInUse()
    {
        var rows = ConsentAppList.Build([Desktop(@"C:#a#App.exe", start: At(2026, 10, 9, 14, 0), stop: 0)], Names, Clock);

        Assert.Equal(new ConsentAppRow("App", @"C:#a#App.exe", true, ConsentState.Absent, ConsentUse.InUse, "In use"), Assert.Single(rows));
    }

    [Fact]
    public void Build_StartedAndStopped_ShowsTheStopTime()
    {
        var rows = ConsentAppList.Build([Desktop(@"C:#a#App.exe", start: At(2026, 10, 9, 13, 0), stop: At(2026, 10, 9, 14, 5))], Names, Clock);

        Assert.Equal((ConsentUse.LastUsed, "Last used Today at 14:05"), (rows[0].Use, rows[0].UseText));
    }

    [Fact]
    public void Build_StopWithoutStart_ShowsTheStopTime()
    {
        var rows = ConsentAppList.Build([Desktop(@"C:#a#App.exe", start: 0, stop: At(2026, 10, 8, 9, 30))], Names, Clock);

        Assert.Equal((ConsentUse.LastUsed, "Last used Yesterday at 09:30"), (rows[0].Use, rows[0].UseText));
    }

    [Fact]
    public void Build_NeverUsed_HasNoUseText()
    {
        var rows = ConsentAppList.Build([Desktop(@"C:#a#App.exe")], Names, Clock);

        Assert.Equal((ConsentUse.None, (string?)null), (rows[0].Use, rows[0].UseText));
    }

    [Theory]
    [InlineData(2026, 10, 9, 0, 1, "Today at 00:01")]
    [InlineData(2026, 10, 8, 23, 59, "Yesterday at 23:59")]
    [InlineData(2026, 10, 7, 12, 0, "2 days ago")]
    [InlineData(2026, 10, 3, 12, 0, "6 days ago")]
    [InlineData(2026, 10, 2, 12, 0, "2 Oct 2026")]
    [InlineData(2025, 1, 5, 8, 0, "5 Jan 2025")]
    public void LastUsedText_CountsCalendarDays(int year, int month, int day, int hour, int minute, string expected)
    {
        Assert.Equal(expected, ConsentAppList.LastUsedText(At(year, month, day, hour, minute), Clock));
    }

    [Fact]
    public void LastUsedText_CountsDaysInTheClocksZone()
    {
        // 23:30 UTC on the 8th is 01:30 on the 9th at UTC+2, so it is "today" there but "yesterday" in UTC.
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        var clock = Clock with { Zone = zone };

        Assert.Equal("Today at 01:30", ConsentAppList.LastUsedText(At(2026, 10, 8, 23, 30), clock));
        Assert.Equal("Yesterday at 23:30", ConsentAppList.LastUsedText(At(2026, 10, 8, 23, 30), Clock));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(long.MaxValue)]
    public void LastUsedText_NoOrInvalidTime_IsNull(long fileTime)
    {
        Assert.Null(ConsentAppList.LastUsedText(fileTime, Clock));
    }

    [Fact]
    public void LastUsedText_FutureTime_ReadsAsToday()
    {
        Assert.Equal("Today at 15:30", ConsentAppList.LastUsedText(At(2026, 10, 9, 15, 30), Clock));
    }
}
