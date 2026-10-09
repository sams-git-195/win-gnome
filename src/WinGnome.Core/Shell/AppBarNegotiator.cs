using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

public enum AppBarEdge
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>A monitor's full rectangle in physical pixels (virtual-screen coordinates, may be negative).</summary>
public readonly record struct AppBarMonitor(int Id, PixelRect Bounds);

public readonly record struct MonitorWorkArea(int MonitorId, PixelRect Bounds, PixelRect WorkArea);

/// <summary>
/// The geometry half of the AppBar protocol (ABM_NEW, QUERYPOS, SETPOS, REMOVE, SETAUTOHIDEBAREX)
/// for a shell that serves it: which rectangle each docked bar gets so bars never overlap, and each
/// monitor's resulting work area. All rectangles are integer physical pixels, so DPI doesn't enter.
/// Not thread-safe: call from the UI thread, like the window messages that drive it. Windows are
/// identified by handle value, so the phase-1 server must call <see cref="Remove"/> when an
/// appbar window is destroyed without ABM_REMOVE; a reused HWND would otherwise inherit its slot.
/// </summary>
/// <remarks>
/// Rules: a bar's monitor is the one it overlaps most (ties: the earlier in the list; none: the first,
/// which is the primary, though a rectangle off every monitor is returned as proposed and reserves nothing). QUERYPOS/SETPOS clamp to the monitor, push the edge side past bars already on
/// the same edge, and trim the span against bars on the perpendicular edges, so a new bar never
/// overlaps an existing one. Only the edge side and span move; the far side is the caller's thickness.
/// Auto-hide bars reserve no space; there is at most one per edge per monitor.
/// </remarks>
public sealed class AppBarNegotiator
{
    private sealed record Docked(AppBarEdge Edge, PixelRect Rect);

    private readonly Dictionary<long, Docked?> _bars = [];
    private readonly Dictionary<long, int> _registrationOrder = [];
    private int _nextOrder;
    private readonly Dictionary<(int MonitorId, AppBarEdge Edge), long> _autoHide = [];
    private IReadOnlyList<AppBarMonitor> _monitors = [];

    /// <summary>Replaces the monitor layout (display change). Auto-hide claims on monitors that vanished are dropped.</summary>
    public void SetMonitors(IReadOnlyList<AppBarMonitor> monitors)
    {
        _monitors = monitors;
        var ids = monitors.Select(m => m.Id).ToHashSet();
        foreach (var key in _autoHide.Keys.Where(k => !ids.Contains(k.MonitorId)).ToList())
        {
            _autoHide.Remove(key);
        }
    }

    /// <summary>ABM_NEW. False when the window is already registered.</summary>
    public bool Register(long hwnd)
    {
        if (!_bars.TryAdd(hwnd, null))
        {
            return false;
        }

        _registrationOrder[hwnd] = _nextOrder++;
        return true;
    }

    /// <summary>ABM_REMOVE. False when the window wasn't registered.</summary>
    public bool Remove(long hwnd)
    {
        if (!_bars.Remove(hwnd))
        {
            return false;
        }

        _registrationOrder.Remove(hwnd);
        foreach (var key in _autoHide.Where(a => a.Value == hwnd).Select(a => a.Key).ToList())
        {
            _autoHide.Remove(key);
        }

        return true;
    }

    /// <summary>ABM_QUERYPOS. The rectangle the bar would get, or null when it isn't registered. Changes nothing.</summary>
    public PixelRect? QueryPos(long hwnd, AppBarEdge edge, PixelRect proposed) =>
        _bars.ContainsKey(hwnd) ? Adjust(hwnd, edge, proposed) : null;

    /// <summary>ABM_SETPOS. Docks the bar at the adjusted rectangle and returns it; null when it isn't registered.</summary>
    public PixelRect? SetPos(long hwnd, AppBarEdge edge, PixelRect proposed)
    {
        if (!_bars.ContainsKey(hwnd))
        {
            return null;
        }

        var rect = Adjust(hwnd, edge, proposed);
        _bars[hwnd] = new Docked(edge, rect);
        return rect;
    }

    /// <summary>
    /// ABM_SETAUTOHIDEBAREX on the monitor that <paramref name="monitorHint"/> overlaps most. Registering
    /// fails when another bar already auto-hides on that edge; the bar stops reserving docked space.
    /// Unregistering always succeeds.
    /// </summary>
    public bool SetAutoHide(long hwnd, AppBarEdge edge, PixelRect monitorHint, bool enable)
    {
        if (!_bars.ContainsKey(hwnd) || MonitorFor(monitorHint) is not { } monitor)
        {
            return false;
        }

        var key = (monitor.Id, edge);
        if (!enable)
        {
            if (_autoHide.TryGetValue(key, out var owner) && owner == hwnd)
            {
                _autoHide.Remove(key);
            }

            return true;
        }

        if (_autoHide.TryGetValue(key, out var existing) && existing != hwnd)
        {
            return false;
        }

        _autoHide[key] = hwnd;
        _bars[hwnd] = null;
        return true;
    }

