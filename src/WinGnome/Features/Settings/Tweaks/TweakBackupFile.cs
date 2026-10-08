using System.IO;
using System.Text.Json;
using WinGnome.Core.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Tweaks;

/// <summary>
/// Persists the <see cref="TweakBackup"/> as JSON next to the settings. Writes are atomic, and the previous
/// file is kept as "*.bak" so a bad write can never destroy the only record of a user's original values.
/// </summary>
internal sealed class TweakBackupFile(string directory)
{
    public const string FileName = "tweaks-backup.json";

    private string FilePath { get; } = Path.Combine(directory, FileName);

    /// <summary>Loads the backup. A missing or unreadable file yields an empty backup; unparsable JSON is preserved as "*.corrupt".</summary>
    public TweakBackup Load()
    {
        if (!File.Exists(FilePath))
        {
            return new TweakBackup();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            if (!IsJson(json))
            {
                File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                Log.Warn("Tweak backup was not valid JSON; kept a copy as tweaks-backup.json.corrupt");
            }

            return TweakBackup.FromJson(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read the tweak backup", ex);
            return new TweakBackup();
        }
    }

    /// <summary>Writes the backup atomically. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> on failure.</summary>
    public void Save(TweakBackup backup)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, backup.ToJson());
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, FilePath + ".bak");
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    private static bool IsJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
