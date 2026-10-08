using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class TweakEngineTests
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ClassicKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string ClassicParent = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private static readonly TweakDefinition DarkMode = TweakCatalog.Find("dark-mode")!;
    private static readonly TweakDefinition Classic = TweakCatalog.Find("classic-context-menu")!;

    private readonly InMemoryRegistryStore _store = new();
    private readonly TweakBackup _backup = new();
    private readonly TweakEngine _engine;

    public TweakEngineTests()
    {
        _engine = new TweakEngine(_store, _backup);
    }

    [Fact]
    public void Constructor_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new TweakEngine(null!, new TweakBackup()));
        Assert.Throws<ArgumentNullException>(() => new TweakEngine(new InMemoryRegistryStore(), null!));
    }

    [Fact]
    public void Backup_ExposesTheInstanceItWasGiven()
    {
        Assert.Same(_backup, _engine.Backup);
    }

    [Fact]
    public void IsApplied_FalseOnAFreshStore()
    {
        Assert.False(_engine.IsApplied(DarkMode));
        Assert.False(_engine.IsApplied(Classic));
    }

    [Fact]
    public void Apply_WritesEveryChange()
    {
        _engine.Apply(DarkMode);

        Assert.Equal(RegistryValue.DWord(0), _store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Equal(RegistryValue.DWord(0), _store.GetValue(Personalize, "SystemUsesLightTheme"));
        Assert.True(_engine.IsApplied(DarkMode));
    }

    [Fact]
    public void IsApplied_RequiresEveryChange()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(0));
        Assert.False(_engine.IsApplied(DarkMode));

        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.DWord(0));
        Assert.True(_engine.IsApplied(DarkMode));
    }

    [Fact]
    public void IsApplied_RequiresTheRightValueAndKind()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(0));
        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.Text("0"));
        Assert.False(_engine.IsApplied(DarkMode));

        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.DWord(1));
        Assert.False(_engine.IsApplied(DarkMode));
    }

    [Fact]
    public void IsApplied_ForTheDefaultValueTweak()
    {
        _store.SetValue(ClassicKey, null, RegistryValue.Text(""));
        Assert.True(_engine.IsApplied(Classic));
    }

    [Fact]
    public void Apply_RecordsABackupOfPreviousState()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));

        _engine.Apply(DarkMode);

        var record = _backup.Get("dark-mode")!;
        Assert.Equal(2, record.Entries.Count);
        var apps = record.Entries.Single(e => e.ValueName == "AppsUseLightTheme");
        Assert.True(apps.Existed);
        Assert.Equal(RegistryValue.DWord(1), apps.Previous);
        var system = record.Entries.Single(e => e.ValueName == "SystemUsesLightTheme");
        Assert.False(system.Existed);
        Assert.Null(system.Previous);
        Assert.False(record.KeyCreated);
        Assert.Equal(["dark-mode"], _engine.BackedUpTweakIds);
    }

    [Fact]
    public void Revert_RestoresPreviousValues_AndDeletesOnesThatDidNotExist()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));
        _engine.Apply(DarkMode);

        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Null(_store.GetValue(Personalize, "SystemUsesLightTheme"));
        Assert.False(_engine.IsApplied(DarkMode));
        Assert.Empty(_engine.BackedUpTweakIds);
        Assert.False(_backup.Contains("dark-mode"));
    }

    [Fact]
    public void Revert_RestoresValuesOfADifferentKind()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.Text("light"));
        _engine.Apply(DarkMode);

        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.Text("light"), _store.GetValue(Personalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void Apply_IsIdempotent_AndNeverOverwritesTheOriginalBackup()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));
        _engine.Apply(DarkMode);

        _engine.Apply(DarkMode);
        _engine.Apply(DarkMode);

        Assert.Equal(["dark-mode"], _engine.BackedUpTweakIds);
        _engine.Revert(DarkMode);
        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void Apply_Again_RewritesValuesThatChangedBehindOurBack()
    {
        _engine.Apply(DarkMode);
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));

        _engine.Apply(DarkMode);

        Assert.True(_engine.IsApplied(DarkMode));
    }

    [Fact]
    public void Revert_ThenApplyAgain_TakesAFreshBackup()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));
        _engine.Apply(DarkMode);
        _engine.Revert(DarkMode);
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(5));

        _engine.Apply(DarkMode);
        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.DWord(5), _store.GetValue(Personalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void Revert_WithoutBackup_DeletesOnlyValuesThatStillEqualTheEnabledOnes()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(0));
        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.DWord(1));

        _engine.Revert(DarkMode);

        Assert.Null(_store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "SystemUsesLightTheme"));
    }

    [Fact]
    public void Revert_WithoutBackup_OnAnUntouchedStore_DoesNothing()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));

        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Empty(_engine.BackedUpTweakIds);
    }

    [Fact]
    public void ClassicMenu_Revert_DeletesTheCreatedKeyTree()
    {
        _engine.Apply(Classic);

        Assert.True(_backup.Get("classic-context-menu")!.KeyCreated);
        Assert.True(_store.KeyExists(ClassicParent));

        _engine.Revert(Classic);

        Assert.False(_store.KeyExists(ClassicParent));
        Assert.False(_store.KeyExists(ClassicKey));
        Assert.False(_engine.IsApplied(Classic));
    }

    [Fact]
    public void ClassicMenu_Revert_KeepsAPreExistingKey()
    {
        _store.SetValue(ClassicParent, "Note", RegistryValue.Text("someone else's"));

        _engine.Apply(Classic);
        Assert.False(_backup.Get("classic-context-menu")!.KeyCreated);
        _engine.Revert(Classic);

        Assert.True(_store.KeyExists(ClassicParent));
        Assert.Equal(RegistryValue.Text("someone else's"), _store.GetValue(ClassicParent, "Note"));
        Assert.False(_engine.IsApplied(Classic));
    }

    [Fact]
    public void ClassicMenu_Revert_RestoresAPreviousDefaultValue()
    {
        _store.SetValue(ClassicKey, null, RegistryValue.Text("C:\\old.dll"));

        _engine.Apply(Classic);
        Assert.Equal(RegistryValue.Text(""), _store.GetValue(ClassicKey, null));
        _engine.Revert(Classic);

        Assert.Equal(RegistryValue.Text("C:\\old.dll"), _store.GetValue(ClassicKey, null));
        Assert.True(_store.KeyExists(ClassicParent));
    }

    [Fact]
    public void ClassicMenu_RevertWithoutBackup_RemovesTheKeyWhenFullyApplied()
    {
        _store.SetValue(ClassicKey, null, RegistryValue.Text(""));

        _engine.Revert(Classic);

        Assert.False(_store.KeyExists(ClassicParent));
    }

    [Fact]
    public void ClassicMenu_RevertWithoutBackup_LeavesAForeignValueAlone()
    {
        _store.SetValue(ClassicKey, null, RegistryValue.Text("C:\\foreign.dll"));

        _engine.Revert(Classic);

        Assert.Equal(RegistryValue.Text("C:\\foreign.dll"), _store.GetValue(ClassicKey, null));
    }

    [Fact]
    public void Tweaks_AreIndependent()
    {
        var web = TweakCatalog.Find("disable-web-search")!;
        _engine.Apply(DarkMode);
        _engine.Apply(web);

        _engine.Revert(DarkMode);

        Assert.True(_engine.IsApplied(web));
        Assert.False(_engine.IsApplied(DarkMode));
        Assert.Equal(["disable-web-search"], _engine.BackedUpTweakIds);
    }

    [Fact]
    public void BackupSurvivesJsonRoundTrip_AndStillReverts()
    {
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(1));
        _engine.Apply(DarkMode);
        _engine.Apply(Classic);
        var json = _engine.Backup.ToJson();

        var restored = new TweakEngine(_store, TweakBackup.FromJson(json));
        restored.Revert(DarkMode);
        restored.Revert(Classic);

        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Null(_store.GetValue(Personalize, "SystemUsesLightTheme"));
        Assert.False(_store.KeyExists(ClassicParent));
        Assert.Empty(restored.BackedUpTweakIds);
    }

    [Fact]
    public void EveryCatalogTweak_AppliesAndRevertsCleanly_OnAnEmptyStore()
    {
        foreach (var tweak in TweakCatalog.All)
        {
            _engine.Apply(tweak);
            Assert.True(_engine.IsApplied(tweak), tweak.Id);
        }

        foreach (var tweak in TweakCatalog.All)
        {
            _engine.Revert(tweak);
            Assert.False(_engine.IsApplied(tweak), tweak.Id);
            foreach (var change in tweak.Changes)
            {
                Assert.Null(_store.GetValue(change.SubKey, change.ValueName));
            }
        }

        Assert.Empty(_engine.BackedUpTweakIds);
        Assert.False(_store.KeyExists(@"Software\Classes\CLSID"));
    }

    [Fact]
    public void EveryCatalogTweak_RestoresPreExistingValuesExactly()
    {
        var original = new Dictionary<(string, string?), RegistryValue>();
        var counter = 100;
        foreach (var change in TweakCatalog.All.SelectMany(t => t.Changes))
        {
            var value = RegistryValue.DWord(counter++);
            _store.SetValue(change.SubKey, change.ValueName, value);
            original[(change.SubKey, change.ValueName)] = value;
        }

        foreach (var tweak in TweakCatalog.All)
        {
            _engine.Apply(tweak);
        }

        foreach (var tweak in TweakCatalog.All)
        {
            _engine.Revert(tweak);
        }

        foreach (var ((subKey, name), value) in original)
        {
            Assert.Equal(value, _store.GetValue(subKey, name));
        }
    }

    [Fact]
    public void Apply_WithAnOlderBackup_BacksUpValuesTheTweakGainedSince()
    {
        // A backup written by a version whose dark-mode tweak only set AppsUseLightTheme.
        _backup.Set("dark-mode", new TweakBackupRecord(false,
            [new TweakBackupEntry(Personalize, "AppsUseLightTheme", true, RegistryValue.DWord(1))]));
        _store.SetValue(Personalize, "AppsUseLightTheme", RegistryValue.DWord(0));
        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.DWord(1));

        _engine.Apply(DarkMode);
        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "SystemUsesLightTheme"));
    }

    [Fact]
    public void Apply_WithACompleteBackup_KeepsTheSameRecord()
    {
        _engine.Apply(DarkMode);
        var record = _backup.Get("dark-mode");

        _engine.Apply(DarkMode);

        Assert.Same(record, _backup.Get("dark-mode"));
    }

    [Fact]
    public void Revert_WithAnOlderBackup_DeletesUncoveredValuesOnlyWhileTheyHaveTheEnabledValue()
    {
        _backup.Set("dark-mode", new TweakBackupRecord(false,
            [new TweakBackupEntry(Personalize, "AppsUseLightTheme", true, RegistryValue.DWord(1))]));
        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.DWord(0));

        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Null(_store.GetValue(Personalize, "SystemUsesLightTheme"));

        _backup.Set("dark-mode", new TweakBackupRecord(false,
            [new TweakBackupEntry(Personalize, "AppsUseLightTheme", true, RegistryValue.DWord(1))]));
        _store.SetValue(Personalize, "SystemUsesLightTheme", RegistryValue.DWord(1));

        _engine.Revert(DarkMode);

        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(Personalize, "SystemUsesLightTheme"));
    }

    [Fact]
    public void BackupChanged_IsRaisedBeforeAnyRegistryWrite()
    {
        var raised = 0;
        _engine.BackupChanged += (_, _) =>
        {
            raised++;
            Assert.True(_backup.Contains("dark-mode"));
            Assert.Null(_store.GetValue(Personalize, "AppsUseLightTheme"));
        };

        _engine.Apply(DarkMode);

        Assert.Equal(1, raised);
        Assert.True(_engine.IsApplied(DarkMode));
    }

    [Fact]
    public void BackupChanged_HandlerFailure_AbortsApplyBeforeWriting()
    {
        _engine.BackupChanged += (_, _) => throw new IOException("disk full");

        Assert.Throws<IOException>(() => _engine.Apply(DarkMode));

        Assert.Null(_store.GetValue(Personalize, "AppsUseLightTheme"));
        Assert.Null(_store.GetValue(Personalize, "SystemUsesLightTheme"));
    }

    [Fact]
    public void BackupChanged_IsRaisedOnRevert_ButNotForARepeatedApplyOrABackuplessRevert()
    {
        _engine.Apply(DarkMode);
        var raised = 0;
        _engine.BackupChanged += (_, _) => raised++;

        _engine.Apply(DarkMode);
        Assert.Equal(0, raised);

        _engine.Revert(DarkMode);
        Assert.Equal(1, raised);

        _engine.Revert(DarkMode);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Apply_AndRevert_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Apply(null!));
        Assert.Throws<ArgumentNullException>(() => _engine.Revert(null!));
        Assert.Throws<ArgumentNullException>(() => _engine.IsApplied(null!));
    }

    [Fact]
    public void CustomTweak_WithBinaryAndQWord_RoundTripsThroughBackup()
    {
        var custom = new TweakDefinition(
            "custom",
            "Custom",
            "A custom tweak.",
            TweakCategory.Behaviour,
            [
                new RegistryChange(@"Software\Custom", "Bin", RegistryValue.Binary([9, 9])),
                new RegistryChange(@"Software\Custom", "Big", RegistryValue.QWord(1L << 40)),
            ],
            RequiresExplorerRestart: false);
        _store.SetValue(@"Software\Custom", "Bin", RegistryValue.Binary([1, 2, 3]));

        _engine.Apply(custom);
        var restored = new TweakEngine(_store, TweakBackup.FromJson(_engine.Backup.ToJson()));
        restored.Revert(custom);

        Assert.Equal(RegistryValue.Binary([1, 2, 3]), _store.GetValue(@"Software\Custom", "Bin"));
        Assert.Null(_store.GetValue(@"Software\Custom", "Big"));
    }
}
