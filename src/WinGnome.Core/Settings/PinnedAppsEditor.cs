namespace WinGnome.Core.Settings;

/// <summary>List operations behind the dock's pinned-apps editor.</summary>
public static class PinnedAppsEditor
{
    /// <summary>True when the item at <paramref name="index"/> can move by <paramref name="offset"/> positions.</summary>
    public static bool CanMove(int count, int index, int offset) =>
        index >= 0 && index < count && offset != 0 && index + offset >= 0 && index + offset < count;

    /// <summary>Moves the item at <paramref name="index"/> by <paramref name="offset"/> positions. Returns false when it cannot move.</summary>
    public static bool Move(IList<PinnedApp> apps, int index, int offset)
    {
        ArgumentNullException.ThrowIfNull(apps);
        if (!CanMove(apps.Count, index, offset))
        {
            return false;
        }

        var item = apps[index];
        apps.RemoveAt(index);
        apps.Insert(index + offset, item);
        return true;
    }

    /// <summary>
    /// Appends <paramref name="app"/> unless it has no launch id or an app with the same launch id (ignoring case)
    /// is already pinned. Returns whether it was added.
    /// </summary>
    public static bool TryAdd(IList<PinnedApp> apps, PinnedApp app)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(app.LaunchId) || Contains(apps, app.LaunchId))
        {
            return false;
        }

        apps.Add(app);
        return true;
    }

    /// <summary>True when an app with <paramref name="launchId"/> (case-insensitive) is in the list.</summary>
    public static bool Contains(IEnumerable<PinnedApp> apps, string launchId)
    {
        ArgumentNullException.ThrowIfNull(apps);
        return apps.Any(a => string.Equals(a.LaunchId, launchId, StringComparison.OrdinalIgnoreCase));
    }
}
