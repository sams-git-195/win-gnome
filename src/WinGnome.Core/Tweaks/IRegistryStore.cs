namespace WinGnome.Core.Tweaks;

/// <summary>
/// Minimal registry abstraction. Every key path is relative to HKEY_CURRENT_USER and uses backslashes.
/// A null or empty value name means the key's default value.
/// </summary>
/// <remarks>
/// The tweak engine backs up whatever <see cref="GetValue"/> returns and writes it back on revert, so a
/// real-registry implementation must round-trip values exactly:
/// <list type="bullet">
/// <item>Read REG_EXPAND_SZ with <c>RegistryValueOptions.DoNotExpandEnvironmentNames</c>; otherwise the
/// expanded text is backed up and "%VAR%" references are lost on revert.</item>
/// <item>Return REG_DWORD as <see cref="int"/>, REG_QWORD as <see cref="long"/>, REG_BINARY as <see cref="byte"/>[].</item>
/// <item>Throw (for example <see cref="NotSupportedException"/>) for kinds <see cref="RegistryValueKind"/> cannot
/// represent (REG_MULTI_SZ, REG_NONE, ...). Never report such a value as absent: the backup would then record
/// "did not exist" and revert would delete it. Apply reads every value before writing, so throwing is safe.</item>
/// </list>
/// </remarks>
public interface IRegistryStore
{
    /// <summary>Reads a value, or null when the key or value does not exist. See the remarks for the type mapping.</summary>
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
