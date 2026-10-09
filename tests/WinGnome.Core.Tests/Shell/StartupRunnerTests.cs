using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class StartupRunnerTests
{
    private const string Own = "WinGnome";

    private static StartupEntry E(string name, string command, StartupSource source) => new(name, command, source);

    private static StartupPlan Plan(StartupEntry[] entries, StartupApprovedSet? approved = null, bool safeMode = false) =>
        StartupRunner.BuildPlan(entries, approved ?? new StartupApprovedSet(), Own, safeMode);

    private static string[] Names(StartupPlan plan) => [.. plan.Steps.Select(s => s.Entry.Name)];

    [Fact]
    public void BuildPlan_OrdersRunOnceThenRunThenFolders_MachineBeforeUser()
    {
        var plan = Plan(
        [
            E("userFolder", @"C:\u\a.lnk", StartupSource.FolderUser),
            E("runUser", "u.exe", StartupSource.RunUser),
            E("onceUser", "ou.exe", StartupSource.RunOnceUser),
            E("commonFolder", @"C:\c\b.lnk", StartupSource.FolderCommon),
            E("runMachine", "m.exe", StartupSource.RunMachine),
            E("onceMachine", "om.exe", StartupSource.RunOnceMachine),
        ]);

        Assert.Equal(["onceMachine", "onceUser", "runMachine", "runUser", "commonFolder", "userFolder"], Names(plan));
    }

    [Fact]
    public void BuildPlan_KeepsInputOrderWithinASource()
    {
        var plan = Plan(
        [
            E("b", "b.exe", StartupSource.RunUser),
            E("a", "a.exe", StartupSource.RunUser),
            E("c", "c.exe", StartupSource.RunUser),
        ]);

        Assert.Equal(["b", "a", "c"], Names(plan));
    }

    [Fact]
    public void BuildPlan_EmptyInput_GivesAnEmptyPlan()
    {
        var plan = Plan([]);

        Assert.Empty(plan.Steps);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void BuildPlan_PlainRunOnce_IsDeletedBeforeRun()
    {
        var step = Plan([E("setup", "setup.exe", StartupSource.RunOnceUser)]).Steps.Single();

        Assert.True(step.DeleteBeforeRun);
        Assert.False(step.DeleteAfterSuccess);
    }

    [Fact]
    public void BuildPlan_BangPrefixedRunOnce_IsDeletedAfterSuccess()
    {
        var step = Plan([E("!setup", "setup.exe", StartupSource.RunOnceMachine)]).Steps.Single();

        Assert.False(step.DeleteBeforeRun);
        Assert.True(step.DeleteAfterSuccess);
        Assert.Equal("!setup", step.Entry.Name);
    }

    [Fact]
    public void BuildPlan_RunEntries_AreNeverDeleted()
    {
        var steps = Plan([E("a", "a.exe", StartupSource.RunUser), E("b", @"C:\b.lnk", StartupSource.FolderUser)]).Steps;

        Assert.All(steps, s => Assert.False(s.DeleteBeforeRun || s.DeleteAfterSuccess));
    }

    [Fact]
    public void BuildPlan_BangPrefixOnARunEntry_IsNotRunOnceSemantics()
    {
        var step = Plan([E("!x", "x.exe", StartupSource.RunUser)]).Steps.Single();

        Assert.False(step.DeleteAfterSuccess);
    }

    [Fact]
    public void BuildPlan_SafeMode_RunsOnlyStarPrefixedRunOnce()
    {
        var plan = Plan(
        [
            E("*fix", "fix.exe", StartupSource.RunOnceMachine),
            E("!*both", "both.exe", StartupSource.RunOnceUser),
            E("*!both2", "both2.exe", StartupSource.RunOnceUser),
            E("plainOnce", "p.exe", StartupSource.RunOnceUser),
            E("run", "r.exe", StartupSource.RunMachine),
            E("folder", @"C:\f.lnk", StartupSource.FolderUser),
        ],
        safeMode: true);

        Assert.Equal(["*fix", "!*both", "*!both2"], Names(plan));
        Assert.Equal(
            [StartupSkipReason.SafeMode, StartupSkipReason.SafeMode, StartupSkipReason.SafeMode],
            plan.Skipped.Select(s => s.Reason));
    }

    [Fact]
    public void BuildPlan_NotSafeMode_StarPrefixedRunOnceRunsLikeAnyOther()
    {
        var step = Plan([E("*fix", "fix.exe", StartupSource.RunOnceUser)]).Steps.Single();

        Assert.True(step.DeleteBeforeRun);
    }

    [Fact]
    public void BuildPlan_SkipsWinGnomesOwnEntry_CaseInsensitively()
    {
        var plan = Plan([E("wingnome", @"C:\WinGnome.exe", StartupSource.RunUser), E("Other", "o.exe", StartupSource.RunUser)]);

        Assert.Equal(["Other"], Names(plan));
        Assert.Equal(StartupSkipReason.OwnEntry, plan.Skipped.Single().Reason);
    }

    [Fact]
    public void BuildPlan_SkipsOwnEntryEvenWithRunOncePrefix()
    {
        var plan = Plan([E("!WinGnome", "w.exe", StartupSource.RunOnceUser)]);

        Assert.Empty(plan.Steps);
        Assert.Equal(StartupSkipReason.OwnEntry, plan.Skipped.Single().Reason);
    }

    [Theory]
    [InlineData(0x03)]
    [InlineData(0x07)]
    public void BuildPlan_DisabledInStartupApproved_IsSkipped(byte flag)
    {
        var approved = new StartupApprovedSet().Add(StartupSource.RunUser, "Chat", [flag, 0, 0, 0]);

        var plan = Plan([E("chat", "chat.exe", StartupSource.RunUser)], approved);

        Assert.Empty(plan.Steps);
        Assert.Equal(StartupSkipReason.Disabled, plan.Skipped.Single().Reason);
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x06)]
    public void BuildPlan_EnabledInStartupApproved_Runs(byte flag)
    {
        var approved = new StartupApprovedSet().Add(StartupSource.RunUser, "chat", [flag]);

        Assert.Equal(["chat"], Names(Plan([E("chat", "chat.exe", StartupSource.RunUser)], approved)));
    }

    [Fact]
    public void BuildPlan_ApprovalIsPerSource()
    {
        var approved = new StartupApprovedSet().Add(StartupSource.RunMachine, "chat", [0x03]);

        var plan = Plan([E("chat", "a.exe", StartupSource.RunUser), E("chat", "b.exe", StartupSource.RunMachine)], approved);

        Assert.Equal("a.exe", plan.Steps.Single().Command.Executable);
    }

    [Fact]
    public void BuildPlan_DisabledStartupFolderItem_IsSkipped()
    {
        var approved = new StartupApprovedSet().Add(StartupSource.FolderUser, "App.lnk", [0x03]);

        var plan = Plan([E("App.lnk", @"C:\Start Menu\Startup\App.lnk", StartupSource.FolderUser)], approved);

        Assert.Equal(StartupSkipReason.Disabled, plan.Skipped.Single().Reason);
    }

    [Fact]
    public void BuildPlan_RunOnceIgnoresStartupApproved()
    {
        var approved = new StartupApprovedSet().Add(StartupSource.RunOnceUser, "once", [0x03]);

        Assert.Equal(["once"], Names(Plan([E("once", "o.exe", StartupSource.RunOnceUser)], approved)));
    }

    [Fact]
    public void BuildPlan_FolderItemPathWithSpaces_IsTheWholeExecutable()
    {
        var step = Plan([E("My App.lnk", @"C:\Users\Sam H\Start Menu\Startup\My App.lnk", StartupSource.FolderUser)]).Steps.Single();

        Assert.Equal(new ParsedCommand(@"C:\Users\Sam H\Start Menu\Startup\My App.lnk", "", false), step.Command);
    }

    [Fact]
    public void BuildPlan_SkipsDesktopIniInFolders()
    {
        var plan = Plan([E("desktop.ini", @"C:\Startup\desktop.ini", StartupSource.FolderCommon)]);

        Assert.Empty(plan.Steps);
        Assert.Equal(StartupSkipReason.FolderMetadata, plan.Skipped.Single().Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildPlan_EmptyCommand_IsSkipped(string command)
    {
        var plan = Plan([E("x", command, StartupSource.RunUser)]);

        Assert.Equal(StartupSkipReason.EmptyCommand, plan.Skipped.Single().Reason);
    }

    [Theory]
    [InlineData("\"C:\\unterminated\\a.exe")]
    [InlineData("\"\" arg")]
    public void BuildPlan_MalformedQuoting_IsSkipped(string command)
    {
        var plan = Plan([E("x", command, StartupSource.RunUser), E("ok", "ok.exe", StartupSource.RunUser)]);

        Assert.Equal(["ok"], Names(plan));
        Assert.Equal(StartupSkipReason.MalformedCommand, plan.Skipped.Single().Reason);
    }

    [Fact]
    public void BuildPlan_SkippedKeepsTheEntry()
    {
        var entry = E("WinGnome", "w.exe", StartupSource.RunUser);

        Assert.Equal(entry, Plan([entry]).Skipped.Single().Entry);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --silent /x", @"C:\Program Files\App\app.exe", "--silent /x")]
    [InlineData("\"C:\\App\\app.exe\"", @"C:\App\app.exe", "")]
    [InlineData("\"C:\\App\\app.exe\"--flag", @"C:\App\app.exe", "--flag")]
    [InlineData(@"C:\App\app.exe -minimized", @"C:\App\app.exe", "-minimized")]
    [InlineData(@"C:\App\app.exe", @"C:\App\app.exe", "")]
    [InlineData(@"C:\Program Files\App\app.exe /q", @"C:\Program Files\App\app.exe", "/q")]
    [InlineData(@"C:\Program Files\App\APP.EXE", @"C:\Program Files\App\APP.EXE", "")]
    [InlineData(@"C:\Program Files\App\run.cmd go now", @"C:\Program Files\App\run.cmd", "go now")]
    [InlineData(@"rundll32.exe C:\x\y.dll,Entry arg", "rundll32.exe", @"C:\x\y.dll,Entry arg")]
    [InlineData(@"C:\App\app.exe.config.exe /a", @"C:\App\app.exe.config.exe", "/a")]
    [InlineData("tool /flag value", "tool", "/flag value")]
    [InlineData(@"C:\x\run.cmd tool.exe", @"C:\x\run.cmd", "tool.exe")]
    [InlineData("  \t C:\\a.exe   x  ", @"C:\a.exe", "x")]
    public void TryParseCommand_SplitsExecutableAndArguments(string line, string exe, string args)
    {
        Assert.True(StartupRunner.TryParseCommand(line, wholeLineIsPath: false, out var command));

        Assert.Equal(new ParsedCommand(exe, args, false), command);
    }

    [Theory]
    [InlineData("\"%ProgramFiles%\\App\\app.exe\" -x")]
    [InlineData(@"C:\app.exe %APPDATA%\cfg")]
    [InlineData("%windir%\\system32\\cmd.exe /c")]
    public void TryParseCommand_FlagsEnvironmentReferencesWithoutExpandingThem(string line)
    {
        Assert.True(StartupRunner.TryParseCommand(line, false, out var command));

        Assert.True(command.NeedsEnvironmentExpansion);
        Assert.Contains("%", command.Executable + command.Arguments);
    }

    [Theory]
    [InlineData(@"C:\app.exe 100% done")]
    [InlineData(@"C:\app.exe")]
    [InlineData("C:\\app.exe %not closed")]
    [InlineData(@"C:\app.exe 100% done, 50% more")]
    public void TryParseCommand_PlainPercentSigns_AreNotEnvironmentReferences(string line)
    {
        Assert.True(StartupRunner.TryParseCommand(line, false, out var command));

        Assert.False(command.NeedsEnvironmentExpansion);
    }

    [Fact]
    public void TryParseCommand_EnvironmentVariableKeepsItsText()
    {
        StartupRunner.TryParseCommand("%windir%\\notepad.exe a.txt", false, out var command);

        Assert.Equal(new ParsedCommand("%windir%\\notepad.exe", "a.txt", true), command);
    }

    [Fact]
    public void TryParseCommand_FolderPath_IsNeverSplit()
    {
        StartupRunner.TryParseCommand(@"C:\Start Menu\a b.exe c", wholeLineIsPath: true, out var command);

        Assert.Equal(new ParsedCommand(@"C:\Start Menu\a b.exe c", "", false), command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"nope")]
    [InlineData("\"\"")]
    public void TryParseCommand_Malformed_ReturnsFalse(string line)
    {
        Assert.False(StartupRunner.TryParseCommand(line, false, out _));
    }

    [Theory]
    [InlineData(new byte[] { 0x02 }, StartupApproval.Enabled)]
    [InlineData(new byte[] { 0x06, 1, 2, 3 }, StartupApproval.Enabled)]
    [InlineData(new byte[] { 0x03 }, StartupApproval.Disabled)]
    [InlineData(new byte[] { 0x07, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 }, StartupApproval.Disabled)]
    [InlineData(new byte[] { 0x00 }, StartupApproval.Enabled)]
    [InlineData(new byte[] { 0x04 }, StartupApproval.Enabled)]
    [InlineData(new byte[] { 0xFF }, StartupApproval.Enabled)]
    public void Parse_ReadsTheFirstByte(byte[] blob, StartupApproval expected)
    {
        Assert.Equal(expected, StartupApprovedSet.Parse(blob));
    }

    [Fact]
    public void Parse_EmptyBlob_IsEnabled()
    {
        Assert.Equal(StartupApproval.Enabled, StartupApprovedSet.Parse([]));
    }

    [Fact]
    public void Parse_Null_IsEnabled()
    {
        Assert.Equal(StartupApproval.Enabled, StartupApprovedSet.Parse(null));
    }

    [Fact]
    public void Get_UnknownName_IsEnabled()
    {
        Assert.Equal(StartupApproval.Enabled, new StartupApprovedSet().Get(StartupSource.RunUser, "nothing"));
    }
}
