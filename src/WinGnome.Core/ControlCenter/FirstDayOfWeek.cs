using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>
/// Windows' <c>LOCALE_IFIRSTDAYOFWEEK</c>: 0 is Monday through 6 for Sunday, unlike <see cref="DayOfWeek"/>
/// where Sunday is 0.
/// </summary>
public static class FirstDayOfWeek
{
    /// <summary>The days in Windows' order, Monday first.</summary>
    public static IReadOnlyList<DayOfWeek> All { get; } =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>The day for a Windows value 0..6, or null for anything else.</summary>
    public static DayOfWeek? FromWindows(int value) => value is >= 0 and < 7 ? All[value] : null;

    /// <summary>The day for the text <c>GetLocaleInfoEx</c> returns ("0".."6"), or null when it isn't one of those.</summary>
    public static DayOfWeek? FromWindows(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? FromWindows(value) : null;

    /// <summary>The Windows value 0 (Monday) to 6 (Sunday) for <paramref name="day"/>.</summary>
    public static int ToWindows(DayOfWeek day) => day == DayOfWeek.Sunday ? 6 : (int)day - 1;
}
