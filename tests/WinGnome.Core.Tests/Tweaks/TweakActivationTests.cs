using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class TweakActivationTests
{
    private static TweakDefinition Tweak(string description, bool restart, bool signOut = false) =>
        new("t", "Title", description, TweakCategory.Behaviour, [new RegistryChange(@"Software\X", "V", RegistryValue.DWord(1))], restart,
            RequiresSignOut: signOut);

    [Fact]
    public void For_PlainTweak_IsImmediate() =>
        Assert.Equal(TweakActivation.Immediate, TweakActivationRules.For(Tweak("Does a thing.", false)));

    [Fact]
    public void For_RestartFlag_IsRestartExplorer() =>
        Assert.Equal(TweakActivation.RestartExplorer, TweakActivationRules.For(Tweak("Does a thing.", true)));

    [Fact]
    public void For_SignOutFlag_WinsOverRestartFlag() =>
        Assert.Equal(TweakActivation.SignOut, TweakActivationRules.For(Tweak("Does a thing.", true, signOut: true)));

    [Fact]
    public void For_DescriptionMentioningSignOut_DoesNotImplySignOut() =>
        Assert.Equal(TweakActivation.Immediate, TweakActivationRules.For(Tweak("You can sign out at any time.", false)));

    [Fact]
    public void For_Catalogue_OnlyWebSearchNeedsSignOut()
    {
        var signOut = TweakCatalog.All.Where(t => TweakActivationRules.For(t) == TweakActivation.SignOut).Select(t => t.Id);
        Assert.Equal(["disable-web-search"], signOut);
    }
}
