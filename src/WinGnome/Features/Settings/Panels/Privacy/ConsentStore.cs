using Microsoft.Win32;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Features.Settings.Panels.Privacy;

/// <summary>A device the Privacy panel manages.</summary>
internal enum PrivacyCapability
{
    Camera,
    Microphone,
    Location,
}

/// <summary>Everything the Privacy panel shows about one capability, as read from the registry.</summary>
/// <param name="Device">The device-wide switch (HKLM, read-only: changing it needs an administrator).</param>
/// <param name="User">"Let apps access" for the current user.</param>
/// <param name="Desktop">"Let desktop apps access" for the current user.</param>
/// <param name="Apps">Packaged and desktop app keys.</param>
internal sealed record ConsentSnapshot(ConsentState Device, ConsentState User, ConsentState Desktop, IReadOnlyList<ConsentKeySnapshot> Apps);

/// <summary>
/// Reads and writes the privacy switches Windows keeps in the capability access manager's consent store. The only
/// class that touches these keys. The storage is undocumented (KI-086); the capability access manager service watches
/// it, so a change takes effect at once. HKLM is only ever read.
/// </summary>
internal static class ConsentStore
{
    private const string StorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string NonPackaged = "NonPackaged";
    private const string ValueName = "Value";

    /// <summary>Reads one capability. Walks every app key, so call it off the UI thread.</summary>
    public static ConsentSnapshot Read(PrivacyCapability capability)
    {
        var path = $@"{StorePath}\{RegistryName(capability)}";
        using var device = Registry.LocalMachine.OpenSubKey(path);
        using var user = Registry.CurrentUser.OpenSubKey(path);
        using var desktop = user?.OpenSubKey(NonPackaged);

        var apps = new List<ConsentKeySnapshot>();
        foreach (var name in user?.GetSubKeyNames() ?? [])
        {
            if (!name.Equals(NonPackaged, StringComparison.OrdinalIgnoreCase))
            {
                apps.Add(ReadApp(user!, name, isDesktop: false));
            }
        }

        foreach (var name in desktop?.GetSubKeyNames() ?? [])
        {
            apps.Add(ReadApp(desktop!, name, isDesktop: true));
        }

        return new ConsentSnapshot(ConsentValue.Parse(Text(device)), ConsentValue.Parse(Text(user)), ConsentValue.Parse(Text(desktop)), apps);
    }

    /// <summary>Sets "Let apps access" for the current user.</summary>
    public static void SetUser(PrivacyCapability capability, bool allow) => Write($@"{StorePath}\{RegistryName(capability)}", allow);

    /// <summary>Sets "Let desktop apps access" for the current user.</summary>
    public static void SetDesktop(PrivacyCapability capability, bool allow) =>
        Write($@"{StorePath}\{RegistryName(capability)}\{NonPackaged}", allow);

    /// <summary>Sets one packaged app's switch.</summary>
    /// <param name="capability">The device.</param>
    /// <param name="packageFamilyName">The app's key name.</param>
    /// <param name="allow">True to allow.</param>
    public static void SetApp(PrivacyCapability capability, string packageFamilyName, bool allow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFamilyName);
        if (packageFamilyName.Contains('\\', StringComparison.Ordinal) || packageFamilyName.Equals(NonPackaged, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"\"{packageFamilyName}\" is not a package family name.", nameof(packageFamilyName));
        }

        Write($@"{StorePath}\{RegistryName(capability)}\{packageFamilyName}", allow);
    }

    private static string RegistryName(PrivacyCapability capability) => capability switch
    {
        PrivacyCapability.Camera => "webcam",
        PrivacyCapability.Microphone => "microphone",
        PrivacyCapability.Location => "location",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
    };

    private static ConsentKeySnapshot ReadApp(RegistryKey parent, string name, bool isDesktop)
    {
        using var app = parent.OpenSubKey(name);
        return new ConsentKeySnapshot(name, isDesktop, Text(app), Qword(app, "LastUsedTimeStart"), Qword(app, "LastUsedTimeStop"));
    }

    private static void Write(string path, bool allow)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key.SetValue(ValueName, ConsentValue.ToText(allow), RegistryValueKind.String);
    }

    private static string? Text(RegistryKey? key) => key?.GetValue(ValueName) as string;

    private static long Qword(RegistryKey? key, string name) => key?.GetValue(name) is long value ? value : 0;
}
