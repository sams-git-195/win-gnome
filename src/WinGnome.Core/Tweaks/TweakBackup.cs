using System.Globalization;
using System.Text.Json;

namespace WinGnome.Core.Tweaks;

/// <summary>The previous state of one registry value before a tweak overwrote it.</summary>
/// <param name="SubKey">Key path relative to HKCU.</param>
/// <param name="ValueName">Value name; null for the default value.</param>
/// <param name="Existed">False when the value did not exist (revert deletes it).</param>
/// <param name="Previous">The old value when <paramref name="Existed"/> is true.</param>
public sealed record TweakBackupEntry(string SubKey, string? ValueName, bool Existed, RegistryValue? Previous);

/// <summary>Everything needed to undo one tweak.</summary>
/// <param name="KeyCreated">True when applying the tweak created the key that revert should delete as a tree.</param>
/// <param name="Entries">One entry per registry change, in the tweak's change order.</param>
public sealed record TweakBackupRecord(bool KeyCreated, IReadOnlyList<TweakBackupEntry> Entries);

/// <summary>
/// Remembers the registry state from before each tweak was applied so it can be reverted exactly.
/// Persist it with <see cref="ToJson"/> and restore it with <see cref="FromJson"/>.
/// </summary>
public sealed class TweakBackup
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, TweakBackupRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids of tweaks that currently have a backup.</summary>
    public IReadOnlyCollection<string> TweakIds => _records.Keys;

    /// <summary>True when a backup exists for the tweak.</summary>
    public bool Contains(string tweakId) => _records.ContainsKey(tweakId);

    /// <summary>Gets the backup for a tweak, or null.</summary>
    public TweakBackupRecord? Get(string tweakId) => _records.GetValueOrDefault(tweakId);

    /// <summary>Stores (replacing) the backup for a tweak.</summary>
    public void Set(string tweakId, TweakBackupRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tweakId);
        ArgumentNullException.ThrowIfNull(record);
        _records[tweakId] = record;
    }

    /// <summary>Removes a tweak's backup. Returns true when one existed.</summary>
    public bool Remove(string tweakId) => _records.Remove(tweakId);

    /// <summary>Serialises the backup to indented JSON. Byte arrays are stored as base64.</summary>
    public string ToJson()
    {
        var file = new BackupFile { Version = FormatVersion };
        foreach (var (id, record) in _records)
        {
            file.Tweaks[id] = new RecordDto
            {
                KeyCreated = record.KeyCreated,
                Entries = record.Entries.Select(ToDto).ToList(),
            };
        }

        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>Parses JSON produced by <see cref="ToJson"/>. Malformed input yields an empty backup; a damaged tweak record is skipped.</summary>
    public static TweakBackup FromJson(string? json) => FromJson(json, out _);

    /// <summary>Like <see cref="FromJson(string?)"/>, and reports whether anything in <paramref name="json"/> was lost.</summary>
    /// <param name="json">The saved backup; null or blank means "no backup yet".</param>
    /// <param name="complete">
    /// False when the text is not a backup or a record had to be skipped: saving the result over the original would
    /// then destroy original values, so the caller should keep a copy of the text first.
    /// </param>
    public static TweakBackup FromJson(string? json, out bool complete)
    {
        complete = true;
        var backup = new TweakBackup();
        if (string.IsNullOrWhiteSpace(json))
        {
            return backup;
        }

        BackupFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BackupFile>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            complete = false;
            return backup;
        }

        if (file?.Tweaks is null)
        {
            complete = false;
            return backup;
        }

        foreach (var (id, dto) in file.Tweaks)
        {
            // Ids differing only in case would overwrite each other.
            if (string.IsNullOrWhiteSpace(id) || dto?.Entries is null || backup._records.ContainsKey(id))
            {
                complete = false;
                continue;
            }

            try
            {
                var entries = dto.Entries.Select(FromDto).ToList();
                backup._records[id] = new TweakBackupRecord(dto.KeyCreated, entries);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidOperationException)
            {
                // Skip a damaged record rather than restoring half of a tweak.
                complete = false;
            }
        }

        return backup;
    }

    private static EntryDto ToDto(TweakBackupEntry entry) => new()
    {
        SubKey = entry.SubKey,
        ValueName = entry.ValueName,
        Existed = entry.Existed,
        Kind = entry.Previous?.Kind.ToString(),
        Data = entry.Previous is null ? null : EncodeData(entry.Previous),
    };

    private static TweakBackupEntry FromDto(EntryDto dto)
    {
        if (string.IsNullOrEmpty(dto.SubKey))
        {
            throw new FormatException("Backup entry has no key.");
        }

        if (!dto.Existed)
        {
            return new TweakBackupEntry(dto.SubKey, dto.ValueName, false, null);
        }

        if (!Enum.TryParse<RegistryValueKind>(dto.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind) || dto.Data is null)
        {
            throw new FormatException("Backup entry has an invalid value.");
        }

        return new TweakBackupEntry(dto.SubKey, dto.ValueName, true, new RegistryValue(kind, DecodeData(kind, dto.Data)));
    }

    private static string EncodeData(RegistryValue value) => value.Data switch
    {
        byte[] bytes => Convert.ToBase64String(bytes),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        string s => s,
        var other => throw new InvalidOperationException($"Unsupported registry data type {other.GetType().Name}."),
    };

    private static object DecodeData(RegistryValueKind kind, string data) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(data, NumberStyles.Integer, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(data, NumberStyles.Integer, CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromBase64String(data),
        _ => data,
    };

    private sealed class BackupFile
    {
        public int Version { get; set; }

        public Dictionary<string, RecordDto?> Tweaks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RecordDto
    {
        public bool KeyCreated { get; set; }

        public List<EntryDto>? Entries { get; set; }
    }

    private sealed class EntryDto
    {
        public string SubKey { get; set; } = "";

        public string? ValueName { get; set; }

        public bool Existed { get; set; }

        public string? Kind { get; set; }

        public string? Data { get; set; }
    }
}
