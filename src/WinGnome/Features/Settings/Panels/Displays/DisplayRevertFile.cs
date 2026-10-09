using System.IO;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>
/// The display change waiting for "Keep changes?", on disk in the profile folder (see <see cref="DisplayChangeFlow"/>
/// for when it is written and deleted). Finding it at start-up means WinGnome stopped during the countdown.
/// </summary>
internal static class DisplayRevertFile
{
    private const string FileName = "display-revert.json";

    /// <summary>Records the change. Returns false (logged) when it can't be written: the change must not go ahead.</summary>
    public static bool Write(string directory, DisplayRevert revert)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = PathIn(directory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, DisplayRevertRecord.Serialize(revert));
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
    /// At start-up: queues the recovery of a change a previous run never confirmed on the shared display queue (off the
    /// UI thread, before any panel change). It is reverted only while it is still showing.
    /// </summary>
    public static void RecoverIfPending(string directory)
    {
        var path = PathIn(directory);
        if (!File.Exists(path))
        {
            return;
        }

        DisplayWork.Enqueue("recover an unconfirmed display change", () =>
        {
            DisplayRevert? record = null;
            var exists = File.Exists(path);
            try
            {
                record = exists ? DisplayRevertRecord.Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable now (locked?): leave it for the next start rather than discard it.
                Log.Warn("Could not read the display revert record", ex);
                return;
            }

            var result = DisplayWork.Flow(directory).Recover(record, exists);
            Log.Info($"Display recovery at start-up: {result}");
        });
    }

    private static string PathIn(string directory) => Path.Combine(directory, FileName);
}
