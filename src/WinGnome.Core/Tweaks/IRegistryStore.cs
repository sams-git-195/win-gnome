namespace WinGnome.Core.Tweaks;

/// <summary>
/// Minimal registry abstraction. Every key path is relative to HKEY_CURRENT_USER and uses backslashes.
/// A null or empty value name means the key's default value.
/// </summary>
public interface IRegistryStore
{
    /// <summary>Reads a value, or null when the key or value does not exist.</summary>
    RegistryValue? GetValue(string subKey, string? valueName);

    /// <summary>Writes a value, creating the key (and its parents) when needed.</summary>
    void SetValue(string subKey, string? valueName, RegistryValue value);

    /// <summary>Deletes a value. Does nothing when it is absent. The key itself is kept.</summary>
    void DeleteValue(string subKey, string? valueName);

    /// <summary>True when the key exists, including keys that exist only because a sub key was created.</summary>
    bool KeyExists(string subKey);

    /// <summary>Deletes the key and everything below it. Does nothing when it is absent.</summary>
    void DeleteKeyTree(string subKey);
}
