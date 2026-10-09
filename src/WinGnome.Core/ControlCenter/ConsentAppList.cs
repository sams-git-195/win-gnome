using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>One app key under a capability of the consent store.</summary>
/// <param name="Name">The key name: a package family name, or for a desktop app its path with '#' for each '\'.</param>
/// <param name="IsDesktop">True for a key under <c>NonPackaged</c>.</param>
/// <param name="Value">The <c>Value</c> text, or null when absent.</param>
/// <param name="LastUsedStart">The <c>LastUsedTimeStart</c> FILETIME, 0 when absent.</param>
/// <param name="LastUsedStop">The <c>LastUsedTimeStop</c> FILETIME, 0 when absent.</param>
public sealed record ConsentKeySnapshot(string Name, bool IsDesktop, string? Value, long LastUsedStart, long LastUsedStop);

/// <summary>Whether an app is using the device now or last used it.</summary>
public enum ConsentUse
{
    None,
    InUse,
    LastUsed,
}

/// <summary>One app of a capability's list.</summary>
/// <param name="Name">What to show.</param>
/// <param name="Key">The registry key name, to write the switch back.</param>
/// <param name="IsDesktop">True for a desktop app (its switch is read-only).</param>
/// <param name="Value">The app's own stored value.</param>
/// <param name="Use">In use, last used or neither.</param>
/// <param name="UseText">"In use" or "Last used ...", null for <see cref="ConsentUse.None"/>.</param>
public sealed record ConsentAppRow(string Name, string Key, bool IsDesktop, ConsentState Value, ConsentUse Use, string? UseText);

/// <summary>The time, zone and culture "Last used" texts are worked out in (passed in so the result is repeatable).</summary>
/// <param name="NowUtc">The current time.</param>
/// <param name="Zone">The zone the user's days are counted in.</param>
/// <param name="Culture">The culture times and dates are written in.</param>
public sealed record ConsentClock(DateTime NowUtc, TimeZoneInfo Zone, CultureInfo Culture);

/// <summary>Turns a capability's consent keys into the app list of the Privacy panel.</summary>
public static class ConsentAppList
{
    private const string NonPackagedKey = "NonPackaged";

    /// <summary>
    /// The apps to list: packaged apps first, then desktop apps, each sorted by name (ordinal, ignoring case). A packaged
    /// app <paramref name="packagedName"/> can't name is hidden (system packages that are not apps); a desktop key
    /// without a path (such as the <c>Executables</c> bookkeeping key) is skipped, and a desktop app is named after its
    /// file.
    /// </summary>
    /// <param name="keys">One snapshot per key.</param>
    /// <param name="packagedName">The display name of a package family name, or null when it isn't an installed app.</param>
    /// <param name="clock">What "Last used" is measured against.</param>
    public static IReadOnlyList<ConsentAppRow> Build(IEnumerable<ConsentKeySnapshot> keys, Func<string, string?> packagedName, ConsentClock clock)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(packagedName);
        ArgumentNullException.ThrowIfNull(clock);

        var rows = new List<ConsentAppRow>();
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key.Name) || key.Name.Equals(NonPackagedKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = key.IsDesktop ? DesktopName(key.Name) : packagedName(key.Name);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var (use, text) = UseOf(key, clock);
            rows.Add(new ConsentAppRow(name, key.Name, key.IsDesktop, ConsentValue.Parse(key.Value), use, text));
        }

        return rows
            .OrderBy(r => r.IsDesktop)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"Today at 14:05", "Yesterday at 09:30", "3 days ago" or a date, for a FILETIME (UTC).</summary>
    public static string? LastUsedText(long fileTime, ConsentClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (fileTime <= 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc())
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(fileTime), clock.Zone);
        var today = TimeZoneInfo.ConvertTimeFromUtc(clock.NowUtc, clock.Zone).Date;
        var days = (today - local.Date).Days;
        return days switch
        {
            <= 0 => $"Today at {local.ToString("t", clock.Culture)}",
            1 => $"Yesterday at {local.ToString("t", clock.Culture)}",
            < 7 => $"{days.ToString(clock.Culture)} days ago",
            _ => local.ToString("d MMM yyyy", clock.Culture),
        };
    }

    // In use: started and not stopped. Otherwise the last stop, if there is one (a stop without a start still counts).
    private static (ConsentUse Use, string? Text) UseOf(ConsentKeySnapshot key, ConsentClock clock)
    {
        if (key.LastUsedStart != 0 && key.LastUsedStop == 0)
        {
            return (ConsentUse.InUse, "In use");
        }

        return LastUsedText(key.LastUsedStop, clock) is { } text ? (ConsentUse.LastUsed, $"Last used {text}") : (ConsentUse.None, null);
    }

    private static string? DesktopName(string key)
    {
        if (!key.Contains('#', StringComparison.Ordinal))
        {
            return null;
        }

        var file = key[(key.LastIndexOf('#') + 1)..];
        var dot = file.LastIndexOf('.');
        var stem = dot > 0 ? file[..dot] : file;
        return stem.Length > 0 ? stem : null;
    }
}
