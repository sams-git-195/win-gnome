using WinGnome.Core.Search;

namespace WinGnome.Core.Tests.Search;

public class FuzzyMatcherTests
{
    private static readonly string[] Sample = ["Alpha", "Beta", "Gamma"];
    private static readonly string[] Accented = ["Résumé Editor", "Other"];

    [Theory]
    [InlineData("code", "Code", 1000)]
    [InlineData("CODE", "code", 1000)]
    [InlineData("  code ", "Code", 1000)]
    [InlineData("cafe", "Café", 1000)]
    [InlineData("café", "CAFE", 1000)]
    [InlineData("vis", "Visual Studio Code", 800)]
    [InlineData("visual s", "Visual Studio Code", 800)]
    [InlineData("studio", "Visual Studio Code", 600)]
    [InlineData("studio c", "Visual Studio Code", 600)]
    [InlineData("code", "Visual Studio Code", 600)]
    [InlineData("tud", "Visual Studio Code", 400)]
    [InlineData("isual", "Visual Studio Code", 400)]
    [InlineData("xyz", "Visual Studio Code", 0)]
    [InlineData("codes", "Code", 0)]
    public void Score_Tiers(string query, string candidate, int expected)
    {
        Assert.Equal(expected, FuzzyMatcher.Score(query, candidate));
    }

    [Fact]
    public void Score_WordStart_RequiresNonLetterBefore()
    {
        // "tud" is inside "Studio", not at a word start -> contains tier
        Assert.Equal(400, FuzzyMatcher.Score("tud", "Studio"));
        Assert.Equal(600, FuzzyMatcher.Score("b", "a-b"));
        Assert.Equal(600, FuzzyMatcher.Score("2", "Windows 2000"));
    }

    [Fact]
    public void Score_Subsequence_IsBetween100And399()
    {
        var score = FuzzyMatcher.Score("vsc", "Visual Studio Code");

        Assert.InRange(score, 100, 399);
    }

    [Fact]
    public void Score_Subsequence_RewardsWordStartsAndConsecutiveLetters()
    {
        var acronym = FuzzyMatcher.Score("vsc", "Visual Studio Code");
        var scattered = FuzzyMatcher.Score("vsc", "avasbcb");

        Assert.True(acronym > scattered, $"{acronym} should beat {scattered}");
    }

    [Fact]
    public void Score_Subsequence_MustKeepLetterOrder()
    {
        Assert.Equal(0, FuzzyMatcher.Score("edoc", "Code"));
        Assert.True(FuzzyMatcher.Score("cde", "Code") > 0);
    }

    [Fact]
    public void Score_Subsequence_NeverReachesContainsTier()
    {
        Assert.InRange(FuzzyMatcher.Score("v s c", "Visual Studio Code"), 100, 399);
        Assert.True(FuzzyMatcher.Score("abcdefghij", "a_b_c_d_e_f_g_h_i_j") <= 399);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Score_EmptyQuery_IsOneForEverything(string query)
    {
        Assert.Equal(1, FuzzyMatcher.Score(query, "Anything"));
        Assert.Equal(1, FuzzyMatcher.Score(query, ""));
    }

    [Fact]
    public void Score_EmptyCandidate_IsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("a", ""));
        Assert.Equal(0, FuzzyMatcher.Score("a", "   "));
    }

    [Fact]
    public void Score_NullInputsAreTolerated()
    {
        Assert.Equal(1, FuzzyMatcher.Score(null!, "x"));
        Assert.Equal(0, FuzzyMatcher.Score("x", null!));
    }

    [Fact]
    public void Score_IgnoresCombiningMarksInDecomposedInput()
    {
        // "e" + combining acute accent
        Assert.Equal(1000, FuzzyMatcher.Score("cafe", "café"));
        Assert.Equal(800, FuzzyMatcher.Score("café", "cafeteria"));
    }

    [Fact]
    public void Rank_OrdersByScoreDescending()
    {
        var items = new[] { "Notepad++ Code Helper", "Visual Studio Code", "Code", "Codec Pack", "Decode" };

        var ranked = FuzzyMatcher.Rank(items, s => s, "code", 10);

        // exact, prefix (shorter first), word-start, contains
        Assert.Equal(["Code", "Codec Pack", "Visual Studio Code", "Notepad++ Code Helper", "Decode"], ranked);
    }

    [Fact]
    public void Rank_EqualScores_PreferShorterText_ThenOriginalOrder()
    {
        var items = new[] { "Terminal Preview", "Terminal", "Terminals", "Terminal Admin" };

        var ranked = FuzzyMatcher.Rank(items, s => s, "term", 10);

        Assert.Equal(["Terminal", "Terminals", "Terminal Admin", "Terminal Preview"], ranked);
    }

    [Fact]
    public void Rank_IsStableForIdenticalTexts()
    {
        var items = new[] { (Id: 1, Name: "App"), (Id: 2, Name: "App"), (Id: 3, Name: "App") };

        var ranked = FuzzyMatcher.Rank(items, i => i.Name, "app", 10);

        Assert.Equal([1, 2, 3], ranked.Select(i => i.Id));
    }

    [Fact]
    public void Rank_DropsNonMatches()
    {
        var ranked = FuzzyMatcher.Rank(Sample, s => s, "zzz", 10);
        Assert.Empty(ranked);
    }

    [Fact]
    public void Rank_RespectsMax()
    {
        var items = Enumerable.Range(0, 20).Select(i => "App " + i).ToArray();

        Assert.Equal(5, FuzzyMatcher.Rank(items, s => s, "app", 5).Count);
        Assert.Equal(["App 0", "App 1"], FuzzyMatcher.Rank(items, s => s, "", 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rank_NonPositiveMax_IsEmpty(int max)
    {
        Assert.Empty(FuzzyMatcher.Rank(Sample, s => s, "a", max));
    }

    [Fact]
    public void Rank_EmptyQuery_KeepsOriginalOrder()
    {
        var items = new[] { "Zebra", "Apple", "Mango" };
        Assert.Equal(items, FuzzyMatcher.Rank(items, s => s, "", 10));
        Assert.Equal(items, FuzzyMatcher.Rank(items, s => s, "  ", 10));
    }

    [Fact]
    public void Rank_WorksOnLazySequencesAndNullText()
    {
        static IEnumerable<string?> Items()
        {
            yield return null;
            yield return "Match";
        }

        var ranked = FuzzyMatcher.Rank(Items(), s => s!, "match", 5);

        Assert.Equal(["Match"], ranked);
    }

    [Fact]
    public void Rank_UsesDiacriticInsensitiveMatching()
    {
        var ranked = FuzzyMatcher.Rank(Accented, s => s, "resume", 5);
        Assert.Equal(["Résumé Editor"], ranked);
    }
}
