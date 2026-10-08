namespace WinGnome.Core.Tweaks;

/// <summary>
/// A dictionary-backed <see cref="IRegistryStore"/> for tests and for the app's safe/selftest mode.
/// Key and value names are case-insensitive, like the real registry.
/// </summary>
public sealed class InMemoryRegistryStore : IRegistryStore
{
    private readonly Dictionary<string, Dictionary<string, RegistryValue>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of keys that were explicitly created (parents of created keys are implied, not counted).</summary>
    public int KeyCount => _keys.Count;

    /// <inheritdoc />
    public RegistryValue? GetValue(string subKey, string? valueName) =>
        _keys.TryGetValue(NormalizeKey(subKey), out var values) && values.TryGetValue(valueName ?? "", out var value)
            ? Copy(value)
            : null;

    /// <inheritdoc />
    public void SetValue(string subKey, string? valueName, RegistryValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var key = NormalizeKey(subKey);
        if (!_keys.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
            _keys.Add(key, values);
        }

        values[valueName ?? ""] = Copy(value);
    }

    /// <inheritdoc />
    public void DeleteValue(string subKey, string? valueName)
    {
        if (_keys.TryGetValue(NormalizeKey(subKey), out var values))
        {
            values.Remove(valueName ?? "");
        }
    }

    /// <inheritdoc />
    public bool KeyExists(string subKey)
    {
        var key = NormalizeKey(subKey);
        if (key.Length == 0)
        {
            return true;
        }

        var prefix = key + "\\";
        return _keys.ContainsKey(key) || _keys.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void DeleteKeyTree(string subKey)
    {
        var key = NormalizeKey(subKey);
        if (key.Length == 0)
        {
            return;
        }

        var prefix = key + "\\";
        foreach (var existing in _keys.Keys.Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _keys.Remove(existing);
        }
    }

    private static string NormalizeKey(string subKey) =>
        (subKey ?? "").Replace('/', '\\').Trim('\\', ' ');

    private static RegistryValue Copy(RegistryValue value) =>
        value.Data is byte[] bytes ? new RegistryValue(value.Kind, bytes.Clone()) : value;
}
