using System.Globalization;
using System.Text;

namespace WinGnome.Core.ControlCenter;

/// <summary>Text for the About and Sound panels, built from raw values Windows reports.</summary>
public static class SystemInfoText
{
    /// <summary>First build of Windows 11. Its registry ProductName still says "Windows 10".</summary>
    private const int FirstWindows11Build = 22000;

    private static readonly string[] BinaryUnits = ["KiB", "MiB", "GiB", "TiB", "PiB"];
    private static readonly string[] DecimalUnits = ["kB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// "Windows 11 Home 25H2 (build 26200.6899)" from the CurrentVersion registry values. Windows 11 keeps
    /// "Windows 10" in ProductName, so the name is corrected from the build number. Missing parts are left out.
    /// </summary>
    public static string WindowsVersion(string? productName, string? displayVersion, int build, int ubr)
    {
        var name = string.IsNullOrWhiteSpace(productName) ? "Windows" : productName.Trim();
        if (build >= FirstWindows11Build && name.StartsWith("Windows 10", StringComparison.Ordinal))
        {
            name = "Windows 11" + name["Windows 10".Length..];
        }

        var text = new StringBuilder(name);
        if (!string.IsNullOrWhiteSpace(displayVersion))
        {
            text.Append(' ').Append(displayVersion.Trim());
        }

        text.Append(CultureInfo.InvariantCulture, $" (build {build}");
        if (ubr > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $".{ubr}");
        }

        return text.Append(')').ToString();
    }

    /// <summary>Memory size in binary units with one decimal, as GNOME shows it: "16.0 GiB".</summary>
    public static string Memory(ulong bytes, CultureInfo culture) => Size(bytes, 1024, BinaryUnits, culture);

    /// <summary>Disk size in decimal units with one decimal, as GNOME (and drive makers) show it: "512.1 GB".</summary>
    public static string DiskCapacity(ulong bytes, CultureInfo culture) => Size(bytes, 1000, DecimalUnits, culture);

    /// <summary>The processor's registry name, trimmed, with (R) and (TM) shown as symbols.</summary>
    public static string Processor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Unknown";
        }

        var name = raw.Trim().Replace("(R)", "®", StringComparison.OrdinalIgnoreCase).Replace("(TM)", "™", StringComparison.OrdinalIgnoreCase);
        while (name.Contains("  ", StringComparison.Ordinal))
        {
            name = name.Replace("  ", " ", StringComparison.Ordinal);
        }

        return name;
    }

    /// <summary>
    /// The name of an app's audio session: its own display name unless that is empty or an unresolved resource
    /// reference ("@%SystemRoot%\..."), then <paramref name="fallback"/> (usually the program's description).
    /// </summary>
    public static string AudioSessionName(string? displayName, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.TrimStart().StartsWith('@'))
        {
            return displayName.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Unknown app" : fallback.Trim();
    }

    private static string Size(ulong bytes, double step, string[] units, CultureInfo culture)
    {
        if (bytes < step)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        }

        var value = bytes / step;
        var unit = 0;
        while (value >= step && unit < units.Length - 1)
        {
            value /= step;
            unit++;
        }

        return value.ToString("0.0", culture) + " " + units[unit];
    }
}
