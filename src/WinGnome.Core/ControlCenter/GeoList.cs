using System.Globalization;
using WinGnome.Core.Search;

namespace WinGnome.Core.ControlCenter;

/// <summary>A country or region Windows can use for the user's location.</summary>
/// <param name="Name">The geographical name <c>SetUserGeoName</c> takes, e.g. "GB".</param>
/// <param name="DisplayName">The name shown to the user, e.g. "United Kingdom".</param>
public sealed record GeoEntry(string Name, string DisplayName);

/// <summary>Ordering and searching the country list.</summary>
public static class GeoList
{
    /// <summary>By display name, ignoring case, with accented letters sorted beside their plain letter ("Åland Islands" among the As).</summary>
    public static IReadOnlyList<GeoEntry> Sort(IEnumerable<GeoEntry> entries) =>
        entries.OrderBy(e => e.DisplayName, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)).ToList();

    /// <summary>
    /// The entries whose display name or code contains <paramref name="query"/> (case- and accent-insensitive), in list
    /// order. An empty query returns every entry.
    /// </summary>
    public static IReadOnlyList<GeoEntry> Filter(IReadOnlyList<GeoEntry> entries, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return entries;
        }

        return entries.Where(e => Matches(query, e.DisplayName) || Matches(query, e.Name)).ToList();
    }

    /// <summary>The entry with this name (case-insensitive), or null.</summary>
    public static GeoEntry? Find(IReadOnlyList<GeoEntry> entries, string? name) =>
        entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string query, string text) => FuzzyMatcher.Score(query, text) >= FuzzyMatcher.ContainsScore;
}
