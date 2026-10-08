using System.IO;
using System.Security;
using Microsoft.Win32;
using WinGnome.Core.Tweaks;
using Win32Kind = Microsoft.Win32.RegistryValueKind;

namespace WinGnome.Features.Settings.Tweaks;

/// <summary>Raised when the registry cannot be read or written. The message is suitable for showing to the user.</summary>
internal sealed class RegistryAccessException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// <see cref="IRegistryStore"/> over the real <c>HKEY_CURRENT_USER</c> hive. Nothing outside HKCU is reachable.
/// Every access failure is translated to a <see cref="RegistryAccessException"/>.
/// </summary>
internal sealed class RegistryStore : IRegistryStore
{
    /// <inheritdoc />
    public RegistryValue? GetValue(string subKey, string? valueName) =>
        Guard($"read {Describe(subKey, valueName)}", () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            var name = valueName ?? "";
            var data = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return data is null || key is null ? null : ToValue(key.GetValueKind(name), data, subKey, valueName);
        });

    /// <inheritdoc />
    public void SetValue(string subKey, string? valueName, RegistryValue value) =>
        Guard($"write {Describe(subKey, valueName)}", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            key.SetValue(valueName ?? "", value.Data, ToWin32Kind(value.Kind));
            return true;
        });

    /// <inheritdoc />
    public void DeleteValue(string subKey, string? valueName) =>
        Guard($"delete {Describe(subKey, valueName)}", () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
            key?.DeleteValue(valueName ?? "", throwOnMissingValue: false);
            return true;
        });

    /// <inheritdoc />
    public bool KeyExists(string subKey) =>
        Guard($"look up {subKey}", () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key is not null;
        });

    /// <inheritdoc />
    public void DeleteKeyTree(string subKey) =>
        Guard($"delete {subKey}", () =>
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        });

    private static T Guard<T>(string action, Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new RegistryAccessException($"Windows did not allow WinGnome to {action}.", ex);
        }
        catch (SecurityException ex)
        {
            throw new RegistryAccessException($"Windows did not allow WinGnome to {action}.", ex);
        }
        catch (IOException ex)
        {
            throw new RegistryAccessException($"WinGnome could not {action}: {ex.Message}", ex);
        }
    }

    private static string Describe(string subKey, string? valueName) =>
        string.IsNullOrEmpty(valueName) ? $@"HKCU\{subKey}" : $@"HKCU\{subKey}\{valueName}";

    private static RegistryValue ToValue(Win32Kind kind, object data, string subKey, string? valueName) => (kind, data) switch
    {
        (Win32Kind.String, string s) => RegistryValue.Text(s),
        (Win32Kind.ExpandString, string s) => RegistryValue.ExpandText(s),
        (Win32Kind.DWord, int i) => RegistryValue.DWord(i),
        (Win32Kind.QWord, long l) => RegistryValue.QWord(l),
        (Win32Kind.Binary, byte[] b) => RegistryValue.Binary(b),
        _ => throw new RegistryAccessException($"{Describe(subKey, valueName)} holds a {kind} value, which WinGnome cannot back up safely."),
    };

    private static Win32Kind ToWin32Kind(Core.Tweaks.RegistryValueKind kind) => kind switch
    {
        Core.Tweaks.RegistryValueKind.String => Win32Kind.String,
        Core.Tweaks.RegistryValueKind.ExpandString => Win32Kind.ExpandString,
        Core.Tweaks.RegistryValueKind.DWord => Win32Kind.DWord,
        Core.Tweaks.RegistryValueKind.QWord => Win32Kind.QWord,
        Core.Tweaks.RegistryValueKind.Binary => Win32Kind.Binary,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported registry value kind."),
    };
}
