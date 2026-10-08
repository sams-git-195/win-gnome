using WinGnome.Core.Settings;
using WinGnome.Core.Shell;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Dock;

/// <summary>Edits the pinned-app list (pin, unpin, reorder, files dropped on the dock). Inputs are never modified.</summary>
public static class DockPins
{
    private static readonly string[] PinnableExtensions = [".exe", ".lnk"];

    /// <summary>True when <paramref name="launchId"/> is already pinned (case-insensitive).</summary>
    public static bool IsPinned(IReadOnlyList<PinnedApp> pins, string launchId)
    {
        ArgumentNullException.ThrowIfNull(pins);
        return IndexOf(pins, launchId) >= 0;
    }

    /// <summary>
    /// Inserts <paramref name="added"/> at <paramref name="index"/> (clamped), skipping launch ids that are already
    /// pinned or repeated. Returns the new list.
    /// </summary>
    public static List<PinnedApp> Insert(IReadOnlyList<PinnedApp> pins, IEnumerable<PinnedApp> added, int index)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(added);

        var result = pins.ToList();
        var position = Math.Clamp(index, 0, result.Count);
        foreach (var pin in added)
        {
            if (pin is null || string.IsNullOrWhiteSpace(pin.LaunchId) || IndexOf(result, pin.LaunchId) >= 0)
            {
                continue;
            }

            result.Insert(position++, pin);
        }

        return result;
    }

    /// <summary>Removes the pin with <paramref name="launchId"/>. Returns the new list.</summary>
    public static List<PinnedApp> Remove(IReadOnlyList<PinnedApp> pins, string launchId)
    {
        ArgumentNullException.ThrowIfNull(pins);
        return pins.Where(p => !string.Equals(p.LaunchId, launchId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Moves the pin with <paramref name="launchId"/> so it ends up at <paramref name="newIndex"/> (clamped).
    /// Returns the new list, or a copy of the input when the id is not pinned.
    /// </summary>
    public static List<PinnedApp> Move(IReadOnlyList<PinnedApp> pins, string launchId, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var result = pins.ToList();
        var from = IndexOf(result, launchId);
        if (from < 0)
        {
            return result;
        }

        var pin = result[from];
        result.RemoveAt(from);
        result.Insert(Math.Clamp(newIndex, 0, result.Count), pin);
        return result;
    }

    /// <summary>
    /// Puts the pins in the order of <paramref name="orderedLaunchIds"/> (as left by a drag in the dock). Unknown ids
    /// are ignored; pins missing from the order keep their relative order at the end. Returns the new list.
    /// </summary>
    public static List<PinnedApp> Reorder(IReadOnlyList<PinnedApp> pins, IEnumerable<string> orderedLaunchIds)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(orderedLaunchIds);

        var remaining = pins.ToList();
        var result = new List<PinnedApp>(remaining.Count);
        foreach (var id in orderedLaunchIds)
        {
            var index = IndexOf(remaining, id);
            if (index >= 0)
            {
                result.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
        }

        result.AddRange(remaining);
        return result;
    }

    /// <summary>True for files that can be pinned by dropping them on the dock (.exe and .lnk).</summary>
    public static bool IsPinnableFile(string? path)
    {
        var name = PathText.FileName(path);
        return PinnableExtensions.Any(ext => name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A pin for a dropped .exe or .lnk file, named after the file.</summary>
    public static PinnedApp FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new PinnedApp { Name = AppIdentity.DisplayNameFromPath(path), LaunchId = path.Trim() };
    }

    private static int IndexOf(IReadOnlyList<PinnedApp> pins, string launchId)
    {
        for (var i = 0; i < pins.Count; i++)
        {
            if (string.Equals(pins[i].LaunchId, launchId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
