using System.IO;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Displays;

/// <summary>
/// The display change waiting for "Keep changes?", on disk in the profile folder. It is written before the change is
/// applied and deleted when the change is kept or successfully reverted; finding it at start-up means WinGnome stopped
/// during the countdown, and the unconfirmed change is reverted then if it is still showing.
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
    /// At start-up, on a worker thread: when a display change was never confirmed (WinGnome stopped during the
    /// countdown) and the displays still show it, tests and restores the previous settings. The record is deleted when
    /// there is nothing to revert or the revert worked, and kept for the next start when it failed.
    /// </summary>
    public static void RecoverIfPending(string directory)
    {
        var path = PathIn(directory);
        if (!File.Exists(path))
        {
            return;
        }

        DisplayRevert? revert;
        try
        {
            revert = DisplayRevertRecord.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read the display revert record", ex);
            return;
        }

        if (revert is null)
        {
            Log.Warn("The display revert record could not be understood; it is discarded");
            Delete(directory);
            return;
        }

        if (!DisplayRevertRecord.IsStillApplied(revert, DisplayService.Current()))
        {
            Log.Info("An unconfirmed display change is no longer showing; nothing to revert");
            Delete(directory);
            return;
        }

        var present = DisplayService.Current().Select(d => d.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var original = revert.Original.Where(o => present.Contains(o.DeviceName)).ToList();
        if (original.Count == 0 || !DisplayService.Test(original))
        {
            Log.Warn("Windows no longer accepts the previous display settings; the unconfirmed change stays");
            Delete(directory);
            return;
        }

        Log.Info("A display change was never confirmed; restoring the previous display settings");
        if (DisplayService.Revert(original))
        {
            Delete(directory);
        }
    }

    private static string PathIn(string directory) => Path.Combine(directory, FileName);
}
