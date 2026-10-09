using System.Globalization;
using System.Runtime.InteropServices;
using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.RegionLanguage;

/// <summary>The date and time formats the panel can change.</summary>
internal enum RegionFormat
{
    ShortDate,
    LongDate,
    ShortTime,
    LongTime,
}

/// <summary>One pattern of a format drop-down with the current date or time written in it.</summary>
internal sealed record FormatChoice(string Pattern, string Sample)
{
    public string Label => $"{Sample}    ({Pattern})";
}

/// <summary>A format's current pattern and the patterns its locale offers.</summary>
internal sealed record FormatState(string Current, IReadOnlyList<FormatChoice> Choices);

/// <summary>What Windows holds for the Region &amp; Language panel, read in one go.</summary>
/// <param name="LocaleName">The format locale's name, e.g. "en-GB".</param>
/// <param name="GeoName">The user's region, e.g. "GB".</param>
/// <param name="FirstDay">The first day of the week, or null if Windows reports something outside 0..6.</param>
/// <param name="Formats">Each format's current pattern and choices.</param>
/// <param name="DisplayLanguage">The preferred Windows display language, e.g. "en-GB", or empty.</param>
internal sealed record RegionSnapshot(
    string LocaleName,
    string GeoName,
    DayOfWeek? FirstDay,
    IReadOnlyDictionary<RegionFormat, FormatState> Formats,
    string DisplayLanguage);

/// <summary>
/// Reads and changes the current user's region and formats. Writes are the documented user overrides
/// (<c>SetLocaleInfoW</c> on <c>LOCALE_USER_DEFAULT</c>, <c>SetUserGeoName</c>) followed by the "intl" settings-change
/// broadcast that Windows Settings sends, so other apps re-read them. Writes broadcast, so never call them on the UI thread.
/// </summary>
internal static unsafe class RegionService
{
    private const int MaxLocaleName = 85;
    private const int MaxInfo = 512;

    /// <summary>The LCTYPEs behind every value "Reset to defaults" puts back to the locale's own.</summary>
    private static readonly uint[] ResettableTypes =
    [
        NativeMethods.LOCALE_SSHORTDATE, NativeMethods.LOCALE_SLONGDATE, NativeMethods.LOCALE_SSHORTTIME,
        NativeMethods.LOCALE_STIMEFORMAT, NativeMethods.LOCALE_IFIRSTDAYOFWEEK,
    ];

    /// <summary>The user's format locale name, or an empty string (logged) when Windows doesn't give one.</summary>
    public static string ReadLocaleName()
    {
        var buffer = stackalloc char[MaxLocaleName];
        var length = NativeMethods.GetUserDefaultLocaleName(buffer, MaxLocaleName);
        if (length > 1)
        {
            return new string(buffer, 0, length - 1);
        }

        Log.Warn($"Could not read the format locale (error {Marshal.GetLastPInvokeError()})");
        return "";
    }

    public static RegionSnapshot Read()
    {
        var locale = ReadLocaleName();
        var formats = new Dictionary<RegionFormat, FormatState>();
        foreach (var format in Enum.GetValues<RegionFormat>())
        {
            formats[format] = ReadFormat(locale, format);
        }

        return new RegionSnapshot(locale, ReadGeoName(), ReadFirstDay(locale), formats, ReadDisplayLanguage());
    }

    /// <summary>Every country Windows can use as the region, named in the user's display language, sorted.</summary>
    public static IReadOnlyList<GeoEntry> ReadGeos()
    {
        var names = new List<string>();
        var callback = new NativeMethods.GeoNamesEnumProc((name, _) =>
        {
            if (Marshal.PtrToStringUni(name) is { Length: > 0 } text)
            {
                names.Add(text);
            }

            return true;
        });

        if (!NativeMethods.EnumSystemGeoNames(NativeMethods.GEOCLASS_NATION, callback, 0))
        {
            Log.Warn($"Could not list the regions (error {Marshal.GetLastPInvokeError()})");
        }

        GC.KeepAlive(callback);

        var entries = new List<GeoEntry>();
        foreach (var name in names)
        {
            if (TryDisplayName(name) is { } display)
            {
                entries.Add(new GeoEntry(name, display));
            }
        }

        return GeoList.Sort(entries);
    }

    public static bool SetGeo(string geoName)
    {
        if (!NativeMethods.SetUserGeoName(geoName))
        {
            Log.Warn($"SetUserGeoName(\"{geoName}\") failed (error {Marshal.GetLastPInvokeError()})");
            return false;
        }

        SystemBroadcast.SettingChanged("intl");
        return true;
    }

    public static bool SetFormat(RegionFormat format, string pattern)
    {
        if (!SetLocaleValue(LocaleType(format), pattern))
        {
            return false;
        }

        SystemBroadcast.SettingChanged("intl");
        return true;
    }

    public static bool SetFirstDay(DayOfWeek day)
    {
        var value = FirstDayOfWeek.ToWindows(day).ToString(CultureInfo.InvariantCulture);
        if (!SetLocaleValue(NativeMethods.LOCALE_IFIRSTDAYOFWEEK, value))
        {
            return false;
        }

        SystemBroadcast.SettingChanged("intl");
        return true;
    }

