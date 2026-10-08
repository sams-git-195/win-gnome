using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class TrayIconRegistryTests
{
    private static NotifyIconCommand Command(
        NotifyIconMessage message, nint owner = 1, uint id = 1, NotifyIconFields flags = NotifyIconFields.None,
        uint callback = 0, nint icon = 0, string tip = "", NotifyIconStates state = NotifyIconStates.None,
        NotifyIconStates stateMask = NotifyIconStates.None, uint version = 0, Guid guid = default) =>
        new(message, owner, id, flags, callback, icon, tip, state, stateMask, version, guid);

    private static NotifyIconCommand Add(nint owner, uint id, string tip = "") =>
        Command(NotifyIconMessage.Add, owner, id, NotifyIconFields.Message | NotifyIconFields.Icon | NotifyIconFields.Tip,
            callback: 0x8000, icon: 99, tip: tip);

    [Fact]
    public void KeepsInsertionOrder()
    {
        var registry = new TrayIconRegistry();

        registry.Apply(Add(1, 1, "a"), true);
        registry.Apply(Add(2, 1, "b"), true);
        var third = registry.Apply(Add(1, 2, "c"), true);

        Assert.Equal(["a", "b", "c"], registry.Icons.Select(i => i.Tip));
        Assert.Equal(new TrayChange(TrayChangeKind.Added, 2, registry.Icons[2], true), third);
    }

    [Fact]
    public void DuplicateAdd_UpdatesInPlace()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Add(1, 1, "old"), true);
        registry.Apply(Add(2, 2), true);

        var change = registry.Apply(Add(1, 1, "new"), false);

        Assert.Equal(TrayChangeKind.Updated, change.Kind);
        Assert.Equal(0, change.Index);
        Assert.True(change.Accepted);
        Assert.Equal("new", registry.Icons[0].Tip);
        Assert.Equal(2, registry.Icons.Count);
    }

    [Fact]
    public void Modify_OnlyChangesFlaggedFields()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Add(1, 1, "tip"), true);

        registry.Apply(Command(NotifyIconMessage.Modify, 1, 1, NotifyIconFields.Icon, callback: 0x9999, icon: 0, tip: "ignored"), false);

        var icon = registry.Icons[0];
        Assert.Equal("tip", icon.Tip);
        Assert.Equal(0x8000u, icon.CallbackMessage);
        Assert.False(icon.HasIcon);
        Assert.False(icon.IsVisible);
    }

    [Fact]
    public void Modify_UnknownIcon_RejectedUnlessShellKnowsIt()
    {
        var registry = new TrayIconRegistry();
        var modify = Command(NotifyIconMessage.Modify, 5, 5, NotifyIconFields.Icon | NotifyIconFields.Message, callback: 0x8001, icon: 7);

        Assert.Equal(TrayChange.Rejected, registry.Apply(modify, knownToShell: false));
        Assert.Empty(registry.Icons);

        var change = registry.Apply(modify, knownToShell: true);

        Assert.Equal(TrayChangeKind.Added, change.Kind);
        Assert.True(registry.Icons[0].IsVisible);
    }

    [Fact]
    public void Delete_RemovesAndReportsIndex()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Add(1, 1), true);
        registry.Apply(Add(2, 2), true);

        var change = registry.Apply(Command(NotifyIconMessage.Delete, 1, 1), true);

        Assert.Equal(TrayChangeKind.Removed, change.Kind);
        Assert.Equal(0, change.Index);
        Assert.Single(registry.Icons);
        Assert.False(registry.Apply(Command(NotifyIconMessage.Delete, 1, 1), true).Accepted);
    }

    [Fact]
    public void GuidIdentifiesIconAcrossOwnerChanges()
    {
        var guid = Guid.NewGuid();
        var registry = new TrayIconRegistry();
        registry.Apply(Command(NotifyIconMessage.Add, 1, 1, NotifyIconFields.ItemGuid | NotifyIconFields.Icon, icon: 3, guid: guid), true);

        var change = registry.Apply(Command(NotifyIconMessage.Modify, 9, 4, NotifyIconFields.ItemGuid | NotifyIconFields.Tip, tip: "moved", guid: guid), false);

        Assert.Equal(TrayChangeKind.Updated, change.Kind);
        Assert.Equal((nint)9, registry.Icons[0].Id.Owner);
        Assert.Equal("moved", registry.Icons[0].Tip);
    }

    [Fact]
    public void HiddenStateFollowsMask()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Add(1, 1), true);

        registry.Apply(Command(NotifyIconMessage.Modify, 1, 1, NotifyIconFields.State, state: NotifyIconStates.Hidden, stateMask: NotifyIconStates.Hidden), true);
        Assert.False(registry.Icons[0].IsVisible);

        // State bits outside the mask are ignored.
        registry.Apply(Command(NotifyIconMessage.Modify, 1, 1, NotifyIconFields.State, state: NotifyIconStates.None, stateMask: NotifyIconStates.SharedIcon), true);
        Assert.True(registry.Icons[0].IsHidden);

        registry.Apply(Command(NotifyIconMessage.Modify, 1, 1, NotifyIconFields.State, state: NotifyIconStates.None, stateMask: NotifyIconStates.Hidden), true);
        Assert.True(registry.Icons[0].IsVisible);
    }

    [Fact]
    public void SetVersion_ChangesVersionOfKnownIcon()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Add(1, 1), true);

        var change = registry.Apply(Command(NotifyIconMessage.SetVersion, 1, 1, version: 4), true);

        Assert.True(change.Accepted);
        Assert.Equal(4u, registry.Icons[0].Version);
        Assert.False(registry.Apply(Command(NotifyIconMessage.SetVersion, 3, 3, version: 4), true).Accepted);
    }

    [Fact]
    public void RejectsIconWithoutOwner()
    {
        Assert.Equal(TrayChange.Rejected, new TrayIconRegistry().Apply(Add(0, 1), true));
    }

    [Fact]
    public void ToolTip_StandardForLegacyOrShowTip()
    {
        var legacy = TrayIconState.Create(Add(1, 1, "tip"));
        var v4 = legacy with { Version = 4 };

        Assert.True(legacy.ShowsToolTip);
        Assert.False(v4.ShowsToolTip);
        Assert.True((v4 with { ShowTip = true }).ShowsToolTip);
        Assert.False((legacy with { Tip = "" }).ShowsToolTip);
    }

    [Fact]
    public void RemoveWhere_ReportsDescendingIndices()
    {
        var registry = new TrayIconRegistry();
        for (var i = 1; i <= 4; i++)
        {
            registry.Apply(Add(i, 1), true);
        }

        var changes = registry.RemoveWhere(icon => icon.Id.Owner % 2 == 1);

        Assert.Equal([2, 0], changes.Select(c => c.Index));
        Assert.Equal([(nint)2, 4], registry.Icons.Select(i => i.Id.Owner));
    }
}
