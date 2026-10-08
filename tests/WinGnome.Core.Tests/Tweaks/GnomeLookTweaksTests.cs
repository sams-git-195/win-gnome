using WinGnome.Core.Theming;
using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class GnomeLookTweaksTests
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string SpotlightGuid = "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}";

    private readonly InMemoryRegistryStore _store = new();
    private readonly TweakEngine _engine;

    public GnomeLookTweaksTests()
    {
        _engine = new TweakEngine(_store, new TweakBackup());
    }

    private static TweakDefinition Get(string id) => Assert.IsType<TweakDefinition>(TweakCatalog.Find(id));

    private static Dictionary<(string SubKey, string? Name), RegistryValue> Map(TweakDefinition t) =>
        t.Changes.ToDictionary(c => (c.SubKey, c.ValueName), c => c.Value);

    public static IEnumerable<object[]> GnomeIds() =>
    [
        ["gnome-accent"],
        ["neutral-chrome"],
        ["hide-desktop-icons"],
        ["hide-spotlight-icon"],
    ];

    [Fact]
    public void GnomeLook_GroupsExactlyTheFourTweaks_InOrder()
    {
        Assert.Equal(
            ["gnome-accent", "neutral-chrome", "hide-desktop-icons", "hide-spotlight-icon"],
            TweakCatalog.All.Where(t => t.Category == TweakCategory.GnomeLook).Select(t => t.Id));
    }

    [Fact]
    public void GnomeAccent_WritesTheAdwaitaBlueAccentChanges()
    {
        var t = Get("gnome-accent");

        Assert.Equal(AccentColorChanges.For(HexColor.Parse("#3584E4")), t.Changes);
        Assert.True(t.BroadcastThemeChange);
        Assert.False(t.RequiresExplorerRestart);
        Assert.Equal(RegistryValue.DWord(unchecked((int)0xFFE48435)), Map(t)[(AccentKey, "AccentColorMenu")]);
    }

    [Fact]
    public void NeutralChrome_TurnsOffAccentOnTitleBarsAndShellSurfaces()
    {
        var t = Get("neutral-chrome");

        var map = Map(t);
        Assert.Equal(2, map.Count);
        Assert.Equal(RegistryValue.DWord(0), map[(DwmKey, "ColorPrevalence")]);
        Assert.Equal(RegistryValue.DWord(0), map[(Personalize, "ColorPrevalence")]);
        Assert.True(t.BroadcastThemeChange);
        Assert.False(t.RequiresExplorerRestart);
    }

    [Fact]
    public void HideDesktopIcons_SetsHideIcons_AndNeedsAnExplorerRestart()
    {
        var t = Get("hide-desktop-icons");

        var change = Assert.Single(t.Changes);
        Assert.Equal(Advanced, change.SubKey);
        Assert.Equal("HideIcons", change.ValueName);
        Assert.Equal(RegistryValue.DWord(1), change.Value);
        Assert.True(t.RequiresExplorerRestart);
        Assert.False(t.BroadcastThemeChange);
    }

    [Fact]
    public void HideSpotlightIcon_HidesTheLearnAboutThisPictureIcon_AndNeedsAnExplorerRestart()
    {
        var t = Get("hide-spotlight-icon");

        var change = Assert.Single(t.Changes);
        Assert.Equal(DesktopIcons, change.SubKey);
        Assert.Equal(SpotlightGuid, change.ValueName);
        Assert.Equal(RegistryValue.DWord(1), change.Value);
        Assert.True(t.RequiresExplorerRestart);
        Assert.False(t.DeleteKeyOnRevertIfCreated);
    }

    [Theory]
    [MemberData(nameof(GnomeIds))]
    public void Apply_WritesEveryChange_AndIsApplied(string id)
    {
        var t = Get(id);

        _engine.Apply(t);

        Assert.True(_engine.IsApplied(t));
        Assert.All(t.Changes, c => Assert.Equal(c.Value, _store.GetValue(c.SubKey, c.ValueName)));
    }

    [Theory]
    [MemberData(nameof(GnomeIds))]
    public void Revert_OnAnEmptyRegistry_DeletesEveryValueTheTweakCreated(string id)
    {
        var t = Get(id);
        _engine.Apply(t);

        _engine.Revert(t);

        Assert.All(t.Changes, c => Assert.Null(_store.GetValue(c.SubKey, c.ValueName)));
        Assert.False(_engine.IsApplied(t));
        Assert.Empty(_engine.BackedUpTweakIds);
    }

    [Theory]
    [MemberData(nameof(GnomeIds))]
    public void Revert_RestoresPreviousValues_OfTheSameKindAndData(string id)
    {
        var t = Get(id);
        var originals = t.Changes.Select((c, i) => (Change: c, Original: Original(c, i))).ToList();
        foreach (var (change, original) in originals)
        {
            _store.SetValue(change.SubKey, change.ValueName, original);
        }

        _engine.Apply(t);
        _engine.Revert(t);

        foreach (var (change, original) in originals)
        {
            Assert.Equal(original, _store.GetValue(change.SubKey, change.ValueName));
        }
    }

    [Fact]
    public void GnomeAccent_Revert_RestoresTheUsersExistingAccentPaletteAndColours()
    {
        var t = Get("gnome-accent");
        byte[] palette = [0xF6, 0xBC, 0x95, 0, 0xEB, 0x9B, 0x77, 0, 0xD7, 0x60, 0x42, 0, 0xCF, 0x4A, 0x2E, 0, 0xB0, 0x3A, 0x24, 0, 0x8A, 0x25, 0x17, 0, 0x5D, 0x0D, 0x08, 0, 0x88, 0x17, 0x98, 0];
        _store.SetValue(AccentKey, "AccentPalette", RegistryValue.Binary(palette));
        _store.SetValue(AccentKey, "AccentColorMenu", RegistryValue.DWord(unchecked((int)0xFF4565EF)));
        _store.SetValue(@"Control Panel\Desktop", "AutoColorization", RegistryValue.DWord(1));

        _engine.Apply(t);
        Assert.NotEqual(RegistryValue.Binary(palette), _store.GetValue(AccentKey, "AccentPalette"));
        _engine.Revert(t);

        Assert.Equal(RegistryValue.Binary(palette), _store.GetValue(AccentKey, "AccentPalette"));
        Assert.Equal(RegistryValue.DWord(unchecked((int)0xFF4565EF)), _store.GetValue(AccentKey, "AccentColorMenu"));
        Assert.Equal(RegistryValue.DWord(1), _store.GetValue(@"Control Panel\Desktop", "AutoColorization"));
        Assert.Null(_store.GetValue(AccentKey, "StartColorMenu"));
    }

    [Theory]
    [MemberData(nameof(GnomeIds))]
    public void Backup_SurvivesAJsonRoundTrip_AndStillRevertsExactly(string id)
    {
        var t = Get(id);
        var originals = t.Changes.Select((c, i) => (Change: c, Original: Original(c, i))).ToList();
        foreach (var (change, original) in originals)
        {
            _store.SetValue(change.SubKey, change.ValueName, original);
        }

        _engine.Apply(t);

        var reloaded = new TweakEngine(_store, TweakBackup.FromJson(_engine.Backup.ToJson()));
        reloaded.Revert(t);

        foreach (var (change, original) in originals)
        {
            Assert.Equal(original, _store.GetValue(change.SubKey, change.ValueName));
        }
    }

    [Theory]
    [MemberData(nameof(GnomeIds))]
    public void Backup_OfAnEmptyRegistry_SurvivesAJsonRoundTrip_AndDeletesWhatWasCreated(string id)
    {
        var t = Get(id);
        _engine.Apply(t);

        var reloaded = new TweakEngine(_store, TweakBackup.FromJson(_engine.Backup.ToJson()));
        reloaded.Revert(t);

        Assert.All(t.Changes, c => Assert.Null(_store.GetValue(c.SubKey, c.ValueName)));
    }

    [Fact]
    public void GnomeAccent_Revert_RestoresAnAutoColorizationOfAnotherKind()
    {
        var t = Get("gnome-accent");
        _store.SetValue(@"Control Panel\Desktop", "AutoColorization", RegistryValue.Text("1"));

        _engine.Apply(t);
        _engine.Revert(t);

        Assert.Equal(RegistryValue.Text("1"), _store.GetValue(@"Control Panel\Desktop", "AutoColorization"));
    }

    [Fact]
    public void NeutralChrome_AlreadyNeutral_RevertLeavesTheOriginalZerosAlone()
    {
        var t = Get("neutral-chrome");
        _store.SetValue(DwmKey, "ColorPrevalence", RegistryValue.DWord(0));

        _engine.Apply(t);
        _engine.Revert(t);

        Assert.Equal(RegistryValue.DWord(0), _store.GetValue(DwmKey, "ColorPrevalence"));
        Assert.Null(_store.GetValue(Personalize, "ColorPrevalence"));
    }

    /// <summary>A value of the same kind as the change, different from what the tweak writes.</summary>
    private static RegistryValue Original(RegistryChange change, int index) => change.Value.Kind switch
    {
        RegistryValueKind.Binary => RegistryValue.Binary([1, 2, 3, (byte)index]),
        _ => RegistryValue.DWord(unchecked((int)0xFF000000) + 7 + index),
    };
}
