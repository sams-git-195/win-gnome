namespace WinGnome.Core.ControlCenter;

/// <summary>One entry of an AutoPlay drop-down.</summary>
/// <param name="HandlerId">The handler's registry name (for example <c>MSOpenFolder</c>), which is what is stored.</param>
/// <param name="Label">What the drop-down shows.</param>
public sealed record AutoplayChoice(string HandlerId, string Label)
{
    /// <summary>The label: it is what a drop-down item's accessible name falls back to, so it must read as text.</summary>
    public override string ToString() => Label;
}

/// <summary>One media type of the Removable Media panel and what Windows does when it appears.</summary>
/// <param name="EventId">The AutoPlay event's registry name (for example <c>StorageOnArrival</c>).</param>
/// <param name="Label">The media type as shown ("Removable drive").</param>
/// <param name="Choices">The drop-down: the three standard actions first, then installed handlers.</param>
/// <param name="Selected">The current choice; always one of <paramref name="Choices"/>.</param>
public sealed record AutoplayRow(string EventId, string Label, IReadOnlyList<AutoplayChoice> Choices, AutoplayChoice Selected);

/// <summary>A value to store as the default value of a key under HKCU <c>...\Explorer\AutoplayHandlers</c>.</summary>
/// <param name="SubKey">The key, relative to <c>AutoplayHandlers</c>.</param>
/// <param name="Handler">The handler name to store.</param>
public sealed record AutoplayWrite(string SubKey, string Handler);

/// <summary>
/// The AutoPlay settings model of Windows' "Removable media" page: which media types to show, which handlers each
/// offers, and which registry values record a choice. Reading the registry is the app's job; this decides what the
/// values mean.
/// </summary>
public static class AutoplayModel
{
    /// <summary>"Ask what to do": Windows shows its AutoPlay prompt.</summary>
    public const string PromptEachTime = "MSPromptEachTime";

    /// <summary>"Do nothing".</summary>
    public const string TakeNoAction = "MSTakeNoAction";

    /// <summary>"Open folder".</summary>
    public const string OpenFolder = "MSOpenFolder";

    /// <summary>The two media types that are always listed (Windows Settings always has them).</summary>
    private static readonly string[] AlwaysShown = ["StorageOnArrival", "ShowPicturesOnArrival"];

    private static readonly AutoplayChoice[] Specials =
    [
        new(PromptEachTime, "Ask what to do"),
        new(TakeNoAction, "Do nothing"),
        new(OpenFolder, "Open folder"),
    ];

