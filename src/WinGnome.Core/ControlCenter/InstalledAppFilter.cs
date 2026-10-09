namespace WinGnome.Core.ControlCenter;

/// <summary>
/// Which Uninstall keys are shown as installed apps, following the rules Windows' own "Programs and Features" list
/// uses: keys without a <c>DisplayName</c>, system components, child entries (<c>ParentKeyName</c>) and updates
/// (<c>ReleaseType</c>) are hidden, and an app registered in more than one view is shown once.
/// </summary>
public static class InstalledAppFilter
{
    // ReleaseType values that mark an update to another product rather than a product of its own.
    private static readonly HashSet<string> UpdateReleaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Update",
        "Hotfix",
        "Security Update",
        "Update Rollup",
        "Service Pack",
    };

    /// <summary>
    /// The visible apps, sorted by name (ordinal, ignoring case). Of duplicates (same name, version and publisher,
    /// ignoring case) the first in input order is kept, so pass the keys in <see cref="InstalledAppScope"/> order.
    /// </summary>
    public static IReadOnlyList<InstalledAppRecord> Apply(IEnumerable<InstalledAppRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var seen = new HashSet<(string, string, string)>();
        var shown = new List<InstalledAppRecord>();
        foreach (var record in records)
        {
            if (!IsShown(record))
            {
                continue;
            }

            var identity = (record.DisplayName!.ToUpperInvariant(), (record.DisplayVersion ?? "").ToUpperInvariant(), (record.Publisher ?? "").ToUpperInvariant());
            if (seen.Add(identity))
            {
                shown.Add(record);
            }
        }

        return shown.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when the key describes an app of its own that Windows would list.</summary>
    public static bool IsShown(InstalledAppRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !string.IsNullOrWhiteSpace(record.DisplayName)
            && !record.SystemComponent
            && record.ParentKeyName is null
            && (record.ReleaseType is null || !UpdateReleaseTypes.Contains(record.ReleaseType));
    }
}
