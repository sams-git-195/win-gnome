using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class StartupEntryTests
{
    private const string Exe = @"C:\Apps\WinGnome\WinGnome.exe";

    [Fact]
    public void ValueFor_QuotesThePath() =>
        Assert.Equal("\"C:\\Apps\\WinGnome\\WinGnome.exe\"", StartupEntry.ValueFor(Exe));

    [Fact]
    public void ValueFor_RejectsBlankPath() =>
        Assert.Throws<ArgumentException>(() => StartupEntry.ValueFor(" "));

    [Fact]
    public void Plan_EnabledAndAbsent_Writes()
    {
        var plan = StartupEntry.Plan(true, Exe, null);
        Assert.Equal(StartupAction.Write, plan.Action);
        Assert.Equal(StartupEntry.ValueFor(Exe), plan.Value);
    }

    [Fact]
    public void Plan_EnabledAndDifferent_Writes()
    {
        var plan = StartupEntry.Plan(true, Exe, "\"D:\\old\\WinGnome.exe\"");
        Assert.Equal(StartupAction.Write, plan.Action);
    }

    [Fact]
    public void Plan_EnabledAndAlreadyCurrent_DoesNothingEvenIfCaseDiffers()
    {
        var plan = StartupEntry.Plan(true, Exe, StartupEntry.ValueFor(Exe).ToUpperInvariant());
        Assert.Equal(StartupAction.None, plan.Action);
    }

    [Fact]
    public void Plan_EnabledWithoutPath_DoesNothing() =>
        Assert.Equal(StartupAction.None, StartupEntry.Plan(true, null, null).Action);

    [Fact]
    public void Plan_DisabledAndPresent_Deletes() =>
        Assert.Equal(StartupAction.Delete, StartupEntry.Plan(false, Exe, "\"x\"").Action);

    [Fact]
    public void Plan_DisabledAndAbsent_DoesNothing() =>
        Assert.Equal(StartupAction.None, StartupEntry.Plan(false, Exe, null).Action);

    private const string DefaultProfile = @"C:\Users\a\AppData\Roaming\WinGnome";

    [Fact]
    public void IsOwnedBy_DefaultProfile_IsTrue() =>
        Assert.True(StartupEntry.IsOwnedBy(DefaultProfile, DefaultProfile));

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Roaming\WinGnome\")]
    [InlineData(@"c:\users\A\appdata\roaming\wingnome")]
    public void IsOwnedBy_DefaultProfileSpelledDifferently_IsTrue(string directory) =>
        Assert.True(StartupEntry.IsOwnedBy(directory, DefaultProfile));

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Temp\wingnome-smoke")]
    [InlineData(@"C:\Users\a\AppData\Roaming\WinGnomeQA")]
    [InlineData(@"C:\Users\a\AppData\Roaming\WinGnome\profile2")]
    [InlineData("")]
    public void IsOwnedBy_OtherProfile_IsFalse(string directory) =>
        Assert.False(StartupEntry.IsOwnedBy(directory, DefaultProfile));
}
