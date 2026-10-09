using System.Globalization;
using System.Text.Json;
using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

/// <summary>
/// One work area WinGnome set directly because Explorer had not applied a strip it granted. <paramref name="Owner"/>
/// is the HWND of the bar that asked for it, <paramref name="Key"/> the monitor's GDI device name,
/// <paramref name="Bounds"/> that monitor's rectangle when the change was made (a record whose bounds no longer
/// match is stale: a mode change reset the work area anyway), <paramref name="Original"/> the work area before it and
/// <paramref name="Applied"/> the one WinGnome wrote.
/// </summary>
public readonly record struct WorkAreaRecord(long Owner, string Key, PixelRect Bounds, PixelRect Original, PixelRect Applied);

/// <summary>
/// The parsed <c>workareas.state</c> marker: no records when the file is absent or empty, <paramref name="Unreadable"/>
/// when it exists but cannot be trusted. An unreadable marker is never read as "no records" — the shrinks it
/// described may still be in effect, and the caller has a separate repair path for that case.
/// </summary>
public readonly record struct WorkAreaFile(IReadOnlyList<WorkAreaRecord> Records, bool Unreadable);

/// <summary>Reads and writes the work-area marker. Rectangles are stored as <c>left,top,right,bottom</c>.</summary>
public static class WorkAreaState
{
    private sealed record MarkerEntry(long Owner, string Key, string Bounds, string Original, string Applied);

    private sealed record MarkerFile(List<MarkerEntry>? Records);

    public static WorkAreaFile Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new([], false);
        }

        try
        {
            if (JsonSerializer.Deserialize<MarkerFile>(json) is not { Records: { } entries })
            {
                return new([], true);
            }

            var records = new List<WorkAreaRecord>(entries.Count);
            foreach (var entry in entries)
            {
                // All or nothing: a half-read marker could strand the shrinks it dropped.
                if (entry is null || string.IsNullOrEmpty(entry.Key)
                    || !TryParseRect(entry.Bounds, out var bounds)
                    || !TryParseRect(entry.Original, out var original)
                    || !TryParseRect(entry.Applied, out var applied))
                {
                    return new([], true);
                }

                records.Add(new WorkAreaRecord(entry.Owner, entry.Key, bounds, original, applied));
            }

            return new(records, false);
        }
        catch (JsonException)
        {
            return new([], true);
        }
    }

    public static string Serialize(IReadOnlyList<WorkAreaRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var entries = records
            .Select(r => new MarkerEntry(r.Owner, r.Key, Format(r.Bounds), Format(r.Original), Format(r.Applied)))
            .ToList();
        return JsonSerializer.Serialize(new MarkerFile(entries));
    }

    private static string Format(PixelRect rect) => string.Join(',',
        rect.Left.ToString(CultureInfo.InvariantCulture),
        rect.Top.ToString(CultureInfo.InvariantCulture),
        rect.Right.ToString(CultureInfo.InvariantCulture),
        rect.Bottom.ToString(CultureInfo.InvariantCulture));

    private static bool TryParseRect(string? text, out PixelRect rect)
    {
        rect = default;
        var parts = text?.Split(',') ?? [];
        if (parts.Length != 4)
        {
            return false;
        }

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        rect = new PixelRect(values[0], values[1], values[2], values[3]);
        return true;
    }
}
