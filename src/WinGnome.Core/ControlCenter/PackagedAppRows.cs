using System.Text.RegularExpressions;

namespace WinGnome.Core.ControlCenter;

/// <summary>An app from the shell's app catalogue (shell:AppsFolder): its name and parsing name.</summary>
public sealed record CatalogApp(string Name, string ParsingName);

/// <summary>One packaged app (MSIX/Store package) in the Apps panel, standing for every app entry of its package family.</summary>
/// <param name="FamilyName">The package family name, e.g. <c>Microsoft.WindowsCalculator_8wekyb3d8bbwe</c>.</param>
/// <param name="Name">The display name of the family's first app (by name).</param>
/// <param name="AppUserModelId">That app's AUMID, used for its icon.</param>
public sealed record PackagedAppRow(string FamilyName, string Name, string AppUserModelId);

/// <summary>
/// Builds the packaged rows of the installed-apps list from the app catalogue the shell has already loaded, so opening
/// the panel needs no package API. Packaged apps are the entries whose parsing name is an AUMID
/// (<c>&lt;PackageFamilyName&gt;!&lt;AppId&gt;</c>); desktop entries (paths, known-folder ids, explicit AUMIDs without
/// a package family) are ignored.
/// </summary>
public static partial class PackagedAppRows
{
    /// <summary>One row per package family, sorted by name (ordinal, ignoring case).</summary>
    public static IReadOnlyList<PackagedAppRow> Build(IEnumerable<CatalogApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        var byFamily = new Dictionary<string, PackagedAppRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (FamilyNameOf(app.ParsingName) is { } family && !byFamily.ContainsKey(family))
            {
                byFamily[family] = new PackagedAppRow(family, app.Name, app.ParsingName);
            }
        }

        return byFamily.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The package family name of an AUMID such as <c>Name_8wekyb3d8bbwe!App</c>, or null when it isn't one.</summary>
    public static string? FamilyNameOf(string? parsingName)
    {
        if (string.IsNullOrEmpty(parsingName))
        {
            return null;
        }

        var bang = parsingName.IndexOf('!');
        if (bang <= 0 || bang == parsingName.Length - 1)
        {
            return null;
        }

        var family = parsingName[..bang];
        return FamilyName().IsMatch(family) ? family : null;
    }

    // <package name>_<publisher id>: the name is 3-50 letters, digits, dots and dashes; the publisher id is 13
    // characters of Crockford base32 (lower case, no i, l, o or u).
    [GeneratedRegex("^[A-Za-z0-9.-]{3,50}_[0-9a-hjkmnp-tv-z]{13}$")]
    private static partial Regex FamilyName();
}
