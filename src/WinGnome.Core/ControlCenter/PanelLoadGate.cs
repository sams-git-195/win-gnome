namespace WinGnome.Core.ControlCenter;

/// <summary>
/// Which of a settings panel's background loads may show its result (spec 0020's <c>LoadAsync</c>): every load belongs
/// to one open of the panel (its generation) and one channel, and only the newest load on a channel survives — an
/// older read that finishes late (a re-read after each write) can't put stale values back, and loads on different
/// channels (independent lists on one panel) never drop each other. Opening and closing each start a new generation,
/// so a result that arrives after the panel was closed (or closed and reopened) is dropped. Pure counting: the panel
/// keeps the dispatcher, the read and the actual showing.
/// </summary>
public sealed class PanelLoadGate
{
    // The newest load's sequence per channel; cleared with every open and close (the generation already drops the
    // earlier open's loads).
    private readonly Dictionary<string, int> _newest = new(StringComparer.Ordinal);

    private int _generation;
    private int _sequence;

    /// <summary>True between <see cref="Opened"/> and <see cref="Closed"/>.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The current open's generation; every load records it when it starts.</summary>
    public int Generation => _generation;

    /// <summary>Starts a new open: a new generation, dropping every load started before it.</summary>
    public void Opened()
    {
        IsOpen = true;
        _generation++;
        _newest.Clear();
    }

    /// <summary>Ends the current open: a new generation, so loads still running can't show into a later open.</summary>
    public void Closed()
    {
        IsOpen = false;
        _generation++;
        _newest.Clear();
    }

    /// <summary>Registers a new load on <paramref name="channel"/>, superseding the earlier ones, and returns its sequence.</summary>
    public int Begin(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var sequence = ++_sequence;
        _newest[channel] = sequence;
        return sequence;
    }

    /// <summary>True while the panel is still open from the same generation and the load is still its channel's newest.</summary>
    public bool MayShow(int generation, string channel, int sequence) =>
        IsOpen
        && generation == _generation
        && _newest.TryGetValue(channel, out var newest)
        && newest == sequence;
}
