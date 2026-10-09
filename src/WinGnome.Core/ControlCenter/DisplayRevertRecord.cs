using System.Text.Json;

namespace WinGnome.Core.ControlCenter;

/// <summary>One display's mode and position, enough to put it back.</summary>
/// <param name="DeviceName">GDI device name, e.g. <c>\\.\DISPLAY1</c>.</param>
public sealed record DisplaySetting(string DeviceName, int Width, int Height, int RefreshHz, int X, int Y, bool IsPrimary);

/// <summary>
/// The display settings to restore while a change waits for "Keep changes?". It is written to disk before the change
/// is applied and deleted once the change is kept or reverted, so if WinGnome dies during the countdown the next start
/// finds it and reverts the unconfirmed change.
/// </summary>
public static class DisplayRevertRecord
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(IReadOnlyList<DisplaySetting> displays) =>
        JsonSerializer.Serialize(new Record { Displays = [.. displays] }, Options);

    /// <summary>The recorded displays, dropping entries without a device name or size; null when the text is unusable or empty.</summary>
    public static IReadOnlyList<DisplaySetting>? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Record? record;
        try
        {
            record = JsonSerializer.Deserialize<Record>(text, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        var displays = (record?.Displays ?? [])
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.DeviceName) && d.Width > 0 && d.Height > 0)
            .ToList();
        return displays.Count == 0 ? null : displays;
    }

    private sealed class Record
    {
        public List<DisplaySetting>? Displays { get; set; }
    }
}
