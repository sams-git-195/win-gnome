using System.Globalization;
using WinGnome.Core.Settings;

namespace WinGnome.Core.TopBar;

/// <summary>Formats the top bar clock the way GNOME Shell does, e.g. "Wed 8 Oct  14:05".</summary>
public static class ClockFormatter
{
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Formats <paramref name="now"/> using the culture's abbreviated weekday and month names. Weekday and date are
    /// optional; when either is shown, two spaces separate it from the time.
    /// </summary>
    public static string Format(DateTime now, TopBarSettings s, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(culture);

        var info = culture.DateTimeFormat;
        var date = new List<string>(3);
        if (s.ShowWeekday)
        {
            date.Add(info.GetAbbreviatedDayName(now.DayOfWeek));
        }

        if (s.ShowDate)
        {
            date.Add(now.Day.ToString(CultureInfo.InvariantCulture));
            date.Add(info.GetAbbreviatedMonthName(now.Month));
        }

        var pattern = s.ClockStyle == ClockStyle.TwelveHour
            ? (s.ShowSeconds ? "h:mm:ss tt" : "h:mm tt")
            : (s.ShowSeconds ? "HH:mm:ss" : "HH:mm");
        var time = now.ToString(pattern, culture).Trim();

        return date.Count == 0 ? time : string.Join(' ', date) + "  " + time;
    }

    /// <summary>
    /// Time until the clock text can next change: the next second boundary when seconds are shown, otherwise
    /// the next minute boundary. Never less than 50 ms.
    /// </summary>
    public static TimeSpan NextTickDelay(DateTime now, bool showSeconds)
    {
        var interval = showSeconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        var delay = TimeSpan.FromTicks(interval - (now.Ticks % interval));
        return delay < MinimumDelay ? MinimumDelay : delay;
    }
}
