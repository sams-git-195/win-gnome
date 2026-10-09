using WinGnome.Core.Search;

namespace WinGnome.Core.ControlCenter;

/// <summary>A Windows time zone as listed by the Date &amp; Time panel.</summary>
/// <param name="Id">Windows time zone key name, e.g. "W. Europe Standard Time".</param>
/// <param name="DisplayName">Windows display name, e.g. "(UTC+01:00) Amsterdam, Berlin, ...".</param>
/// <param name="BaseOffset">Standard-time offset from UTC.</param>
public sealed record TimeZoneEntry(string Id, string DisplayName, TimeSpan BaseOffset);

/// <summary>Ordering and searching the time zone list.</summary>
public static class TimeZoneList
{
    /// <summary>West to east by standard offset, then by display name.</summary>
    public static IReadOnlyList<TimeZoneEntry> Sort(IEnumerable<TimeZoneEntry> zones) =>
        zones.OrderBy(z => z.BaseOffset).ThenBy(z => z.DisplayName, StringComparer.Ordinal).ToList();

    /// <summary>
    /// The zones whose display name or id contains <paramref name="query"/> (case- and accent-insensitive), in list
    /// order so the result still reads west to east. An empty query returns every zone.
    /// </summary>
    public static IReadOnlyList<TimeZoneEntry> Filter(IReadOnlyList<TimeZoneEntry> zones, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return zones;
        }

        return zones.Where(z => Matches(query, z.DisplayName) || Matches(query, z.Id)).ToList();
    }

    /// <summary>Index of the zone with this id, or -1.</summary>
    public static int IndexOf(IReadOnlyList<TimeZoneEntry> zones, string id)
    {
        for (var i = 0; i < zones.Count; i++)
        {
            if (string.Equals(zones[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Matches(string query, string text) => FuzzyMatcher.Score(query, text) >= FuzzyMatcher.ContainsScore;
}
