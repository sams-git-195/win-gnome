using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class AppListSearchTests
{
    private sealed record Item(string Name, string? Publisher);

    private static readonly Item[] Items =
    [
        new("Visual Studio Code", "Microsoft Corporation"),
        new("7-Zip", "Igor Pavlov"),
        new("Code Writer", null),
        new("Steam", "Valve Corporation"),
        new("Microsoft Edge", "Microsoft Corporation"),
    ];

    private static string[] Search(string? query) =>
        [.. AppListSearch.Filter(Items, i => i.Name, i => i.Publisher, query).Select(i => i.Name)];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Filter_EmptyQuery_ReturnsEverythingInListOrder(string? query) =>
        Assert.Equal(["Visual Studio Code", "7-Zip", "Code Writer", "Steam", "Microsoft Edge"], Search(query));

    [Fact]
    public void Filter_RanksPrefixThenWordStartThenSubsequence() =>
        // "Microsoft Edge" holds c, o, d, e in order.
        Assert.Equal(["Code Writer", "Visual Studio Code", "Microsoft Edge"], Search("code"));

    [Fact]
    public void Filter_PublisherMatch_FollowsNameMatches() =>
        Assert.Equal(["Microsoft Edge", "Visual Studio Code"], Search("microsoft"));

    [Fact]
    public void Filter_PublisherOnlyMatch_IsFound() =>
        Assert.Equal(["Steam"], Search("valve"));

    [Fact]
    public void Filter_PublisherSubsequence_IsNotAMatch() =>
        // "vlv" is a subsequence of "Valve Corporation" but not of any name; publisher matches need the text itself.
        Assert.Empty(Search("vlv"));

    [Fact]
    public void Filter_NameSubsequence_IsAMatch() =>
        Assert.Equal(["Visual Studio Code"], Search("vsc"));

    [Fact]
    public void Filter_NoMatch_GivesEmpty() =>
        Assert.Empty(Search("qqq"));

    [Fact]
    public void Filter_IsCaseAndAccentInsensitive() =>
        Assert.Equal(["Steam"], Search("STÉAM"));
}
