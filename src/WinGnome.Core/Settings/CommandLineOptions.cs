namespace WinGnome.Core.Settings;

/// <summary>Parsed command-line switches. Unknown arguments are collected rather than rejected.</summary>
public sealed record CommandLineOptions
{
    /// <summary>Use this directory for settings, backups and logs instead of %APPDATA%\WinGnome.</summary>
    public string? SettingsDirectory { get; init; }

    /// <summary>Run without hiding the taskbar, applying tweaks, or installing keyboard/mouse hooks.</summary>
    public bool Safe { get; init; }

    /// <summary>Start every feature, run briefly, shut down and exit with 0 on success.</summary>
    public bool SelfTest { get; init; }

    /// <summary>Restore the native taskbar (after a crash) and exit.</summary>
    public bool RestoreTaskbar { get; init; }

    /// <summary>Open the settings window on start.</summary>
    public bool OpenSettings { get; init; }

    public IReadOnlyList<string> Unknown { get; init; } = [];

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? settingsDir = null;
        bool safe = false, selfTest = false, restore = false, openSettings = false;
        var unknown = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--settings-dir" when i + 1 < args.Count:
                    settingsDir = args[++i];
                    break;
                case "--safe":
                    safe = true;
                    break;
                case "--selftest":
                    selfTest = true;
                    break;
                case "--restore-taskbar":
                    restore = true;
                    break;
                case "--settings":
                    openSettings = true;
                    break;
                default:
                    unknown.Add(arg);
                    break;
            }
        }

        return new CommandLineOptions
        {
            SettingsDirectory = string.IsNullOrWhiteSpace(settingsDir) ? null : settingsDir,
            Safe = safe,
            SelfTest = selfTest,
            RestoreTaskbar = restore,
            OpenSettings = openSettings,
            Unknown = unknown,
        };
    }
}
