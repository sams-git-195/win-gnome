using WinGnome.Core.Search;

namespace WinGnome.Core.ControlCenter;

/// <summary>Searching the Apps panel's installed-apps list by name, then by publisher.</summary>
public static class AppListSearch
{
    /// <summary>
    /// The items matching <paramref name="query"/>. Names are matched fuzzily (<see cref="FuzzyMatcher"/>) and ranked
    /// best first; an item whose name doesn't match but whose publisher contains the query (or a word of it starts with
    /// the query) follows the name matches. Ties keep list order. An empty query returns every item in list order.
    /// </summary>
    public static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> items, Func<T, string> name, Func<T, string?> publisher, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(publisher);
        if (string.IsNullOrWhiteSpace(query))
        {
            return items;
        }

        var scored = new List<(T Item, int Score, int Index)>();
        for (var i = 0; i < items.Count; i++)
        {
            var score = FuzzyMatcher.Score(query, name(items[i]) ?? "");
            if (score == 0 && FuzzyMatcher.Score(query, publisher(items[i]) ?? "") >= FuzzyMatcher.ContainsScore)
            {
                // Below every name match (the lowest is SubsequenceScore).
                score = 1;
            }

            if (score > 0)
            {
                scored.Add((items[i], score, i));
            }
        }

        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Index).Select(s => s.Item).ToList();
    }
}
