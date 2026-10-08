using System.Globalization;
using System.Text;

namespace WinGnome.Core.TopBar;

/// <summary>Formats the two-line date header of the calendar popup, GNOME style ("Wednesday" / "8 October 2026").</summary>
public static class CalendarHeader
{
    private const string FallbackPattern = "d MMMM yyyy";

    /// <summary>Full weekday name with an upper-case first letter (some cultures write them in lower case).</summary>
    public static string Weekday(DateTime date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var name = culture.DateTimeFormat.GetDayName(date.DayOfWeek);
        return name.Length == 0 ? name : char.ToUpper(name[0], culture) + name[1..];
    }

    /// <summary>The culture's long date without the weekday, which the header already shows on its own line.</summary>
    public static string LongDate(DateTime date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return date.ToString(HeaderDatePattern(culture.DateTimeFormat.LongDatePattern), culture);
    }

    /// <summary>
    /// Turns a .NET long date pattern into the header pattern: weekday specifiers ("ddd"/"dddd") are removed,
    /// the day loses its leading zero ("dd" → "d", as GNOME writes "8 October"), quoted literals and escapes
    /// are respected, and separators left dangling at either end are trimmed
    /// ("dddd, MMMM dd, yyyy" → "MMMM d, yyyy").
    /// </summary>
    public static string HeaderDatePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var result = new StringBuilder(pattern.Length);
        var quote = '\0';
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
                result.Append(c);
                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    result.Append(c);
                    break;
                case '\\' when i + 1 < pattern.Length:
                    result.Append(c).Append(pattern[++i]);
                    break;
                case 'd':
                    var run = 1;
                    while (i + run < pattern.Length && pattern[i + run] == 'd')
                    {
                        run++;
                    }

                    if (run < 3)
                    {
                        result.Append('d');
                    }

                    i += run - 1;
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }

        var trimmed = result.ToString().Trim(' ', ',', '،', '、', ' ');
        // A one-letter pattern would be read as a standard format ("d" = short date); "%" marks it as custom.
        return trimmed.Length switch
        {
            0 => FallbackPattern,
            1 => "%" + trimmed,
            _ => trimmed,
        };
    }
}
