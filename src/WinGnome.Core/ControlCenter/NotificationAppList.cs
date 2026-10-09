namespace WinGnome.Core.ControlCenter;

/// <summary>The values of one <c>Notifications\Settings\&lt;id&gt;</c> registry key; null means the value is absent.</summary>
/// <param name="Id">The key name: an AUMID, a desktop app's own id, or a path.</param>
/// <param name="Enabled">The <c>Enabled</c> value.</param>
/// <param name="ShowBanner">The <c>ShowBanner</c> value.</param>
/// <param name="ShowInActionCenter">The <c>ShowInActionCenter</c> value.</param>
/// <param name="HasLastNotificationTime">True when the key has <c>LastNotificationAddedTime</c> (the app has sent a notification).</param>
public sealed record NotificationKeySnapshot(string Id, int? Enabled, int? ShowBanner, int? ShowInActionCenter, bool HasLastNotificationTime);

/// <summary>One app in the Notifications panel.</summary>
public sealed record NotificationAppRow(string Name, string Id, bool Enabled, bool Banner, bool InCentre);

/// <summary>Turns the notification registry keys into the list of apps the panel shows.</summary>
public static class NotificationAppList
{
    private const string GeneratedIdPrefix = "NotifyIconGeneratedAumid_";
    private const string WindowsPrefix = "Windows.";

    // Windows' own notification sources have no app to look a name up from. Anything not listed is hidden.
    private static readonly Dictionary<string, string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows.SystemToast.AutoPlay"] = "AutoPlay",
        ["Windows.SystemToast.BackgroundAccess"] = "Background activity",
        ["Windows.SystemToast.BackupReminder"] = "Backup reminders",
        ["Windows.SystemToast.Bthprops"] = "Bluetooth",
        ["Windows.SystemToast.BthQuickPair"] = "Bluetooth Swift Pair",
        ["Windows.SystemToast.DefaultAudioEndpoint"] = "Sound devices",
        ["Windows.SystemToast.HelloFace"] = "Windows Hello",
        ["Windows.SystemToast.StartupApp"] = "Startup apps",
        ["Windows.SystemToast.Suggested"] = "Suggested notifications",
        ["Windows.Defender.SecurityCenter"] = "Windows Security",
    };

    /// <summary>
    /// The apps to list, sorted by name (ordinal, ignoring case). A key counts only when it has a value or a last
    /// notification time. Windows' own sources use a built-in name table, and unknown ones are hidden, as are generated
    /// notification-icon ids and packaged apps <paramref name="nameOf"/> can't name. Other ids (desktop apps) show their
    /// looked-up name, else the file name of a path id, else the id.
    /// </summary>
    /// <param name="keys">One snapshot per registry key.</param>
    /// <param name="nameOf">The display name of an app id, or null when it isn't an installed app.</param>
    public static IReadOnlyList<NotificationAppRow> Build(IEnumerable<NotificationKeySnapshot> keys, Func<string, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(nameOf);

        var rows = new List<NotificationAppRow>();
        foreach (var key in keys)
        {
            var hasEntry = key.HasLastNotificationTime || key.Enabled is not null || key.ShowBanner is not null || key.ShowInActionCenter is not null;
            if (!hasEntry || NameFor(key.Id, nameOf) is not { } name)
            {
                continue;
            }

            rows.Add(new NotificationAppRow(name, key.Id, NotificationValue.IsOn(key.Enabled),
                NotificationValue.IsOn(key.ShowBanner), NotificationValue.IsOn(key.ShowInActionCenter)));
        }

        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NameFor(string id, Func<string, string?> nameOf)
    {
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith(GeneratedIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (SystemNames.TryGetValue(id, out var system))
        {
            return system;
        }

        if (id.StartsWith(WindowsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (nameOf(id) is { Length: > 0 } known)
        {
            return known;
        }

        // A packaged app ("Family!App") that isn't installed (or isn't in the catalogue) has no name worth showing.
        if (id.Contains('!', StringComparison.Ordinal))
        {
            return null;
        }

        var slash = id.LastIndexOfAny(['/', '\\']);
        if (slash < 0)
        {
            return id;
        }

        var file = id[(slash + 1)..];
        var dot = file.LastIndexOf('.');
        var stem = dot > 0 ? file[..dot] : file;
        return stem.Length > 0 ? stem : null;
    }
}
