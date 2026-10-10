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
    public void Modify_UnknownIcon_WithoutMessageFlag_CreatesEntryWithNoCallback()
    {
        var registry = new TrayIconRegistry();

        // The tooltip-only modify of an app whose NIM_ADD reached Explorer alone (front gap, KI-019): apps reuse
        // their NOTIFYICONDATA, so uCallbackMessage can be set while NIF_MESSAGE is not flagged. The adopted
        // entry stays click-dead (no callback, no version) until the app re-registers.
        var change = registry.Apply(
            Command(NotifyIconMessage.Modify, 7, 100, NotifyIconFields.Tip, callback: 0x8000, tip: "Windows Security"),
            knownToShell: true);

        Assert.Equal(TrayChangeKind.Added, change.Kind);
        Assert.True(change.CreatedViaModify);
        Assert.Equal(0u, registry.Icons[0].CallbackMessage);
        Assert.Equal(0u, registry.Icons[0].Version);
    }

    [Fact]
    public void DuplicateAdd_HealsMissingCallbackAndVersion()
    {
        var guid = Guid.NewGuid();
        var registry = new TrayIconRegistry();
        registry.Apply(Command(NotifyIconMessage.Modify, 1, 100, NotifyIconFields.ItemGuid | NotifyIconFields.Tip, tip: "old", guid: guid), knownToShell: true);

        // The app answers the heal broadcast the way it answered at start-up: a full ADD (here from a new owner
        // window, identified by its GUID), then NIM_SETVERSION. Both land on the adopted entry.
        var add = registry.Apply(Command(NotifyIconMessage.Add, 2, 7,
            NotifyIconFields.ItemGuid | NotifyIconFields.Message | NotifyIconFields.Icon | NotifyIconFields.Tip,
            callback: 0x9000, icon: 5, tip: "new", guid: guid), false);
        registry.Apply(Command(NotifyIconMessage.SetVersion, 2, 7, NotifyIconFields.ItemGuid, version: 4, guid: guid), false);

        Assert.Equal(TrayChangeKind.Updated, add.Kind);
        Assert.False(add.CreatedViaModify);
        Assert.Single(registry.Icons);
        Assert.Equal(0x9000u, registry.Icons[0].CallbackMessage);
        Assert.Equal(4u, registry.Icons[0].Version);
        Assert.Equal("new", registry.Icons[0].Tip);
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

    // --- Callback learning from unflagged updates (spec 0022 addendum, KI-105) ---
    //
    // Apps reuse one NOTIFYICONDATA struct for every Shell_NotifyIcon call, so a tooltip-only NIM_MODIFY
    // carries the real uCallbackMessage/uVersion even without NIF_MESSAGE. One observation may be junk;
    // the same plausible pair twice is adopted.

    /// <summary>An entry with no callback, as left behind by a front-gap registration (KI-019).</summary>
    private static TrayIconRegistry CallbackLessRegistry(nint owner = 1, uint id = 100)
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Command(NotifyIconMessage.Add, owner, id, NotifyIconFields.Icon | NotifyIconFields.Tip, icon: 5, tip: "Security"), true);
        return registry;
    }

    /// <summary>The shape apps answer a TaskbarCreated broadcast with: a tooltip-only modify, raw fields riding along.</summary>
    private static NotifyIconCommand TooltipModify(
        uint callback, uint version = 4, string tip = "t", nint owner = 1, uint id = 100, NotifyIconFields extra = NotifyIconFields.None) =>
        Command(NotifyIconMessage.Modify, owner, id, NotifyIconFields.Tip | extra, callback: callback, tip: tip, version: version);

    [Fact]
    public void Learning_FirstUnflaggedObservation_StoresCandidateWithoutAdopting()
    {
        var registry = CallbackLessRegistry();

        var change = registry.Apply(TooltipModify(0x8001), true);

        // A single unflagged value could be an uninitialised-struct leftover: it is only a candidate.
        Assert.False(change.LearnedCallback);
        Assert.Equal((0x8001u, 4u), change.ObservedCallback);
        Assert.Equal(0u, registry.Icons[0].CallbackMessage);
        Assert.Equal(0u, registry.Icons[0].Version);
    }

    [Fact]
    public void Learning_SecondMatchingObservation_AdoptsCallbackAndVersion()
    {
        var registry = CallbackLessRegistry();
        registry.Apply(TooltipModify(0x8001, tip: "first"), true);

        var change = registry.Apply(TooltipModify(0x8001, tip: "second"), true);

        Assert.True(change.LearnedCallback);
        Assert.Null(change.ObservedCallback);
        Assert.Equal(0x8001u, registry.Icons[0].CallbackMessage);
        Assert.Equal(4u, registry.Icons[0].Version);
        Assert.Equal(0x8001u, change.Icon!.CallbackMessage);
        Assert.Equal(4u, change.Icon.Version);
    }

    [Fact]
    public void Learning_DifferingObservation_ReplacesTheCandidate()
    {
        var registry = CallbackLessRegistry();
        registry.Apply(TooltipModify(0x8001), true);

        var replaced = registry.Apply(TooltipModify(0x8002), true);
        var confirmed = registry.Apply(TooltipModify(0x8002), true);

        Assert.False(replaced.LearnedCallback);
        Assert.Equal((0x8002u, 4u), replaced.ObservedCallback);
        Assert.True(confirmed.LearnedCallback);
        Assert.Equal(0x8002u, registry.Icons[0].CallbackMessage);
    }

    [Theory]
    // Callback gate: 0 (raw field never populated), below WM_USER (window messages like WM_CLOSE 0x0010
    // must never be adopted), and the RegisterWindowMessage range from 0xC000 up.
    [InlineData(0x0000u, 4u)]
    [InlineData(0x0010u, 4u)]
    [InlineData(0x03FFu, 4u)]
    [InlineData(0xC000u, 4u)]
    [InlineData(0xFFFFFFFFu, 4u)]
    // Version gate: anything but {0, 3, 4} — e.g. balloon uTimeout values sharing the union field.
    [InlineData(0x8001u, 5u)]
    [InlineData(0x8001u, 10u)]
    [InlineData(0x8001u, 30u)]
    public void Learning_RejectsImplausibleValues(uint callback, uint version)
    {
        var registry = CallbackLessRegistry();

        var first = registry.Apply(TooltipModify(callback, version), true);
        var second = registry.Apply(TooltipModify(callback, version), true);

        Assert.Null(first.ObservedCallback);
        Assert.False(second.LearnedCallback);
        Assert.Equal(0u, registry.Icons[0].CallbackMessage);
        Assert.Equal(0u, registry.Icons[0].Version);
    }

    [Theory]
    // The gate boundaries themselves, and legacy version 0 in a confirmed pair (rule 5).
    [InlineData(0x0400u, 0u)] // WM_USER, legacy encoding
    [InlineData(0x8001u, 3u)]
    [InlineData(0xBFFFu, 4u)] // top of WM_APP
    public void Learning_AdoptsPlausibleBoundaries(uint callback, uint version)
    {
        var registry = CallbackLessRegistry();
        registry.Apply(TooltipModify(callback, version), true);

        var change = registry.Apply(TooltipModify(callback, version), true);

        Assert.True(change.LearnedCallback);
        Assert.Equal(callback, registry.Icons[0].CallbackMessage);
        Assert.Equal(version, registry.Icons[0].Version);
    }

    [Fact]
    public void Learning_FlaggedMessage_ClearsTheCandidateAndWins()
    {
        var registry = CallbackLessRegistry();
        registry.Apply(TooltipModify(0x8001), true); // candidate (0x8001, 4) pending

        var flagged = registry.Apply(Command(NotifyIconMessage.Modify, 1, 100,
            NotifyIconFields.Message | NotifyIconFields.Tip, callback: 0x9000, tip: "t"), true);
        Assert.Equal(0x9000u, registry.Icons[0].CallbackMessage);

        // The app drops its callback again (NIF_MESSAGE with 0), so the entry is click-dead once more —
        // but the old candidate must be gone: the pair that was pending before the flagged modify is now
        // a plain first observation, not a confirmation.
        registry.Apply(Command(NotifyIconMessage.Modify, 1, 100, NotifyIconFields.Message, callback: 0), true);
        var after = registry.Apply(TooltipModify(0x8001), true);

        Assert.False(after.LearnedCallback);
        Assert.Equal((0x8001u, 4u), after.ObservedCallback);
        Assert.Equal(0u, registry.Icons[0].CallbackMessage);
    }

    [Fact]
    public void Learning_SetVersion_ClearsTheCandidate()
    {
        var registry = CallbackLessRegistry();
        registry.Apply(TooltipModify(0x8001), true); // candidate (0x8001, 4) pending

        registry.Apply(Command(NotifyIconMessage.SetVersion, 1, 100, version: 3), true);
        var after = registry.Apply(TooltipModify(0x8001), true);

        // The authoritative SETVERSION dropped the candidate, so this is a first observation again.
        Assert.False(after.LearnedCallback);
        Assert.Equal((0x8001u, 4u), after.ObservedCallback);
    }

    [Fact]
    public void Learning_FlaggedAdd_ClearsTheCandidate()
    {
        var registry = CallbackLessRegistry();
        registry.Apply(TooltipModify(0x8001), true); // candidate (0x8001, 4) pending

        // The app re-registers properly (the heal's effect): the flagged ADD is authoritative.
        var add = registry.Apply(Add(1, 100, "tip"), true);
        Assert.False(add.LearnedCallback);
        Assert.Equal(0x8000u, registry.Icons[0].CallbackMessage);

        // Same trick as the flagged-modify test: drop back to no callback and check the old candidate
        // is gone — the pair that was pending must be a first observation again, not a confirmation.
        registry.Apply(Command(NotifyIconMessage.Modify, 1, 100, NotifyIconFields.Message, callback: 0), true);
        var after = registry.Apply(TooltipModify(0x8001), true);

        Assert.False(after.LearnedCallback);
        Assert.Equal((0x8001u, 4u), after.ObservedCallback);
    }

    [Fact]
    public void Learning_NeverOverwritesARealCallback()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Add(1, 100, "tip"), true); // authoritative callback 0x8000 via NIF_MESSAGE

        var first = registry.Apply(TooltipModify(0x8001, id: 100), true);
        var second = registry.Apply(TooltipModify(0x8001, id: 100), true);

        Assert.Null(first.ObservedCallback);
        Assert.False(second.LearnedCallback);
        Assert.Equal(0x8000u, registry.Icons[0].CallbackMessage);
        Assert.Equal(0u, registry.Icons[0].Version);
    }

    [Fact]
    public void Learning_RemoveClearsTheCandidate()
    {
        var registry = new TrayIconRegistry();
        registry.Apply(Command(NotifyIconMessage.Add, 1, 100, NotifyIconFields.Tip, tip: "a"), true);
        registry.Apply(Command(NotifyIconMessage.Add, 2, 100, NotifyIconFields.Tip, tip: "b"), true);
        registry.Apply(TooltipModify(0x8001), true);                        // candidate for owner 1
        registry.Apply(TooltipModify(0x8001, owner: 2), true);             // candidate for owner 2

        registry.Apply(Command(NotifyIconMessage.Delete, 1, 100), true);   // NIM_DELETE clears
        registry.RemoveWhere(icon => icon.Id.Owner == 2);                  // owner-gone sweep clears too

        registry.Apply(Command(NotifyIconMessage.Add, 1, 100, NotifyIconFields.Tip, tip: "a2"), true);
        registry.Apply(Command(NotifyIconMessage.Add, 2, 100, NotifyIconFields.Tip, tip: "b2"), true);

        // Had the candidates survived the removals, these first observations would adopt immediately.
        var firstA = registry.Apply(TooltipModify(0x8001), true);
        var firstB = registry.Apply(TooltipModify(0x8001, owner: 2), true);

        Assert.False(firstA.LearnedCallback);
        Assert.Equal((0x8001u, 4u), firstA.ObservedCallback);
        Assert.False(firstB.LearnedCallback);
        Assert.Equal((0x8001u, 4u), firstB.ObservedCallback);
    }

    [Fact]
    public void Learning_AdoptionCreatesOnTheKnownToShellModifyPath()
    {
        // The field shape (spec 0022 addendum): SecurityHealthSystray's ADD reached Explorer alone; it
        // answers every TaskbarCreated broadcast — the host's startup one and its +2 s heal — with a
        // tooltip-only modify whose raw fields carry the real callback. It never re-registers with
        // NIF_MESSAGE, so learning is the only repair.
        var registry = new TrayIconRegistry();

        var created = registry.Apply(
            Command(NotifyIconMessage.Modify, 0x10378, 100, NotifyIconFields.Tip, callback: 0x8001, tip: "Windows Security", version: 4),
            knownToShell: true);
        Assert.Equal(TrayChangeKind.Added, created.Kind);
        Assert.True(created.CreatedViaModify);
        Assert.Equal((0x8001u, 4u), created.ObservedCallback);
        Assert.False(created.LearnedCallback);
        Assert.Equal(0u, registry.Icons[0].CallbackMessage);

        var healed = registry.Apply(
            Command(NotifyIconMessage.Modify, 0x10378, 100, NotifyIconFields.Tip, callback: 0x8001, tip: "Windows Security - No actions needed.", version: 4),
            knownToShell: true);

        Assert.Equal(TrayChangeKind.Updated, healed.Kind);
        Assert.True(healed.LearnedCallback);
        Assert.Equal(0x8001u, registry.Icons[0].CallbackMessage);
        Assert.Equal(4u, registry.Icons[0].Version);
        Assert.Single(registry.Icons);
    }
}
