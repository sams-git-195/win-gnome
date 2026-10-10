using WinGnome.Core.Settings;
using WinGnome.Core.Shell;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Dock;

/// <summary>Merges pinned launchers with running windows into the list of dock icons.</summary>
public static class DockModelBuilder
{
    /// <summary>
    /// Builds the dock model. Pinned apps come first in pinned order; unpinned running apps (when
    /// <paramref name="includeUnpinnedRunning"/> is true) follow in order of first appearance in
    /// <paramref name="windows"/>. A window belongs to a pinned app when identities match; failing that when the
    /// pin's target is the same install as the window's process (for named-AUMID pins, only windows with no AUMID
    /// of their own; <see cref="AppPathMatch.IsSameInstall"/>); and failing that, for file-system pins, when the pinned file name equals the window's process file name.
    /// Named-AUMID pins get their target from <paramref name="resolvePath"/>; generated AUMIDs never match by path.
    /// </summary>
    public static IReadOnlyList<DockApp> Build(
        IReadOnlyList<PinnedApp> pinned,
        IReadOnlyList<RunningWindow> windows,
        nint foreground,
        bool includeUnpinnedRunning,
        Func<string, string>? resolvePath = null)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(windows);

        var slots = new List<PinnedSlot>();
        foreach (var pin in pinned)
        {
            if (pin is null || string.IsNullOrWhiteSpace(pin.LaunchId))
            {
                continue;
            }

            var identity = AppIdentity.ForLaunchId(pin.LaunchId, resolvePath);
            var exeName = "";
            string target;
            if (identity.StartsWith("path:", StringComparison.Ordinal))
            {
                target = identity["path:".Length..];
                exeName = PathText.FileName(target);
            }
            else
            {
                target = ResolveAumidTarget(pin.LaunchId, resolvePath);
            }

            slots.Add(new PinnedSlot(pin, identity, target, exeName));
        }

        var unpinned = new List<UnpinnedGroup>();
        var unpinnedByIdentity = new Dictionary<string, UnpinnedGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var window in windows)
        {
            var slot = FindPinnedSlot(slots, window);
            if (slot is not null)
            {
                slot.Windows.Add(window);
                continue;
            }

            if (!includeUnpinnedRunning)
            {
                continue;
            }

            if (!unpinnedByIdentity.TryGetValue(window.Identity, out var group))
            {
                group = new UnpinnedGroup(window.Identity);
                unpinnedByIdentity.Add(window.Identity, group);
                unpinned.Add(group);
            }

            group.Windows.Add(window);
        }

        var result = new List<DockApp>(slots.Count + unpinned.Count);
        foreach (var slot in slots)
        {
            var first = slot.Windows.Count > 0 ? slot.Windows[0] : null;
            var name = !string.IsNullOrWhiteSpace(slot.Pin.Name) ? slot.Pin.Name : first?.AppName ?? slot.Pin.LaunchId;
            result.Add(new DockApp(
                slot.Identity,
                name,
                slot.Pin.LaunchId,
                first?.ProcessPath,
                first?.AppUserModelId,
                IsPinned: true,
                slot.Windows.Select(w => w.Handle).ToList(),
                IsFocused(slot.Windows, foreground)));
        }

        foreach (var group in unpinned)
        {
            var first = group.Windows[0];
            result.Add(new DockApp(
                group.Identity,
                first.AppName,
                LaunchId: null,
                first.ProcessPath,
                first.AppUserModelId,
                IsPinned: false,
                group.Windows.Select(w => w.Handle).ToList(),
                IsFocused(group.Windows, foreground)));
        }

        return result;
    }

    /// <summary>
    /// The pin a window joins. Each pass (identity, same install, exe file name) takes the first pin that matches;
    /// when several pins match in the same pass, <see cref="PreferPlain"/> picks among them.
    /// </summary>
    private static PinnedSlot? FindPinnedSlot(List<PinnedSlot> slots, RunningWindow window)
    {
        var byIdentity = PreferPlain(slots.Where(slot => AppIdentity.IsSameApp(slot.Identity, window.Identity)));
        if (byIdentity is not null)
        {
            return byIdentity;
        }

        var windowHasAumid = !string.IsNullOrWhiteSpace(window.AppUserModelId);
        var byInstall = PreferPlain(slots.Where(slot =>
        {
            // A named-AUMID pin takes a window with a different AUMID of its own only by identity: a browser web
            // app runs the browser's exe but must keep its own icon. Path pins match by install as before.
            var isNamedAumidPin = slot.ExeName.Length == 0;
            return !(isNamedAumidPin && windowHasAumid) && AppPathMatch.IsSameInstall(slot.TargetPath, window.ProcessPath);
        }));
        if (byInstall is not null)
        {
            return byInstall;
        }

        var processFile = PathText.FileName(window.ProcessPath);
        if (processFile.Length == 0)
        {
            return null;
        }

        return PreferPlain(slots.Where(slot =>
            slot.ExeName.Length > 0 && string.Equals(slot.ExeName, processFile, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Chooses among pins that match a window equally well (KI-023). A window carries no launch arguments the dock
    /// can read, so it can't be told which of two pins for the same program (say two browser profiles) started it.
    /// The pin launched without arguments is the plain app and wins; otherwise the first in pinned order does.
    /// </summary>
    private static PinnedSlot? PreferPlain(IEnumerable<PinnedSlot> matches)
    {
        PinnedSlot? first = null;
        foreach (var slot in matches)
        {
            if (string.IsNullOrWhiteSpace(slot.Pin.Arguments))
            {
                return slot;
            }

            first ??= slot;
        }

        return first;
    }

    /// <summary>
    /// The shortcut target behind a named-AUMID pin, or "" when there is no resolver or the id is one the shell
    /// generated (its target is a generic launcher such as cmd.exe). An id the resolver doesn't know comes back
    /// unchanged, which never equals a process path.
    /// </summary>
    private static string ResolveAumidTarget(string launchId, Func<string, string>? resolvePath) =>
        resolvePath is null || AppPathMatch.IsGeneratedAumid(launchId) ? "" : resolvePath(launchId) ?? "";

    /// <summary>
    /// A minimised foreground window is not focused: after a minimise the system often leaves it as the foreground
    /// window, and treating it as focused would plan a second minimise instead of a restore.
    /// </summary>
    private static bool IsFocused(List<RunningWindow> windows, nint foreground) =>
        foreground != 0 && windows.Any(w => w.Handle == foreground && !w.IsMinimized);

    private sealed record PinnedSlot(PinnedApp Pin, string Identity, string TargetPath, string ExeName)
    {
        public List<RunningWindow> Windows { get; } = [];
    }

    private sealed record UnpinnedGroup(string Identity)
    {
        public List<RunningWindow> Windows { get; } = [];
    }
}
