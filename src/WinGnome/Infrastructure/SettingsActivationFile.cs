using System.IO;
using WinGnome.Core.Settings;

namespace WinGnome.Infrastructure;

/// <summary>
/// Carries a second launch's <c>--settings-panel</c> to the running instance through a small file in the shared
/// settings folder (see <see cref="SettingsActivationRequest"/>). The activation event only says "show settings".
/// </summary>
internal static class SettingsActivationFile
{
    /// <summary>Second launch: records which panel to show, before signalling the running instance.</summary>
    public static void Write(string settingsDirectory, string? panelId)
    {
        var path = Path.Combine(settingsDirectory, SettingsActivationRequest.FileName);
        try
        {
            File.WriteAllText(path, SettingsActivationRequest.Format(panelId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The running instance still opens its settings window, just not at the panel.
            Log.Warn($"Could not write the settings request {path}", ex);
        }
    }

    /// <summary>Owning instance at start-up: deletes a stale request no running instance read.</summary>
    public static void Discard(string settingsDirectory)
    {
        var path = Path.Combine(settingsDirectory, SettingsActivationRequest.FileName);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not delete the stale settings request {path}", ex);
        }
    }
    /// <summary>Running instance: the requested panel id, or null; the file is deleted so a later launch starts clean.</summary>
    public static string? Take(string settingsDirectory)
    {
        var path = Path.Combine(settingsDirectory, SettingsActivationRequest.FileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var content = File.ReadAllText(path);
            File.Delete(path);
            return SettingsActivationRequest.Parse(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read the settings request {path}", ex);
            return null;
        }
    }
}
