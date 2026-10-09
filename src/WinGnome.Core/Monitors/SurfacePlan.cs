using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Monitors;

/// <summary>A per-monitor surface (a top bar or a dock) as it exists now.</summary>
/// <param name="Key">The monitor key the surface belongs to.</param>
/// <param name="MonitorBounds">The monitor rectangle the surface was laid out and docked for.</param>
/// <param name="Dpi">The monitor DPI the surface was laid out for.</param>
/// <param name="Detached">True when its AppBar undocked itself (the monitor went away or changed under it).</param>
public sealed record SurfaceState(string Key, PixelRect MonitorBounds, int Dpi, bool Detached = false);

public enum SurfaceStepKind
{
    /// <summary>Undock and destroy the surface.</summary>
    Remove,
    /// <summary>Undock the surface, keeping it, so it can be docked again in a later <see cref="Dock"/> step.</summary>
    Release,
    /// <summary>Lay the surface out for <see cref="SurfaceStep.Monitor"/> and dock it there (re-keying it when the key differs).</summary>
    Dock,
    /// <summary>Create a surface on <see cref="SurfaceStep.Monitor"/> and dock it.</summary>
    Add,
}

/// <summary>One reconcile step. <paramref name="Key"/> is the existing surface's key (the new monitor's key for Add).</summary>
public sealed record SurfaceStep(SurfaceStepKind Kind, string Key, MonitorInfo? Monitor = null);

/// <summary>
/// Which monitors get a surface, and the ordered steps that take the current surfaces there. Steps come in four
/// groups: every removal, then every release, then every dock, then every addition. Re-docked surfaces are all
/// released before any of them docks again, so Explorer never sees two WinGnome AppBars for one monitor (which would
/// stack two strips), even mid-way through a primary swap where monitors trade coordinates.
/// </summary>
public static class SurfacePlan
{
    /// <summary>The monitors that should have a surface: every monitor, or only the primary.</summary>
    public static IReadOnlyList<MonitorInfo> Compute(MonitorLayout layout, BarMonitors mode)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return mode == BarMonitors.All ? layout.Monitors : layout.Primary is { } primary ? [primary] : [];
    }

    public static IReadOnlyList<SurfaceStep> Reconcile(IEnumerable<SurfaceState> current, MonitorLayout layout, BarMonitors mode)
    {
        ArgumentNullException.ThrowIfNull(current);
        var desired = Compute(layout, mode);
        var surfaces = current.DistinctBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToList();

        var removes = new List<SurfaceStep>();
        var releases = new List<SurfaceStep>();
        var docks = new List<SurfaceStep>();
        var adds = new List<SurfaceStep>();

        void Redock(SurfaceState surface, MonitorInfo target)
        {
            if (!surface.Detached)
            {
                releases.Add(new SurfaceStep(SurfaceStepKind.Release, surface.Key));
            }

            docks.Add(new SurfaceStep(SurfaceStepKind.Dock, surface.Key, target));
        }

        var orphans = new List<SurfaceState>();
        foreach (var surface in surfaces)
        {
            if (desired.FirstOrDefault(m => SameKey(m.Key, surface.Key)) is not { } monitor)
            {
                orphans.Add(surface);
            }
            else if (surface.Detached || surface.MonitorBounds != monitor.Bounds || surface.Dpi != monitor.Dpi)
            {
                Redock(surface, monitor);
            }
        }

        var missing = desired.Where(m => !surfaces.Any(s => SameKey(s.Key, m.Key))).ToList();

        // With one surface for the primary, a new primary takes the existing surface over instead of a destroy and
        // a rebuild (pins, icons and backdrop stay).
        if (mode == BarMonitors.Primary && orphans.Count > 0 && missing.Count > 0)
        {
            Redock(orphans[0], missing[0]);
            orphans.RemoveAt(0);
            missing.RemoveAt(0);
        }

        removes.AddRange(orphans.Select(s => new SurfaceStep(SurfaceStepKind.Remove, s.Key)));
        adds.AddRange(missing.Select(m => new SurfaceStep(SurfaceStepKind.Add, m.Key, m)));
        return [.. removes, .. releases, .. docks, .. adds];
    }

    private static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
