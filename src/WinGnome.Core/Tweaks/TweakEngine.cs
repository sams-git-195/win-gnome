namespace WinGnome.Core.Tweaks;

/// <summary>Applies and reverts <see cref="TweakDefinition"/>s against an <see cref="IRegistryStore"/>, keeping a <see cref="TweakBackup"/>.</summary>
public sealed class TweakEngine
{
    private readonly IRegistryStore _store;

    /// <summary>Creates an engine over a registry store and the (possibly empty) backup loaded from disk.</summary>
    public TweakEngine(IRegistryStore store, TweakBackup backup)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(backup);
        _store = store;
        Backup = backup;
    }

    /// <summary>
    /// Raised whenever <see cref="Backup"/> changes. During <see cref="Apply"/> it is raised after the original
    /// values have been recorded but <em>before</em> anything is written, so a handler that persists the backup
    /// guarantees the originals are on disk before the registry changes (a crash in between cannot lose them).
    /// An exception from a handler aborts the apply before any write.
    /// </summary>
    public event EventHandler? BackupChanged;

    /// <summary>The backup this engine reads and writes. Persist it from <see cref="BackupChanged"/> (or after Apply/Revert).</summary>
    public TweakBackup Backup { get; }

    /// <summary>Ids of tweaks that have a backup, i.e. were applied by this engine and not yet reverted.</summary>
    public IReadOnlyList<string> BackedUpTweakIds => Backup.TweakIds.ToList();

    /// <summary>True when every change of the tweak is currently present with its enabled value.</summary>
    public bool IsApplied(TweakDefinition t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t.Changes.Count > 0 && t.Changes.All(c => c.Value.Equals(_store.GetValue(c.SubKey, c.ValueName)));
    }

    /// <summary>
    /// Backs up the current state, then writes every change. An existing backup is never overwritten, so calling
    /// it again is harmless; changes the backup does not cover yet (the tweak gained a value in a newer version)
    /// are added to it before they are written.
    /// </summary>
    public void Apply(TweakDefinition t)
    {
        ArgumentNullException.ThrowIfNull(t);

        var record = Backup.Get(t.Id);
        var updated = record is null ? CreateRecord(t) : AddMissingEntries(t, record);
        if (!ReferenceEquals(updated, record))
        {
            Backup.Set(t.Id, updated);
            BackupChanged?.Invoke(this, EventArgs.Empty);
        }

        foreach (var change in t.Changes)
        {
            _store.SetValue(change.SubKey, change.ValueName, change.Value);
        }
    }

    /// <summary>
    /// Restores the values recorded at Apply time (deleting ones that did not exist), removes a key tree the tweak
    /// created when <see cref="TweakDefinition.DeleteKeyOnRevertIfCreated"/> is set, and forgets the backup.
    /// Values the backup does not cover (including every value when there is no backup) are deleted only when they
    /// still equal the tweak's enabled values.
    /// </summary>
    public void Revert(TweakDefinition t)
    {
        ArgumentNullException.ThrowIfNull(t);

        var parent = ParentKeyToDelete(t);
        var record = Backup.Get(t.Id);
        if (record is not null)
        {
            foreach (var entry in record.Entries.Reverse())
            {
                if (entry.Existed && entry.Previous is not null)
                {
                    _store.SetValue(entry.SubKey, entry.ValueName, entry.Previous);
                }
                else
                {
                    _store.DeleteValue(entry.SubKey, entry.ValueName);
                }
            }

            foreach (var change in t.Changes.Where(c => !IsCovered(record, c)))
            {
                DeleteIfStillEnabled(change);
            }

            if (record.KeyCreated && parent is not null)
            {
                _store.DeleteKeyTree(parent);
            }

            Backup.Remove(t.Id);
            BackupChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var wasApplied = IsApplied(t);
        foreach (var change in t.Changes)
        {
            DeleteIfStillEnabled(change);
        }

        // The tweak's own key is only meaningful while the value is there, so remove it when we undid a full application.
        if (wasApplied && parent is not null)
        {
            _store.DeleteKeyTree(parent);
        }
    }

    private TweakBackupRecord CreateRecord(TweakDefinition t)
    {
        var entries = t.Changes.Select(Snapshot).ToList();
        var parent = ParentKeyToDelete(t);
        var keyCreated = parent is not null && !_store.KeyExists(parent);
        return new TweakBackupRecord(keyCreated, entries);
    }

    private TweakBackupRecord AddMissingEntries(TweakDefinition t, TweakBackupRecord record)
    {
        var missing = t.Changes.Where(c => !IsCovered(record, c)).Select(Snapshot).ToList();
        return missing.Count == 0 ? record : record with { Entries = [.. record.Entries, .. missing] };
    }

    private TweakBackupEntry Snapshot(RegistryChange change)
    {
        var existing = _store.GetValue(change.SubKey, change.ValueName);
        return new TweakBackupEntry(change.SubKey, change.ValueName, existing is not null, existing);
    }

    private void DeleteIfStillEnabled(RegistryChange change)
    {
        if (change.Value.Equals(_store.GetValue(change.SubKey, change.ValueName)))
        {
            _store.DeleteValue(change.SubKey, change.ValueName);
        }
    }

    /// <summary>True when the record has an entry for the change's value (registry names are case-insensitive; null and "" are both the default value).</summary>
    private static bool IsCovered(TweakBackupRecord record, RegistryChange change) =>
        record.Entries.Any(e =>
            string.Equals(e.SubKey.Trim('\\'), change.SubKey.Trim('\\'), StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.ValueName ?? "", change.ValueName ?? "", StringComparison.OrdinalIgnoreCase));

    private static string? ParentKeyToDelete(TweakDefinition t)
    {
        if (!t.DeleteKeyOnRevertIfCreated || t.Changes.Count == 0)
        {
            return null;
        }

        var subKey = t.Changes[0].SubKey.TrimEnd('\\');
        var index = subKey.LastIndexOf('\\');
        return index > 0 ? subKey[..index] : null;
    }
}
