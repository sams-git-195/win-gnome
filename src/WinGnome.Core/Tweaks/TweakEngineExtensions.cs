namespace WinGnome.Core.Tweaks;

/// <summary>Convenience queries over a <see cref="TweakEngine"/>.</summary>
public static class TweakEngineExtensions
{
    /// <summary>Ids of the catalogue tweaks that are currently applied in the registry, in catalogue order.</summary>
    public static IReadOnlyList<string> AppliedTweakIds(this TweakEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return TweakCatalog.All.Where(engine.IsApplied).Select(t => t.Id).ToList();
    }
}
