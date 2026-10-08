namespace WinGnome.Core.Settings;

/// <summary>What to do with the "start with Windows" registry value.</summary>
public enum StartupAction { None, Write, Delete }

/// <summary>The decision from <see cref="StartupEntry.Plan"/>: the action and, for writes, the value to store.</summary>
public sealed record StartupPlan(StartupAction Action, string? Value);

/// <summary>Pure rules for the HKCU Run-key entry that launches WinGnome at sign-in.</summary>
public static class StartupEntry
{
    /// <summary>Key under HKCU that holds per-user startup programs.</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Name of the value WinGnome owns inside <see cref="RunKey"/>.</summary>
    public const string ValueName = "WinGnome";

    /// <summary>The command line stored in the Run value: the executable path in quotes.</summary>
    public static string ValueFor(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return "\"" + executablePath + "\"";
    }

    /// <summary>
    /// Decides how to bring the registry in line with the setting, touching it only when the stored value differs.
    /// Enabling without a known executable path does nothing.
    /// </summary>
    /// <param name="enabled">The "Start with Windows" setting.</param>
    /// <param name="executablePath">Path of the running executable, if known.</param>
    /// <param name="currentValue">The string currently stored in the Run value, or null when absent.</param>
    public static StartupPlan Plan(bool enabled, string? executablePath, string? currentValue)
    {
        if (!enabled)
        {
            return currentValue is null ? new(StartupAction.None, null) : new(StartupAction.Delete, null);
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new(StartupAction.None, null);
        }

        var desired = ValueFor(executablePath);
        return string.Equals(desired, currentValue, StringComparison.OrdinalIgnoreCase)
            ? new(StartupAction.None, null)
            : new(StartupAction.Write, desired);
    }

    /// <summary>
    /// True when a WinGnome using <paramref name="settingsDirectory"/> owns the Run value. The value is shared by
    /// every profile, so only the default profile manages it: a test or second profile started with
    /// --settings-dir must not rewrite or delete the entry the user's everyday WinGnome relies on.
    /// </summary>
    public static bool IsOwnedBy(string settingsDirectory, string defaultDirectory) =>
        !string.IsNullOrWhiteSpace(settingsDirectory)
        && string.Equals(
            Path.TrimEndingDirectorySeparator(settingsDirectory),
            Path.TrimEndingDirectorySeparator(defaultDirectory),
            StringComparison.OrdinalIgnoreCase);
}
