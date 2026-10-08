using System.Text.Json;

namespace WinGnome.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to a JSON file. Writes are atomic
/// (temp file + replace) and a corrupt file is preserved as "*.corrupt" rather than lost.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    public SettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary>Default location: %APPDATA%\WinGnome.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinGnome");

    /// <summary>
    /// Loads settings, falling back to defaults when the file is missing or is not valid settings JSON
    /// (a corrupt file is first copied to "*.corrupt"). I/O errors such as a locked file are not caught:
    /// returning defaults then would let the next save overwrite settings that were never read.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings().Normalize();
        }

        try
        {
            return SettingsSerializer.Deserialize(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            PreserveCorruptFile();
            return new AppSettings().Normalize();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, SettingsSerializer.Serialize(settings));
        File.Move(temp, FilePath, overwrite: true);
    }

    private void PreserveCorruptFile()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only; defaults are still returned.
        }
    }
}
