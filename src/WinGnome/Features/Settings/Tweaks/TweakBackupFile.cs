using System.IO;
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

    /// <summary>Set when the file exists but could not be read (or preserved); saving would overwrite originals we never loaded.</summary>
    private bool _saveBlocked;

    private string FilePath { get; } = Path.Combine(directory, FileName);

    /// <summary>
    /// Loads the backup. A missing file yields an empty backup. A file that is not valid JSON, or that holds records
    /// that cannot be loaded, is copied to "*.corrupt" first so the original values stay recoverable. When the file
    /// cannot be read (or a damaged one cannot be copied), the result is empty and <see cref="Save"/> refuses to run.
    /// </summary>
    public TweakBackup Load()
    {
        _saveBlocked = false;
        if (!File.Exists(FilePath))
        {
            return new TweakBackup();
        }

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _saveBlocked = true;
            Log.Warn("Could not read the tweak backup; tweaks cannot be changed until it is readable", ex);
            return new TweakBackup();
        }

        var backup = TweakBackup.FromJson(json, out var complete);
        if (!complete)
        {
            try
            {
                File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                Log.Warn($"Tweak backup was damaged ({backup.TweakIds.Count} record(s) usable); kept a copy as {FileName}.corrupt");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _saveBlocked = true;
                Log.Warn("Tweak backup was damaged and could not be copied; tweaks cannot be changed this session", ex);
            }
        }

        return backup;
    }

    /// <summary>Writes the backup atomically. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> on failure.</summary>
    public void Save(TweakBackup backup)
    {
        if (_saveBlocked)
        {
            throw new IOException($"{FileName} could not be loaded, so it is not overwritten.");
        }

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
}
