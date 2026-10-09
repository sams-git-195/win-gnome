using System.IO;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>
/// The display settings to go back to while a change waits for "Keep changes?", on disk in the profile folder. It is
/// written before the change is applied and deleted when the change is kept or reverted; finding it at start-up means
/// WinGnome stopped during the countdown, and the unconfirmed change is reverted then.
/// </summary>
internal static class DisplayRevertFile
{
    private const string FileName = "display-revert.json";

    /// <summary>Records <paramref name="snapshot"/>. Returns false (logged) when it can't be written: the change must not go ahead.</summary>
    public static bool Write(string directory, IReadOnlyList<DisplaySetting> snapshot)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = PathIn(directory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, DisplayRevertRecord.Serialize(snapshot));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not record the display settings to revert to; the change was not applied", ex);
            return false;
        }
    }

    public static void Delete(string directory)
    {
        try
        {
            File.Delete(PathIn(directory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not delete the display revert record", ex);
        }
    }

    /// <summary>
    /// At start-up: when a display change was never confirmed (WinGnome stopped during the countdown), restores the
    /// recorded settings and deletes the record. A record that can't be read is deleted.
    /// </summary>
    public static void RecoverIfPending(string directory)
    {
        var path = PathIn(directory);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if (DisplayRevertRecord.Parse(File.ReadAllText(path)) is { } snapshot)
            {
                Log.Info("A display change was never confirmed; restoring the previous display settings");
                if (!DisplayService.Restore(snapshot))
                {
                    Log.Warn("Could not restore the previous display settings");
                }
            }
            else
            {
                Log.Warn("The display revert record could not be read; it is discarded");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read the display revert record", ex);
        }

        Delete(directory);
    }

    private static string PathIn(string directory) => Path.Combine(directory, FileName);
}
