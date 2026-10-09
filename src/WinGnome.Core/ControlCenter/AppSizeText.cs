using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>The size an installer reports (<c>EstimatedSize</c>, in KB) as Windows Settings shows it.</summary>
public static class AppSizeText
{
    private const long KbPerMb = 1024;
    private const long KbPerGb = 1024 * 1024;

    /// <summary>"512 KB", "64.9 MB" or "1.21 GB" (binary units, as Windows uses); null when the size is unknown or not positive.</summary>
    public static string? Format(long? sizeKb, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (sizeKb is not { } kb || kb <= 0)
        {
            return null;
        }

        if (kb < KbPerMb)
        {
            return kb.ToString("0", culture) + " KB";
        }

        if (kb < KbPerGb)
        {
            return ((double)kb / KbPerMb).ToString("0.0", culture) + " MB";
        }

        return ((double)kb / KbPerGb).ToString("0.00", culture) + " GB";
    }
}

/// <summary>An Uninstall key's <c>InstallDate</c> (<c>yyyyMMdd</c>).</summary>
public static class InstallDateText
{
    /// <summary>The date, or null when the text is missing or not exactly <c>yyyyMMdd</c> with a real date.</summary>
    public static DateOnly? Parse(string? raw) =>
        raw is not null && DateOnly.TryParseExact(raw.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>The date in <paramref name="culture"/>'s short date format, or null when <paramref name="raw"/> doesn't parse.</summary>
    public static string? Format(string? raw, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Parse(raw)?.ToString("d", culture);
    }
}