    /// <summary>ABM_GETAUTOHIDEBAREX: the window auto-hiding on that edge of the hinted monitor.</summary>
    public long? GetAutoHideBar(AppBarEdge edge, PixelRect monitorHint) =>
        MonitorFor(monitorHint) is { } monitor && _autoHide.TryGetValue((monitor.Id, edge), out var hwnd) ? hwnd : null;

    /// <summary>Each monitor's bounds minus the docked bars, in monitor order.</summary>
    public IReadOnlyList<MonitorWorkArea> ComputeWorkAreas()
    {
        var result = new List<MonitorWorkArea>(_monitors.Count);
        foreach (var monitor in _monitors)
        {
            var area = monitor.Bounds;
            foreach (var docked in DockedOn(monitor))
            {
                area = docked.Edge switch
                {
                    AppBarEdge.Left => area with { Left = Math.Max(area.Left, docked.Rect.Right) },
                    AppBarEdge.Right => area with { Right = Math.Min(area.Right, docked.Rect.Left) },
                    AppBarEdge.Top => area with { Top = Math.Max(area.Top, docked.Rect.Bottom) },
                    _ => area with { Bottom = Math.Min(area.Bottom, docked.Rect.Top) },
                };
            }

            result.Add(new MonitorWorkArea(monitor.Id, monitor.Bounds, Normalise(area)));
        }

        return result;
    }

    private PixelRect Adjust(long hwnd, AppBarEdge edge, PixelRect proposed)
    {
        // A rectangle off every monitor can't be negotiated; it reserves nothing either (see DockedOn).
        if (MonitorFor(proposed) is not { } monitor || !monitor.Bounds.Intersects(proposed))
        {
            return proposed;
        }

        var b = monitor.Bounds;
        var r = new PixelRect(
            Math.Max(proposed.Left, b.Left),
            Math.Max(proposed.Top, b.Top),
            Math.Min(proposed.Right, b.Right),
            Math.Min(proposed.Bottom, b.Bottom));

        // Only bars registered earlier are avoided (as Explorer does), so a bar re-querying its own
        // rectangle can't be pushed past a bar that came after it and bars never leapfrog.
        var order = _registrationOrder[hwnd];
        foreach (var other in _bars.Where(p => p.Key != hwnd && p.Value is not null && _registrationOrder[p.Key] < order).Select(p => p.Value!))
        {
            if (MonitorFor(other.Rect) is { } otherMonitor && otherMonitor.Id != monitor.Id)
            {
                continue;
            }

            r = Avoid(r, edge, other);
        }

        return Normalise(r);
    }

    private static PixelRect Avoid(PixelRect r, AppBarEdge edge, Docked other)
    {
        var o = other.Rect;
        var vertical = edge is AppBarEdge.Left or AppBarEdge.Right;
        var otherVertical = other.Edge is AppBarEdge.Left or AppBarEdge.Right;

        if (other.Edge == edge)
        {
            return edge switch
            {
                AppBarEdge.Left => r with { Left = Math.Max(r.Left, o.Right) },
                AppBarEdge.Right => r with { Right = Math.Min(r.Right, o.Left) },
                AppBarEdge.Top => r with { Top = Math.Max(r.Top, o.Bottom) },
                _ => r with { Bottom = Math.Min(r.Bottom, o.Top) },
            };
        }

        if (vertical == otherVertical)
        {
            return r; // opposite sides of the screen
        }

        return other.Edge switch
        {
            AppBarEdge.Top => r with { Top = Math.Max(r.Top, o.Bottom) },
            AppBarEdge.Bottom => r with { Bottom = Math.Min(r.Bottom, o.Top) },
            AppBarEdge.Left => r with { Left = Math.Max(r.Left, o.Right) },
            _ => r with { Right = Math.Min(r.Right, o.Left) },
        };
    }

    private IEnumerable<Docked> DockedOn(AppBarMonitor monitor) =>
        _bars.Values.Where(d => d is not null && monitor.Bounds.Intersects(d.Rect) && MonitorFor(d.Rect) is { } m && m.Id == monitor.Id).Select(d => d!);

    private AppBarMonitor? MonitorFor(PixelRect rect)
    {
        if (_monitors.Count == 0)
        {
            return null;
        }

        var best = _monitors[0];
        var bestArea = -1L;
        foreach (var monitor in _monitors)
        {
            var overlap = monitor.Bounds.Intersect(rect);
            var area = (long)overlap.Width * overlap.Height;
            if (area > bestArea)
            {
                best = monitor;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>Collapses an inverted rectangle to zero width/height instead of leaving negative sizes.</summary>
    private static PixelRect Normalise(PixelRect r) =>
        new(r.Left, r.Top, Math.Max(r.Left, r.Right), Math.Max(r.Top, r.Bottom));
}
