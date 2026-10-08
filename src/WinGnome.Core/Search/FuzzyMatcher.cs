using System.Globalization;
using System.Text;

namespace WinGnome.Core.Search;

/// <summary>Case- and diacritic-insensitive fuzzy matching for the overview's app and window search.</summary>
public static class FuzzyMatcher
{
    /// <summary>Score of an exact (normalised) match.</summary>
    public const int ExactScore = 1000;

    /// <summary>Score when the candidate starts with the query.</summary>
    public const int PrefixScore = 800;

    /// <summary>Score when some word of the candidate starts with the query.</summary>
    public const int WordPrefixScore = 600;

    /// <summary>Score when the candidate contains the query.</summary>
    public const int ContainsScore = 400;

    /// <summary>Base score of a subsequence match; bonuses bring it up to at most 399.</summary>
    public const int SubsequenceScore = 100;

    /// <summary>
    /// Scores how well <paramref name="candidate"/> matches <paramref name="query"/>: exact 1000, prefix 800,
    /// word-start 600, contains 400, subsequence 100..399, otherwise 0. An empty query scores 1 for everything.
    /// </summary>
    public static int Score(string query, string candidate)
    {
        var q = Normalize(query).Trim();
        if (q.Length == 0)
        {
            return 1;
        }

        var c = Normalize(candidate).Trim();
        if (c.Length == 0)
        {
            return 0;
        }

        if (c == q)
        {
            return ExactScore;
        }

        if (c.StartsWith(q, StringComparison.Ordinal))
        {
            return PrefixScore;
        }

        if (StartsAWord(c, q))
        {
            return WordPrefixScore;
        }

        if (c.Contains(q, StringComparison.Ordinal))
        {
            return ContainsScore;
        }

        return SubsequenceBonusScore(q, c);
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> items that match the query, best first. Equal scores prefer the
    /// shorter text, then the original order. An empty query returns the first items in their original order.
    /// </summary>
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> items, Func<T, string> text, string query, int max)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);
        if (max <= 0)
        {
            return [];
        }

        var scored = new List<(T Item, int Score, int Length, int Index)>();
        var index = 0;
        foreach (var item in items)
        {
            var candidate = text(item) ?? "";
            var score = Score(query, candidate);
            if (score > 0)
            {
                scored.Add((item, score, candidate.Length, index));
            }

            index++;
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Length)
            .ThenBy(s => s.Index)
            .Take(max)
            .Select(s => s.Item)
            .ToList();
    }

    private static bool StartsAWord(string candidate, string query)
    {
        var from = 0;
        while (from < candidate.Length)
        {
            var index = candidate.IndexOf(query, from, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            if (index == 0 || !char.IsLetterOrDigit(candidate[index - 1]))
            {
                return true;
            }

            from = index + 1;
        }

        return false;
    }

    private static int SubsequenceBonusScore(string query, string candidate)
    {
        var bonus = 0;
        var position = 0;
        var previousMatch = -2;
        foreach (var ch in query)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            var found = candidate.IndexOf(ch, position);
            if (found < 0)
            {
                return 0;
            }

            if (found == previousMatch + 1)
            {
                bonus += 10;
            }

            if (found == 0 || !char.IsLetterOrDigit(candidate[found - 1]))
            {
                bonus += 15;
            }

            previousMatch = found;
            position = found + 1;
        }

        return SubsequenceScore + Math.Min(bonus, 299);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().ToLowerInvariant();
    }
}
