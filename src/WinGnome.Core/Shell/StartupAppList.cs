namespace WinGnome.Core.Shell;

/// <summary>Where Windows keeps an item's StartupApproved value: the hive and the key under <see cref="StartupAppList.ApprovedKeyPath"/>.</summary>
/// <param name="Machine">True for HKLM (needs an administrator to change), false for HKCU.</param>
/// <param name="KeyName"><c>Run</c>, <c>Run32</c> or <c>StartupFolder</c>.</param>
public sealed record StartupApprovalLocation(bool Machine, string KeyName);

/// <summary>One start-up item in the Apps panel.</summary>
/// <param name="Entry">The item as read (value name or file name, command line or path, source).</param>
/// <param name="DisplayName">The value name, or the shortcut's file name without its extension.</param>
/// <param name="Enabled">False when its StartupApproved value marks it disabled.</param>
/// <param name="Editable">The current user can turn it on or off (HKCU Run and the user's Startup folder).</param>
/// <param name="Removable">It can be moved to the Recycle Bin (shortcuts in the user's Startup folder only).</param>
public sealed record StartupAppRow(StartupEntry Entry, string DisplayName, bool Enabled, bool Editable, bool Removable)
{
    /// <summary>True for items that start for every user (HKLM Run keys and the common Startup folder).</summary>
    public bool IsMachineWide => Entry.Source is StartupSource.RunMachine or StartupSource.RunMachine32 or StartupSource.FolderCommon;
}

/// <summary>
/// Turns the Run keys and Startup folders into the Apps panel's start-up list (GNOME Tweaks style). Items are
/// switched off through StartupApproved, never deleted; only the user's own Startup-folder shortcuts can be removed.
/// RunOnce items aren't listed: they run once and StartupApproved doesn't apply to them.
/// </summary>
public static class StartupAppList
{
    /// <summary>The StartupApproved key, under HKCU and HKLM.</summary>
    public const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>
    /// The rows sorted by display name (ordinal, ignoring case), user items before machine ones on a tie. The HKCU
    /// Run value named <paramref name="ownValueName"/> (WinGnome's own, owned by General → Start with Windows) and
    /// Startup-folder metadata (<c>desktop.ini</c>) are left out.
    /// </summary>
    public static IReadOnlyList<StartupAppRow> Build(IEnumerable<StartupEntry> entries, StartupApprovedSet approvals, string ownValueName)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(approvals);
        var rows = new List<StartupAppRow>();
        foreach (var entry in entries)
        {
            if (ApprovalLocation(entry.Source) is null || IsHidden(entry, ownValueName))
            {
                continue;
            }

            var isFolder = entry.Source is StartupSource.FolderUser or StartupSource.FolderCommon;
            var displayName = isFolder ? PathText.FileNameWithoutExtension(entry.Name) : entry.Name;
            var userOwned = entry.Source is StartupSource.RunUser or StartupSource.FolderUser;
            rows.Add(new StartupAppRow(
                entry,
                displayName.Length > 0 ? displayName : entry.Name,
                approvals.Get(entry.Source, entry.Name) == StartupApproval.Enabled,
                Editable: userOwned,
                Removable: entry.Source == StartupSource.FolderUser));
        }

        return rows
            .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.IsMachineWide)
            .ToList();
    }

    /// <summary>Where the item's StartupApproved value lives, or null for RunOnce items (which have none).</summary>
    public static StartupApprovalLocation? ApprovalLocation(StartupSource source) => source switch
    {
        StartupSource.RunUser => new StartupApprovalLocation(Machine: false, "Run"),
        StartupSource.FolderUser => new StartupApprovalLocation(Machine: false, "StartupFolder"),
        StartupSource.RunMachine => new StartupApprovalLocation(Machine: true, "Run"),
        StartupSource.RunMachine32 => new StartupApprovalLocation(Machine: true, "Run32"),
        StartupSource.FolderCommon => new StartupApprovalLocation(Machine: true, "StartupFolder"),
        _ => null,
    };

    private static bool IsHidden(StartupEntry entry, string ownValueName) =>
        (entry.Source == StartupSource.RunUser && string.Equals(entry.Name, ownValueName, StringComparison.OrdinalIgnoreCase))
        || (entry.Source is StartupSource.FolderUser or StartupSource.FolderCommon
            && string.Equals(entry.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase));
}