    /// <summary>Writes the locale's own value of every format and the first day of the week. Continues past a failure and reports it.</summary>
    public static bool ResetFormats(string localeName)
    {
        var allWritten = true;
        foreach (var type in ResettableTypes)
        {
            var original = ReadInfo(localeName, type | NativeMethods.LOCALE_NOUSEROVERRIDE);
            allWritten &= original is not null && SetLocaleValue(type, original);
        }

        SystemBroadcast.SettingChanged("intl");
        return allWritten;
    }

    private static bool SetLocaleValue(uint type, string value)
    {
        if (NativeMethods.SetLocaleInfoW(NativeMethods.LOCALE_USER_DEFAULT, type, value))
        {
            return true;
        }

        Log.Warn($"SetLocaleInfo(0x{type:X}, \"{value}\") failed (error {Marshal.GetLastPInvokeError()})");
        return false;
    }

    private static FormatState ReadFormat(string locale, RegionFormat format)
    {
        var current = ReadInfo(locale, LocaleType(format)) ?? "";
        var patterns = RegionFormatChoices.Build(Enumerate(locale, format), current);
        var choices = patterns.Select(p => new FormatChoice(p, Sample(locale, format, p))).ToList();
        return new FormatState(current, choices);
    }

    private static List<string> Enumerate(string locale, RegionFormat format)
    {
        var patterns = new List<string>();
        bool ok;
        if (format is RegionFormat.ShortDate or RegionFormat.LongDate)
        {
            var callback = new NativeMethods.DateFormatsEnumProc((text, _, _) =>
            {
                patterns.Add(Marshal.PtrToStringUni(text) ?? "");
                return true;
            });
            ok = NativeMethods.EnumDateFormatsExEx(callback, locale,
                format == RegionFormat.ShortDate ? NativeMethods.DATE_SHORTDATE : NativeMethods.DATE_LONGDATE, 0);
            GC.KeepAlive(callback);
        }
        else
        {
            var callback = new NativeMethods.TimeFormatsEnumProc((text, _) =>
            {
                patterns.Add(Marshal.PtrToStringUni(text) ?? "");
                return true;
            });
            ok = NativeMethods.EnumTimeFormatsEx(callback, locale,
                format == RegionFormat.ShortTime ? NativeMethods.TIME_NOSECONDS : 0, 0);
            GC.KeepAlive(callback);
        }

        if (!ok)
        {
            Log.Warn($"Could not list the {format} patterns of \"{locale}\" (error {Marshal.GetLastPInvokeError()})");
        }

        return patterns;
    }

    /// <summary>The current date or time written with <paramref name="pattern"/>, or the pattern itself if Windows can't format it.</summary>
    private static string Sample(string locale, RegionFormat format, string pattern)
    {
        var buffer = stackalloc char[MaxInfo];
        var length = format is RegionFormat.ShortDate or RegionFormat.LongDate
            ? NativeMethods.GetDateFormatEx(locale, 0, 0, pattern, buffer, MaxInfo, 0)
            : NativeMethods.GetTimeFormatEx(locale, 0, 0, pattern, buffer, MaxInfo);
        return length > 1 ? new string(buffer, 0, length - 1) : pattern;
    }

    private static DayOfWeek? ReadFirstDay(string locale) =>
        FirstDayOfWeek.FromWindows(ReadInfo(locale, NativeMethods.LOCALE_IFIRSTDAYOFWEEK));

    private static string ReadGeoName()
    {
        var buffer = stackalloc char[MaxLocaleName];
        var length = NativeMethods.GetUserDefaultGeoName(buffer, MaxLocaleName);
        return length > 1 ? new string(buffer, 0, length - 1) : "";
    }

    private static string ReadDisplayLanguage()
    {
        var buffer = stackalloc char[MaxInfo];
        var size = (uint)MaxInfo;
        if (!NativeMethods.GetUserPreferredUILanguages(NativeMethods.MUI_LANGUAGE_NAME, out _, buffer, ref size))
        {
            Log.Warn($"Could not read the display language (error {Marshal.GetLastPInvokeError()})");
            return "";
        }

        // The first name of the NUL-separated list: the string constructor stops at the first terminator.
        return new string(buffer);
    }

    private static string? ReadInfo(string locale, uint type)
    {
        var buffer = stackalloc char[MaxInfo];
        var length = NativeMethods.GetLocaleInfoEx(locale, type, buffer, MaxInfo);
        if (length > 0)
        {
            return new string(buffer, 0, length - 1);
        }

        Log.Warn($"GetLocaleInfoEx(\"{locale}\", 0x{type:X}) failed (error {Marshal.GetLastPInvokeError()})");
        return null;
    }

    private static uint LocaleType(RegionFormat format) => format switch
    {
        RegionFormat.ShortDate => NativeMethods.LOCALE_SSHORTDATE,
        RegionFormat.LongDate => NativeMethods.LOCALE_SLONGDATE,
        RegionFormat.ShortTime => NativeMethods.LOCALE_SSHORTTIME,
        _ => NativeMethods.LOCALE_STIMEFORMAT,
    };

    private static string? TryDisplayName(string geoName)
    {
        try
        {
            return new RegionInfo(geoName).DisplayName;
        }
        catch (ArgumentException)
        {
            // A geographical name .NET has no region for (not an ISO 3166 code); not offered rather than shown as a bare code.
            return null;
        }
    }

    /// <summary>A locale or language name in words ("English (United Kingdom)"), or the bare name if .NET doesn't know it.</summary>
    public static string CultureName(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return name;
        }
    }
}
