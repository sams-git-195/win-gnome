namespace WinGnome.Core.Dock;

/// <summary>What the dock should do with its preview popover after a hover input.</summary>
public enum DockPreviewChange
{
    /// <summary>Nothing changes on screen.</summary>
    None,
    /// <summary>Open the popover for <see cref="DockPreviewHover.Key"/>.</summary>
    Show,
    /// <summary>The popover is open; show <see cref="DockPreviewHover.Key"/>'s windows in it instead.</summary>
    Switch,
    /// <summary>Close the popover.</summary>
    Hide,
}

/// <summary>Where the hover state machine is.</summary>
public enum DockPreviewPhase
{
    Idle,
    /// <summary>The pointer rests on an icon and the show delay is running.</summary>
    Pending,
    /// <summary>The popover is shown.</summary>
    Open,
    /// <summary>The popover is shown but the pointer left; the grace period is running.</summary>
    Leaving,
}

/// <summary>
/// The hover rules of the dock's window preview popover, without clocks, timers or windows. The app feeds it pointer
/// events with a millisecond timestamp, arms one timer for <see cref="Deadline"/> and calls <see cref="Tick"/> when it
/// fires, then does what the returned <see cref="DockPreviewChange"/> says.
/// </summary>
public sealed class DockPreviewHover
{
    /// <summary>How long the pointer must rest on an icon before the popover opens.</summary>
    public const long ShowDelayMs = 400;

    /// <summary>How long the popover stays after the pointer left both the icon and the popover.</summary>
    public const long LeaveGraceMs = 300;

    private long _due;
    private bool _suppressed;
    private bool _overPopover;
    private string? _hoverKey;
    private string? _disarmedKey;

    public DockPreviewPhase Phase { get; private set; } = DockPreviewPhase.Idle;

    /// <summary>The icon the popover is pending for, shown for, or leaving; null while idle.</summary>
    public string? Key { get; private set; }

    /// <summary>When the app must call <see cref="Tick"/>; null while nothing is pending.</summary>
    public long? Deadline => Phase is DockPreviewPhase.Pending or DockPreviewPhase.Leaving ? _due : null;

    /// <summary>
    /// The pointer is over an icon (<paramref name="key"/>), or over no icon (null). Report it when the hovered icon
    /// changes. <paramref name="hasPreview"/> is false for icons that never show a popover (fewer than two windows).
    /// </summary>
    public DockPreviewChange PointerOverIcon(string? key, bool hasPreview, long nowMs)
    {
        var previewKey = hasPreview ? key : null;
        _hoverKey = previewKey;

        if (_disarmedKey is not null && !string.Equals(_disarmedKey, previewKey, StringComparison.Ordinal))
        {
            _disarmedKey = null;
        }

        if (_suppressed || _disarmedKey is not null)
        {
            return DockPreviewChange.None;
        }

        return previewKey is null ? PointerLeftIcons(nowMs) : PointerOnPreviewIcon(previewKey, nowMs);
    }

    /// <summary>The pointer entered or left the popover.</summary>
    public DockPreviewChange PointerOverPopover(bool over, long nowMs)
    {
        _overPopover = over;
        if (_suppressed)
        {
            return DockPreviewChange.None;
        }

        if (over && Phase == DockPreviewPhase.Leaving)
        {
            Phase = DockPreviewPhase.Open;
        }
        else if (!over && Phase == DockPreviewPhase.Open && !string.Equals(_hoverKey, Key, StringComparison.Ordinal))
        {
            StartLeaving(nowMs);
        }

        return DockPreviewChange.None;
    }

    /// <summary>Call when <see cref="Deadline"/> has passed.</summary>
    public DockPreviewChange Tick(long nowMs)
    {
        if (Deadline is not { } due || nowMs < due)
        {
            return DockPreviewChange.None;
        }

        if (Phase == DockPreviewPhase.Pending)
        {
            Phase = DockPreviewPhase.Open;
            return DockPreviewChange.Show;
        }

        return Reset();
    }

    /// <summary>
    /// Closes the popover for good reason (a click, a menu, a drag, the foreground changing). The icon under the pointer
    /// does not open it again until the pointer has moved to another icon or off the dock.
    /// </summary>
    public DockPreviewChange Dismiss()
    {
        _disarmedKey = _hoverKey;
        return Reset();
    }

    /// <summary>
    /// Closes the popover for a reason that is not the user's (a relayout, a settings or display change, the dock
    /// hiding, another dock's popover opening). Unlike <see cref="Dismiss"/> it disarms nothing: an icon still under the
    /// pointer starts the show delay again, so the popover comes back without the pointer having to leave and return.
    /// </summary>
    public DockPreviewChange Close(long nowMs)
    {
        var change = Reset();
        if (!_suppressed && _disarmedKey is null && _hoverKey is { } key)
        {
            StartPending(key, nowMs);
        }

        return change;
    }

    /// <summary>
    /// Switches the machine off while a menu or drag is up, or the dock is hidden. Lifting it does not re-arm the
    /// icon under the pointer: the next <see cref="PointerOverIcon"/> does.
    /// </summary>
    public DockPreviewChange SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        return suppressed ? Reset() : DockPreviewChange.None;
    }

    private DockPreviewChange PointerLeftIcons(long nowMs)
    {
        switch (Phase)
        {
            case DockPreviewPhase.Pending:
                Reset();
                break;

            case DockPreviewPhase.Open when !_overPopover:
                StartLeaving(nowMs);
                break;
        }

        return DockPreviewChange.None;
    }

    private DockPreviewChange PointerOnPreviewIcon(string key, long nowMs)
    {
        var sameKey = string.Equals(Key, key, StringComparison.Ordinal);
        switch (Phase)
        {
            case DockPreviewPhase.Idle:
                StartPending(key, nowMs);
                return DockPreviewChange.None;

            case DockPreviewPhase.Pending:
                if (!sameKey)
                {
                    StartPending(key, nowMs);
                }

                return DockPreviewChange.None;

            default:
                Phase = DockPreviewPhase.Open;
                if (sameKey)
                {
                    return DockPreviewChange.None;
                }

                Key = key;
                return DockPreviewChange.Switch;
        }
    }

    private void StartPending(string key, long nowMs)
    {
        Phase = DockPreviewPhase.Pending;
        Key = key;
        _due = nowMs + ShowDelayMs;
    }

    private void StartLeaving(long nowMs)
    {
        Phase = DockPreviewPhase.Leaving;
        _due = nowMs + LeaveGraceMs;
    }

    private DockPreviewChange Reset()
    {
        var wasShown = Phase is DockPreviewPhase.Open or DockPreviewPhase.Leaving;
        Phase = DockPreviewPhase.Idle;
        Key = null;
        _overPopover = false;
        return wasShown ? DockPreviewChange.Hide : DockPreviewChange.None;
    }
}
