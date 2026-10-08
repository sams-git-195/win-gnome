namespace WinGnome.Core.Tweaks;

/// <summary>When a tweak's change becomes visible to the user.</summary>
public enum TweakActivation
{
    /// <summary>Immediately, or the next time the affected app starts.</summary>
    Immediate,
    /// <summary>After Explorer restarts.</summary>
    RestartExplorer,
    /// <summary>After the user signs out and back in.</summary>
    SignOut,
}

/// <summary>Works out how a <see cref="TweakDefinition"/> takes effect, for the hints shown in the settings UI.</summary>
public static class TweakActivationRules
{
    /// <summary>
    /// <see cref="TweakActivation.SignOut"/> when the definition requires signing out, otherwise
    /// <see cref="TweakActivation.RestartExplorer"/> when it requires an Explorer restart, otherwise immediate.
    /// </summary>
    public static TweakActivation For(TweakDefinition tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        if (tweak.RequiresSignOut)
        {
            return TweakActivation.SignOut;
        }

        return tweak.RequiresExplorerRestart ? TweakActivation.RestartExplorer : TweakActivation.Immediate;
    }
}
