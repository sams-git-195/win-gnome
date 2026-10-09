using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>One entry of a timeout drop-down: the value in seconds (0 = never) and its label.</summary>
public sealed record TimeoutChoice(int Seconds, string Label);

/// <summary>GNOME's Power panel timeouts and how they are labelled.</summary>
public static class PowerTimeouts
{
    /// <summary>GNOME's "Screen Blank" choices in seconds; 0 is Never.</summary>
    public static IReadOnlyList<int> ScreenBlank { get; } = [60, 120, 180, 240, 300, 480, 600, 720, 900, 0];

    /// <summary>GNOME's "Automatic Suspend" delays in seconds; 0 is Never.</summary>
    public static IReadOnlyList<int> Suspend { get; } = [900, 1200, 1500, 1800, 2700, 3600, 4800, 5400, 6000, 7200, 0];

    /// <summary>
    /// The drop-down entries for <paramref name="presets"/>. A <paramref name="current"/> value that isn't a preset
    /// (set in Windows Settings or by policy) is added in order so the drop-down can show it; Never always comes last.
    /// </summary>
    public static IReadOnlyList<TimeoutChoice> Choices(IReadOnlyList<int> presets, int current)
    {
        var values = presets.Where(p => p > 0).ToList();
        if (current > 0 && !values.Contains(current))
        {
            values.Add(current);
            values.Sort();
        }

        if (presets.Contains(0) || current == 0)
        {
            values.Add(0);
        }

        return values.Select(v => new TimeoutChoice(v, Label(v))).ToList();
    }

    /// <summary>"Never", "30 seconds", "5 minutes", "1 hour 20 minutes".</summary>
    public static string Label(int seconds)
    {
        if (seconds <= 0)
        {
            return "Never";
        }

        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var rest = seconds % 60;
        var parts = new List<string>(2);
        if (hours > 0)
        {
            parts.Add(Unit(hours, "hour"));
        }

        if (minutes > 0)
        {
            parts.Add(Unit(minutes, "minute"));
        }

        if (rest > 0 && hours == 0)
        {
            parts.Add(Unit(rest, "second"));
        }

        return string.Join(' ', parts);
    }

    private static string Unit(int count, string unit) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? "" : "s");
}