    // Event names Windows registers under HKLM AutoplayHandlers\EventHandlers that have a plain-language name.
    // Anything else (vendor players such as "Rio600Arrival", "WPD", ...) is hidden.
    private static readonly Dictionary<string, string> EventLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["StorageOnArrival"] = "Removable drive",
        ["ShowPicturesOnArrival"] = "Memory card",
        ["PlayDVDMovieOnArrival"] = "DVD movie",
        ["PlayBluRayOnArrival"] = "Blu-ray disc movie",
        ["PlayCDAudioOnArrival"] = "Audio CD",
        ["PlayEnhancedCDOnArrival"] = "Enhanced audio CD",
        ["PlayDVDAudioOnArrival"] = "DVD audio",
        ["PlayVideoCDMovieOnArrival"] = "Video CD",
        ["PlaySuperVideoCDMovieOnArrival"] = "Super Video CD",
        ["PlayMusicFilesOnArrival"] = "Music files",
        ["PlayVideoFilesOnArrival"] = "Video files",
        ["MixedContentOnArrival"] = "Mixed content",
        ["UnknownContentOnArrival"] = "Unknown content",
        ["CameraMemoryOnArrival"] = "Camera",
        ["VideoCameraArrival"] = "Video camera",
        ["HandleCDBurningOnArrival"] = "Blank CD",
        ["HandleDVDBurningOnArrival"] = "Blank DVD",
        ["HandleBDBurningOnArrival"] = "Blank Blu-ray disc",
    };

    /// <summary>The media type's display name, or null for an event Windows Settings doesn't list.</summary>
    public static string? EventLabel(string eventId) => EventLabels.GetValueOrDefault(eventId);

    /// <summary>
    /// Handler names from both hives. A per-user handler (an app that registered itself under HKCU) overrides an
    /// HKLM handler of the same id.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MergeNames(
        IReadOnlyDictionary<string, string> machineNames, IReadOnlyDictionary<string, string> userNames)
    {
        var merged = new Dictionary<string, string>(machineNames, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in userNames)
        {
            merged[id] = name;
        }

        return merged;
    }

    /// <summary>
    /// The rows to show: the always-listed media types plus every known event that has at least one named handler,
    /// the two always-listed first and the rest by name.
    /// </summary>
    /// <param name="events">Event names found in either hive's <c>EventHandlers</c>.</param>
    /// <param name="handlersByEvent">Handler ids registered for each event (both hives, any order).</param>
    /// <param name="names">Display name of each handler id (see <see cref="MergeNames"/>); an id without one is hidden.</param>
    /// <param name="chosen">The handler id chosen for each event; absent means "Ask what to do".</param>
    public static IReadOnlyList<AutoplayRow> Build(
        IEnumerable<string> events,
        IReadOnlyDictionary<string, IReadOnlyList<string>> handlersByEvent,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string> chosen)
    {
        var ids = new HashSet<string>(AlwaysShown, StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(events);

        var rows = new List<AutoplayRow>();
        foreach (var id in ids)
        {
            if (EventLabel(id) is not { } label)
            {
                continue;
            }

            var handlers = handlersByEvent.TryGetValue(id, out var found) ? found : [];
            var choices = ChoicesFor(handlers, names);
            if (choices.Count == Specials.Length && !AlwaysShown.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                // Nothing to pick beyond the standard actions, so Windows Settings doesn't list this media type.
                continue;
            }

            // SelectedFor may add the chosen handler to the list, so it runs before the list is copied.
            var selected = SelectedFor(chosen.GetValueOrDefault(id), choices, names);
            rows.Add(new AutoplayRow(id, label, choices.ToList(), selected));
        }

        return rows
            .OrderBy(r => RankOf(r.EventId))
            .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>0 and 1 for the always-listed media types in their fixed order; everything else sorts after them by name.</summary>
    private static int RankOf(string eventId)
    {
        var index = Array.FindIndex(AlwaysShown, a => a.Equals(eventId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? AlwaysShown.Length : index;
    }

    /// <summary>
    /// The values Windows Settings writes to record a choice: both the user's chosen handler and the default
    /// selection, so Explorer and the Settings page agree. Empty when the names are unusable as key names.
    /// </summary>
    public static IReadOnlyList<AutoplayWrite> WritesFor(string eventId, string handlerId)
    {
        if (!IsKeyName(eventId) || !IsKeyName(handlerId))
        {
            return [];
        }

        return
        [
            new AutoplayWrite($@"UserChosenExecuteHandlers\{eventId}", handlerId),
            new AutoplayWrite($@"EventHandlersDefaultSelection\{eventId}", handlerId),
        ];
    }

    private static bool IsKeyName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('\\');

    private static List<AutoplayChoice> ChoicesFor(IReadOnlyList<string> handlers, IReadOnlyDictionary<string, string> names)
    {
        var choices = new List<AutoplayChoice>(Specials);
        var seen = new HashSet<string>(Specials.Select(s => s.HandlerId), StringComparer.OrdinalIgnoreCase);
        var extra = new List<AutoplayChoice>();
        foreach (var id in handlers)
        {
            if (seen.Add(id) && names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                extra.Add(new AutoplayChoice(id, name));
            }
        }

        choices.AddRange(extra.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase));
        return choices;
    }

    private static AutoplayChoice SelectedFor(string? chosenId, List<AutoplayChoice> choices, IReadOnlyDictionary<string, string> names)
    {
        if (string.IsNullOrWhiteSpace(chosenId))
        {
            return choices[0];
        }

        var match = choices.Find(c => c.HandlerId.Equals(chosenId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        // A handler Windows holds that this machine no longer offers (or never listed for the event): show it as it is
        // rather than pretending another action is selected.
        var shown = new AutoplayChoice(chosenId, names.GetValueOrDefault(chosenId) is { Length: > 0 } name ? name : chosenId);
        choices.Add(shown);
        return shown;
    }
}
