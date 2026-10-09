using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>A display mode Windows reports for a display.</summary>
public readonly record struct DisplayMode(int Width, int Height, int RefreshHz);

/// <summary>A display resolution in physical pixels.</summary>
public readonly record struct Resolution(int Width, int Height);

/// <summary>Turns a display's raw mode list into the Displays panel's resolution and refresh-rate choices.</summary>
public static class DisplayModes
{
    // Named aspect ratios, checked before falling back to the reduced fraction. 1366 × 768 and 2560 × 1080 are not
    // exactly 16:9 and 21:9 but are sold as such.
    private static readonly (int W, int H, string Name)[] KnownRatios =
    [
        (16, 9, "16∶9"), (16, 10, "16∶10"), (4, 3, "4∶3"), (5, 4, "5∶4"), (3, 2, "3∶2"), (21, 9, "21∶9"), (32, 9, "32∶9"),
    ];

    private const double RatioTolerance = 0.02;

    // 21:9 panels range from 2.33 (2560 × 1080 is 2.37, 3440 × 1440 is 2.39); allow the wider tolerance for that name only.
    private const double UltrawideTolerance = 0.03;

    /// <summary>Distinct resolutions, largest (by pixel count, then width) first.</summary>
    public static IReadOnlyList<Resolution> Resolutions(IEnumerable<DisplayMode> modes) =>
        modes.Select(m => new Resolution(m.Width, m.Height))
            .Distinct()
            .OrderByDescending(r => (long)r.Width * r.Height)
            .ThenByDescending(r => r.Width)
            .ToList();

    /// <summary>Distinct refresh rates available at <paramref name="resolution"/>, fastest first.</summary>
    public static IReadOnlyList<int> RefreshRates(IEnumerable<DisplayMode> modes, Resolution resolution) =>
        modes.Where(m => m.Width == resolution.Width && m.Height == resolution.Height)
            .Select(m => m.RefreshHz)
            .Distinct()
            .OrderDescending()
            .ToList();

    /// <summary>The refresh rate to use after switching resolution: the current one when available, else the fastest.</summary>
    public static int PickRefresh(IReadOnlyList<int> rates, int current) =>
        rates.Count == 0 || rates.Contains(current) ? current : rates.Max();

    /// <summary>"16∶9" and friends; the reduced fraction for unusual sizes; empty for an empty size.</summary>
    public static string AspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return "";
        }

        var ratio = (double)width / height;
        foreach (var (w, h, name) in KnownRatios)
        {
            var known = (double)w / h;
            var tolerance = w == 21 ? UltrawideTolerance : RatioTolerance;
            if (Math.Abs(ratio - known) / known <= tolerance)
            {
                return name;
            }
        }

        var divisor = Gcd(width, height);
        return string.Create(CultureInfo.InvariantCulture, $"{width / divisor}∶{height / divisor}");
    }

    /// <summary>"1920 × 1080 (16∶9)".</summary>
    public static string Label(Resolution resolution) =>
        string.Create(CultureInfo.InvariantCulture, $"{resolution.Width} × {resolution.Height} ({AspectRatio(resolution.Width, resolution.Height)})");

    /// <summary>Scale in percent from a monitor's effective DPI (96 = 100 %); an unknown DPI reads as 100 %.</summary>
    public static int ScalePercent(int dpi) => dpi <= 0 ? 100 : (int)Math.Round(dpi * 100 / 96.0);

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
