namespace WinGnome.Core.Tweaks;

/// <summary>One HKCU value a tweak writes when enabled. A null <see cref="ValueName"/> is the key's default value.</summary>
public sealed record RegistryChange(string SubKey, string? ValueName, RegistryValue Value);

/// <summary>Where a tweak is listed in the settings UI.</summary>
/// <summary>UI grouping for tweaks. <see cref="Taskbar"/> tweaks style the native taskbar (useful in native taskbar mode).</summary>
public enum TweakCategory { Appearance, Shell, Privacy, Behaviour, Taskbar }

/// <summary>A reversible set of HKCU registry writes.</summary>
/// <param name="Id">Stable identifier stored in settings and in the backup file.</param>
/// <param name="Title">Short UI title.</param>
/// <param name="Description">One-sentence explanation shown under the title.</param>
/// <param name="Category">UI grouping.</param>
/// <param name="Changes">The writes that enable the tweak.</param>
/// <param name="RequiresExplorerRestart">True when Explorer must be restarted (or the user must sign out) for it to take effect.</param>
/// <param name="DeleteKeyOnRevertIfCreated">
/// When true and the tweak had to create the parent of its first change's key, reverting deletes that whole key tree.
/// </param>
/// <param name="BroadcastThemeChange">True when the app should broadcast WM_SETTINGCHANGE "ImmersiveColorSet" after a change.</param>
public sealed record TweakDefinition(
    string Id,
    string Title,
    string Description,
    TweakCategory Category,
    IReadOnlyList<RegistryChange> Changes,
    bool RequiresExplorerRestart,
    bool DeleteKeyOnRevertIfCreated = false,
    bool BroadcastThemeChange = false);
