using WinGnome.Core.Geometry;

namespace WinGnome.Core.Input;

/// <summary>
/// Fires once when the pointer rests in a monitor's top-left corner box for long enough. The pointer must leave
/// the box before it can fire again.
/// </summary>
public sealed class HotCornerDetector
{
    private readonly long _dwellMs;
    private readonly int _size;
    private long? _enteredAt;
    private bool _fired;

    /// <param name="dwellMs">Milliseconds the pointer must stay inside (0 fires on the first sample).</param>
    /// <param name="sizePx">Width and height of the corner box in pixels.</param>
    public HotCornerDetector(int dwellMs, int sizePx = 1)
    {
        _dwellMs = Math.Max(0, dwellMs);
        _size = Math.Max(1, sizePx);
    }

    /// <summary>
    /// Feeds a pointer sample (screen pixels) and the monitor under it. Returns true exactly once per visit
    /// to the corner, when the dwell time has elapsed.
    /// </summary>
    public bool Update(int x, int y, PixelRect monitor, long timestampMs)
    {
        var inside = x >= monitor.Left && x < monitor.Left + _size && y >= monitor.Top && y < monitor.Top + _size;
        if (!inside)
        {
            _enteredAt = null;
            _fired = false;
            return false;
        }

        _enteredAt ??= timestampMs;
        if (_fired)
        {
            return false;
        }

        if (timestampMs - _enteredAt.Value >= _dwellMs)
        {
            _fired = true;
            return true;
        }

        return false;
    }
}
