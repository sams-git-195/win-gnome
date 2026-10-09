using Microsoft.Win32;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.RemovableMedia;

/// <summary>Everything the Removable Media panel shows, read together.</summary>
/// <param name="Disabled">True while AutoPlay is switched off ("Never prompt or start programs on media insertion").</param>
/// <param name="Rows">The media types with their handlers and current choice.</param>
internal sealed record AutoplayState(bool Disabled, IReadOnlyList<AutoplayRow> Rows);

/// <summary>
/// The only class that touches the AutoPlay keys. Windows Settings keeps them under HKCU
/// <c>Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers</c>; the machine's own list of handlers is the
/// same path under HKLM (read only, WinGnome never writes HKLM). The storage is undocumented but long stable (KI-091),
/// and Explorer reads it when media arrives, so a change needs no broadcast.
/// </summary>
internal static class AutoplayStore
{
    private const string Base = @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";

    /// <summary>Reads the AutoPlay switch, the media types and the current choices. Throws on registry failures.</summary>
    public static AutoplayState Read()
    {
        var handlersByEvent = ReadHandlersByEvent();
        var chosen = ReadChosen(handlersByEvent.Keys);
        var names = ReadNames(handlersByEvent.Values.SelectMany(h => h).Concat(chosen.Values));
        var rows = AutoplayModel.Build(handlersByEvent.Keys, handlersByEvent, names, chosen);
        return new AutoplayState(ReadDisabled(), rows);
    }

    /// <summary>Switches AutoPlay off (1) or on (0).</summary>
    public static bool WriteDisabled(bool disabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Base, writable: true);
        key.SetValue("DisableAutoplay", disabled ? 1 : 0, RegistryValueKind.DWord);
        return true;
    }

    /// <summary>Records the handler chosen for a media type, in both places Windows Settings does.</summary>
    public static bool WriteChoice(string eventId, string handlerId)
    {
        var writes = AutoplayModel.WritesFor(eventId, handlerId);
        foreach (var write in writes)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{Base}\{write.SubKey}", writable: true);
            key.SetValue("", write.Handler, RegistryValueKind.String);
        }

        return writes.Count > 0;
    }

    private static bool ReadDisabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Base);
        return key?.GetValue("DisableAutoplay") is int and 1;
    }

    /// <summary>Handler ids registered per event in both hives (an event with none is still listed, with an empty set).</summary>
    private static Dictionary<string, IReadOnlyList<string>> ReadHandlersByEvent()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var events = hive.OpenSubKey($@"{Base}\EventHandlers");
            foreach (var name in events?.GetSubKeyNames() ?? [])
            {
                using var key = events!.OpenSubKey(name);
                if (!found.TryGetValue(name, out var list))
                {
                    list = [];
                    found[name] = list;
                }

                // Handler ids are the value names; the default value (empty name) is not a handler.
                list.AddRange((key?.GetValueNames() ?? []).Where(v => v.Length > 0));
            }
        }

        return found.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The user's chosen handler per event: the explicit choice, else Windows' default selection.</summary>
    private static Dictionary<string, string> ReadChosen(IEnumerable<string> events)
    {
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var eventId in events.Concat(["StorageOnArrival", "ShowPicturesOnArrival"]))
        {
            var handler = ReadDefaultValue($@"{Base}\UserChosenExecuteHandlers\{eventId}")
                ?? ReadDefaultValue($@"{Base}\EventHandlersDefaultSelection\{eventId}");
            if (!string.IsNullOrWhiteSpace(handler))
            {
                chosen[eventId] = handler;
            }
        }

        return chosen;
    }

    private static string? ReadDefaultValue(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue("") as string;
    }

    /// <summary>Display names of the handlers, the per-user registration overriding the machine's.</summary>
    private static IReadOnlyDictionary<string, string> ReadNames(IEnumerable<string> handlerIds)
    {
        // A name with a backslash would open some other key, so it is never looked up.
        var ids = handlerIds.Where(id => id.Length > 0 && !id.Contains('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return AutoplayModel.MergeNames(ReadNames(Registry.LocalMachine, ids), ReadNames(Registry.CurrentUser, ids));
    }

    private static Dictionary<string, string> ReadNames(RegistryKey hive, List<string> ids)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            using var key = hive.OpenSubKey($@"{Base}\Handlers\{id}");
            if (key is null || IndirectString.Load(key.GetValue("Action") as string) is not { } action)
            {
                continue;
            }

            var provider = IndirectString.Load(key.GetValue("Provider") as string);
            names[id] = provider is not null && !provider.Equals(action, StringComparison.OrdinalIgnoreCase)
                ? $"{action} ({provider})"
                : action;
        }

        return names;
    }
}
