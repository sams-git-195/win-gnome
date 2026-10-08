namespace WinGnome.Core.TopBar;

/// <summary>
/// Coalesces brightness writes while the slider is dragged. At most one write is in flight; levels posted
/// while it runs replace each other, so the last value always wins and slow panels are not flooded.
/// Not thread-safe: use from one thread (the UI thread).
/// </summary>
public sealed class WriteCoalescer
{
    private int? _inFlight;
    private int? _pending;

    /// <summary>True while a write is in flight or waiting.</summary>
    public bool IsBusy => _inFlight is not null;

    /// <summary>Returns the level to write now, or null when a write is already running and this level was held back.</summary>
    public int? Post(int level)
    {
        if (_inFlight is null)
        {
            _inFlight = level;
            return level;
        }

        _pending = level;
        return null;
    }

    /// <summary>Call when the running write has finished. Returns the next level to write, or null when idle again.</summary>
    public int? Complete()
    {
        var next = _pending;
        _pending = null;
        if (next is { } level && level != _inFlight)
        {
            _inFlight = level;
            return level;
        }

        _inFlight = null;
        return null;
    }
}
