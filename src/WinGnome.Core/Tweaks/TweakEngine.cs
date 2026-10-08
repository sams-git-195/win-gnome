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

    /// <summary>The backup this engine reads and writes. Persist it after Apply/Revert.</summary>
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
    /// Backs up the current state (only when the tweak has no backup yet), then writes every change.
    /// Calling it again is harmless and never overwrites the original backup.
    /// </summary>
    public void Apply(TweakDefinition t)
    {
        ArgumentNullException.ThrowIfNull(t);

        if (!Backup.Contains(t.Id))
        {
            var entries = new List<TweakBackupEntry>(t.Changes.Count);
            foreach (var change in t.Changes)
            {
                var existing = _store.GetValue(change.SubKey, change.ValueName);
                entries.Add(new TweakBackupEntry(change.SubKey, change.ValueName, existing is not null, existing));
            }

            var keyCreated = false;
            var parent = ParentKeyToDelete(t);
            if (parent is not null)
            {
                keyCreated = !_store.KeyExists(parent);
            }

            Backup.Set(t.Id, new TweakBackupRecord(keyCreated, entries));
        }

        foreach (var change in t.Changes)
        {
            _store.SetValue(change.SubKey, change.ValueName, change.Value);
        }
    }

    /// <summary>
    /// Restores the values recorded at Apply time (deleting ones that did not exist), removes a key tree the tweak
    /// created when <see cref="TweakDefinition.DeleteKeyOnRevertIfCreated"/> is set, and forgets the backup.
    /// Without a backup it deletes only values that still equal the tweak's enabled values.
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

            if (record.KeyCreated && parent is not null)
            {
                _store.DeleteKeyTree(parent);
            }

            Backup.Remove(t.Id);
            return;
        }

        var wasApplied = IsApplied(t);
        foreach (var change in t.Changes)
        {
            if (change.Value.Equals(_store.GetValue(change.SubKey, change.ValueName)))
            {
                _store.DeleteValue(change.SubKey, change.ValueName);
            }
        }

        // The tweak's own key is only meaningful while the value is there, so remove it when we undid a full application.
        if (wasApplied && parent is not null)
        {
            _store.DeleteKeyTree(parent);
        }
    }

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
