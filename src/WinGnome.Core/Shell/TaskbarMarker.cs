using System.Text.Json;

namespace WinGnome.Core.Shell;

/// <summary>
/// The <c>taskbar.state</c> restore marker: the taskbar's original auto-hide state, plus the taskbar windows
/// WinGnome gave an empty window region (recorded before it does, so a force-killed instance's next start can undo it).
/// Files written before regions existed hold only <see cref="WasAutoHide"/> and still load; older builds ignore the
/// list.
/// </summary>
public sealed class TaskbarMarker
{
    public bool WasAutoHide { get; init; }

    /// <summary>Handles (HWNDs) of Explorer taskbar windows that were given an empty region.</summary>
    public IReadOnlyList<long> EmptiedRegions { get; init; } = [];

    /// <summary>This marker with <paramref name="handle"/> added to <see cref="EmptiedRegions"/> (unchanged when already there).</summary>
    public TaskbarMarker WithEmptiedRegion(long handle) =>
        EmptiedRegions.Contains(handle)
            ? this
            : new TaskbarMarker { WasAutoHide = WasAutoHide, EmptiedRegions = [.. EmptiedRegions, handle] };

    /// <summary>This marker with exactly <paramref name="handles"/> recorded (duplicates dropped).</summary>
    public TaskbarMarker WithEmptiedRegions(IEnumerable<long> handles) =>
        new() { WasAutoHide = WasAutoHide, EmptiedRegions = [.. handles.Distinct()] };

    public string Serialize() => JsonSerializer.Serialize(new RawMarker
    {
        WasAutoHide = WasAutoHide,
        EmptiedRegions = [.. EmptiedRegions],
    });

    /// <summary>The marker in <paramref name="json"/>, or null when it is empty, not JSON, or not a marker.</summary>
    public static TaskbarMarker? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<RawMarker>(json);
            return raw is null
                ? null
                : new TaskbarMarker { WasAutoHide = raw.WasAutoHide, EmptiedRegions = raw.EmptiedRegions ?? [] };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Tolerates a missing or null list, which is what a marker from before regions looks like.
    private sealed class RawMarker
    {
        public bool WasAutoHide { get; set; }

        public long[]? EmptiedRegions { get; set; }
    }
}
