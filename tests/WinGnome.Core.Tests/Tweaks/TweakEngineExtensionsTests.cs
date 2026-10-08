using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class TweakEngineExtensionsTests
{
    [Fact]
    public void AppliedTweakIds_ListsOnlyAppliedTweaksInCatalogueOrder()
    {
        var engine = new TweakEngine(new InMemoryRegistryStore(), new TweakBackup());
        Assert.Empty(engine.AppliedTweakIds());

        engine.Apply(TweakCatalog.Find("show-file-extensions")!);
        engine.Apply(TweakCatalog.Find("dark-mode")!);
        Assert.Equal(["dark-mode", "show-file-extensions"], engine.AppliedTweakIds());

        engine.Revert(TweakCatalog.Find("dark-mode")!);
        Assert.Equal(["show-file-extensions"], engine.AppliedTweakIds());
    }
}
