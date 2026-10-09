using System.Text.Json;

namespace WinGnome.Core.ControlCenter;

/// <summary>One display's mode and position, enough to put it back.</summary>
/// <param name="DeviceName">GDI device name, e.g. <c>\\.\DISPLAY1</c>.</param>
public sealed record DisplaySetting(string DeviceName, int Width, int Height, int RefreshHz, int X, int Y, bool IsPrimary);

/// <summary>An unconfirmed display change: the settings before it and the settings it applied.</summary>
public sealed record DisplayRevert(IReadOnlyList<DisplaySetting> Original, IReadOnlyList<DisplaySetting> Target);

/// <summary>
/// The display change waiting for "Keep changes?". It is written to disk before the change is applied and deleted
/// once the change is kept or reverted, so if WinGnome dies during the countdown the next start finds it and reverts
/// the change, but only while the displays still show what it applied (<see cref="IsStillApplied"/>).
/// </summary>
public static class DisplayRevertRecord
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(DisplayRevert revert) =>
        JsonSerializer.Serialize(new Record { Original = [.. revert.Original], Target = [.. revert.Target] }, Options);

    /// <summary>
    /// The recorded change, dropping entries without a device name or size; null when the text is unusable or either
    /// list is empty.
    /// </summary>
    public static DisplayRevert? Parse(string text)
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

        var original = Valid(record?.Original);
        var target = Valid(record?.Target);
        return original.Count == 0 || target.Count == 0 ? null : new DisplayRevert(original, target);
    }

    /// <summary>
    /// True when every display the change targeted is attached and still in the target mode and position. Anything
    /// else means the change already went away (a reboot, or the user changed the displays since) and reverting would
    /// undo something newer.
    /// </summary>
    public static bool IsStillApplied(DisplayRevert revert, IReadOnlyList<DisplaySetting> current) =>
        revert.Target.All(target => current.Any(c =>
            string.Equals(c.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase)
            && c.Width == target.Width && c.Height == target.Height && c.RefreshHz == target.RefreshHz
            && c.X == target.X && c.Y == target.Y));

    private static List<DisplaySetting> Valid(List<DisplaySetting>? displays) =>
        (displays ?? [])
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.DeviceName) && d.Width > 0 && d.Height > 0)
            .ToList();

    private sealed class Record
    {
        public List<DisplaySetting>? Original { get; set; }

        public List<DisplaySetting>? Target { get; set; }
    }
}
