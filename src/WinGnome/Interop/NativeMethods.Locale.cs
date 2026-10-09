using System.Runtime.InteropServices;

namespace WinGnome.Interop;

/// <summary>Locale, date and time formats and the user's region for the Region &amp; Language panel (kernel32.dll).</summary>
internal static partial class NativeMethods
{
    /// <summary>The current user's locale: <c>SetLocaleInfoW</c> then writes the user's overrides.</summary>
    public const uint LOCALE_USER_DEFAULT = 0x0400;

    /// <summary>Or-ed into an LCTYPE to read the locale's own value instead of the user's override.</summary>
    public const uint LOCALE_NOUSEROVERRIDE = 0x80000000;

    public const uint LOCALE_SSHORTDATE = 0x001F;
    public const uint LOCALE_SLONGDATE = 0x0020;
    public const uint LOCALE_SSHORTTIME = 0x0079;
    public const uint LOCALE_STIMEFORMAT = 0x1003;
    public const uint LOCALE_IFIRSTDAYOFWEEK = 0x100C;

    public const uint DATE_SHORTDATE = 0x0001;
    public const uint DATE_LONGDATE = 0x0002;
    public const uint TIME_NOSECONDS = 0x0002;
    public const uint GEOCLASS_NATION = 16;
    public const uint MUI_LANGUAGE_NAME = 0x0008;

    /// <summary>Called by <c>EnumDateFormatsExEx</c> with each pattern; return false to stop.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate bool DateFormatsEnumProc(nint format, uint calendarId, nint lParam);

    /// <summary>Called by <c>EnumTimeFormatsEx</c> with each pattern; return false to stop.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate bool TimeFormatsEnumProc(nint format, nint lParam);

    /// <summary>Called by <c>EnumSystemGeoNames</c> with each geographical name; return false to stop.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate bool GeoNamesEnumProc(nint geoName, nint lParam);

    // Delegate parameters are not supported by LibraryImport, so the enumerations stay DllImport.
#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDateFormatsExEx(DateFormatsEnumProc callback, string localeName, uint flags, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumTimeFormatsEx(TimeFormatsEnumProc callback, string localeName, uint flags, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumSystemGeoNames(uint geoClass, GeoNamesEnumProc callback, nint lParam);
#pragma warning restore SYSLIB1054

    /// <summary>Returns the length including the terminator, or 0 on failure.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial int GetUserDefaultLocaleName(char* localeName, int cchLocaleName);

    /// <summary>Returns the length including the terminator, or 0 on failure. <paramref name="lcType"/> may include <see cref="LOCALE_NOUSEROVERRIDE"/>.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetLocaleInfoEx(string localeName, uint lcType, char* data, int cchData);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool SetLocaleInfoW(uint locale, uint lcType, string lcData);

    /// <summary>Formats the current date with <paramref name="format"/>; a null date means now. Returns the length including the terminator, or 0.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetDateFormatEx(string localeName, uint flags, nint date, string format, char* dateStr, int cchDate, nint calendar);

    /// <summary>Formats the current time with <paramref name="format"/>; a null time means now. Returns the length including the terminator, or 0.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int GetTimeFormatEx(string localeName, uint flags, nint time, string format, char* timeStr, int cchTime);

    /// <summary>Returns the length including the terminator, or 0 on failure.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial int GetUserDefaultGeoName(char* geoName, int geoNameCount);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool SetUserGeoName(string geoName);

    /// <summary>Fills <paramref name="languagesMultiString"/> with the preferred UI language names, each terminated by NUL and the list by two.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetUserPreferredUILanguages(uint flags, out uint numLanguages, char* languagesMultiString, ref uint cchLanguagesMultiString);
}
