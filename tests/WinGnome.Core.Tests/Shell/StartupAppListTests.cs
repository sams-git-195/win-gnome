using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class StartupAppListTests
{
    private const string Own = "WinGnome";
    private static readonly byte[] Disabled = [0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];

    private static StartupEntry E(string name, StartupSource source, string command = "cmd") => new(name, command, source);

    private static IReadOnlyList<StartupAppRow> Build(StartupEntry[] entries, StartupApprovedSet? approvals = null) =>
        StartupAppList.Build(entries, approvals ?? new StartupApprovedSet(), Own);

    [Fact]
    public void Build_Empty_GivesEmpty() =>
        Assert.Empty(Build([]));

    [Fact]
    public void Build_UserRunValue_IsEditableNotRemovable()
    {
        var entry = E("Steam", StartupSource.RunUser, @"""C:\Steam\steam.exe"" -silent");

        Assert.Equal([new StartupAppRow(entry, "Steam", Enabled: true, Editable: true, Removable: false)], Build([entry]));
    }

    [Fact]
    public void Build_UserFolderShortcut_IsEditableAndRemovable_NamedWithoutExtension()
    {
        var entry = E("Ollama.lnk", StartupSource.FolderUser, @"C:\Users\me\Startup\Ollama.lnk");

        Assert.Equal([new StartupAppRow(entry, "Ollama", Enabled: true, Editable: true, Removable: true)], Build([entry]));
    }

    [Theory]
    [InlineData(StartupSource.RunMachine)]
    [InlineData(StartupSource.RunMachine32)]
    [InlineData(StartupSource.FolderCommon)]
    public void Build_MachineWideItems_AreReadOnly(StartupSource source)
    {
        var row = Assert.Single(Build([E("Item", source)]));

        Assert.False(row.Editable);
        Assert.False(row.Removable);
        Assert.True(row.IsMachineWide);
    }

    [Theory]
    [InlineData(StartupSource.RunUser)]
    [InlineData(StartupSource.FolderUser)]
    public void IsMachineWide_UserItems_IsFalse(StartupSource source) =>
        Assert.False(Assert.Single(Build([E("Item", source)])).IsMachineWide);

    [Theory]
    [InlineData(StartupSource.RunOnceUser)]
    [InlineData(StartupSource.RunOnceMachine)]
    public void Build_RunOnce_IsNotListed(StartupSource source) =>
        Assert.Empty(Build([E("Setup", source)]));

    [Fact]
    public void Build_OwnUserRunValue_IsHiddenIgnoringCase() =>
        Assert.Empty(Build([E("wingnome", StartupSource.RunUser)]));

    [Theory]
    [InlineData(StartupSource.RunMachine, "WinGnome")]
    [InlineData(StartupSource.FolderUser, "WinGnome")]
    public void Build_OwnNameElsewhere_IsListed(StartupSource source, string name) =>
        Assert.Single(Build([E(name, source)]));

    [Theory]
    [InlineData(StartupSource.FolderUser)]
    [InlineData(StartupSource.FolderCommon)]
    public void Build_FolderDesktopIni_IsHidden(StartupSource source) =>
        Assert.Empty(Build([E("Desktop.ini", source)]));

    [Fact]
    public void Build_DesktopIniRunValue_IsListed() =>
        Assert.Single(Build([E("desktop.ini", StartupSource.RunUser)]));

    [Fact]
    public void Build_DisabledApproval_ShowsOff()
    {
        var approvals = new StartupApprovedSet().Add(StartupSource.RunUser, "steam", Disabled);

        Assert.False(Assert.Single(Build([E("Steam", StartupSource.RunUser)], approvals)).Enabled);
    }

    [Fact]
    public void Build_ApprovalForAnotherSource_DoesNotApply()
    {
        var approvals = new StartupApprovedSet().Add(StartupSource.RunMachine, "Steam", Disabled);

        Assert.True(Assert.Single(Build([E("Steam", StartupSource.RunUser)], approvals)).Enabled);
    }

    [Fact]
    public void Build_SortsByDisplayNameIgnoringCase_UserBeforeMachineOnATie() =>
        Assert.Equal(
            [("alpha", StartupSource.FolderUser), ("Beta", StartupSource.RunUser), ("beta", StartupSource.RunMachine), ("Gamma", StartupSource.RunMachine32)],
            Build(
            [
                E("Gamma", StartupSource.RunMachine32),
                E("beta", StartupSource.RunMachine),
                E("alpha.lnk", StartupSource.FolderUser),
                E("Beta", StartupSource.RunUser),
            ]).Select(r => (r.DisplayName, r.Entry.Source)));

    [Theory]
    [InlineData(".lnk", ".lnk")]
    [InlineData("App.exe", "App")]
    [InlineData("No extension", "No extension")]
    public void Build_FolderDisplayName_DropsOnlyTheExtension(string fileName, string expected) =>
        Assert.Equal(expected, Assert.Single(Build([E(fileName, StartupSource.FolderUser)])).DisplayName);

    [Theory]
    [InlineData(StartupSource.RunUser, false, "Run")]
    [InlineData(StartupSource.FolderUser, false, "StartupFolder")]
    [InlineData(StartupSource.RunMachine, true, "Run")]
    [InlineData(StartupSource.RunMachine32, true, "Run32")]
    [InlineData(StartupSource.FolderCommon, true, "StartupFolder")]
    public void ApprovalLocation_MapsEachSource(StartupSource source, bool machine, string key) =>
        Assert.Equal(new StartupApprovalLocation(machine, key), StartupAppList.ApprovalLocation(source));

    [Theory]
    [InlineData(StartupSource.RunOnceUser)]
    [InlineData(StartupSource.RunOnceMachine)]
    public void ApprovalLocation_RunOnce_IsNull(StartupSource source) =>
        Assert.Null(StartupAppList.ApprovalLocation(source));
}
