namespace WinGnome.Core.Tray;

/// <summary>What the shell knows about one notification icon (everything except the image itself).</summary>
/// <param name="Id">Identity (owner window, ID, GUID).</param>
/// <param name="CallbackMessage">Message posted to the owner on mouse events; 0 = the app wants none.</param>
/// <param name="Version">NOTIFYICON_VERSION: 0 (legacy), 3 or 4. Decides the callback message format.</param>
/// <param name="Tip">Tooltip text.</param>
/// <param name="State">NIS_* state (hidden, shared icon).</param>
/// <param name="ShowTip">NIF_SHOWTIP: a version-4 icon that still wants the standard tooltip.</param>
/// <param name="HasIcon">False until the app supplies an icon (or after it clears it).</param>
public sealed record TrayIconState(
    TrayIconId Id,
    uint CallbackMessage,
    uint Version,
    string Tip,
    NotifyIconStates State,
    bool ShowTip,
    bool HasIcon)
{
    public bool IsHidden => (State & NotifyIconStates.Hidden) != 0;

    /// <summary>Hidden icons (NIS_HIDDEN) and icons without an image take no space, as in the real tray.</summary>
    public bool IsVisible => !IsHidden && HasIcon;

    /// <summary>
    /// Version-4 icons get the standard tooltip only with NIF_SHOWTIP; otherwise the shell sends NIN_POPUPOPEN and
    /// the app draws its own pop-up.
    /// </summary>
    public bool ShowsToolTip => Tip.Length > 0 && (Version < TrayCallback.Version4 || ShowTip);

    /// <summary>The state after an NIM_ADD/NIM_MODIFY: only the fields whose NIF_* flag is set change.</summary>
    public TrayIconState With(NotifyIconCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var next = this;
        if (command.Has(NotifyIconFields.ItemGuid) && command.ItemGuid != Guid.Empty)
        {
            // A GUID-identified icon may move to a new owner window (the app recreated it); follow it.
            next = next with { Id = command.Key };
        }

        if (command.Has(NotifyIconFields.Message))
        {
            next = next with { CallbackMessage = command.CallbackMessage };
        }

        if (command.Has(NotifyIconFields.Icon))
        {
            next = next with { HasIcon = command.Icon != 0 };
        }

        if (command.Has(NotifyIconFields.Tip))
        {
            next = next with { Tip = command.Tip };
        }

        if (command.Has(NotifyIconFields.Tip) || command.Has(NotifyIconFields.ShowTip))
        {
            next = next with { ShowTip = command.Has(NotifyIconFields.ShowTip) };
        }

        if (command.Has(NotifyIconFields.State))
        {
            next = next with { State = (next.State & ~command.StateMask) | (command.State & command.StateMask) };
        }

        return next;
    }

    public static TrayIconState Create(NotifyIconCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new TrayIconState(command.Key, 0, 0, string.Empty, NotifyIconStates.None, false, false).With(command);
    }
}

public enum TrayChangeKind
{
    None,
    Added,
    Updated,
    Removed,
}

/// <summary>The effect of one call on the icon list. Apply changes in order to mirror the list elsewhere.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Index">Position of the affected icon (for <see cref="TrayChangeKind.Removed"/>: where it was).</param>
/// <param name="Icon">The icon's new state (for <see cref="TrayChangeKind.Removed"/>: its last state).</param>
/// <param name="Accepted">The result to report to the app (TRUE/FALSE of Shell_NotifyIcon).</param>
/// <param name="CreatedViaModify">
/// True when a NIM_MODIFY the shell accepted created the entry because the host never saw it added (the add
/// reached Explorer alone, in a front gap). The entry then carries only what that modify had — typically
/// neither callback message nor version — so clicks are dead until the app re-registers.
/// </param>
/// <param name="LearnedCallback">
/// True when this call adopted the icon's callback message and version from a twice-observed unflagged pair
/// (spec 0022 addendum): <see cref="Icon"/> carries the authoritative values and clicks deliver from now on.
/// </param>
/// <param name="ObservedCallback">
/// The plausible raw (callback, version) pair an unflagged update carried, when the registry stored it as a
/// pending learning candidate (a first sighting, or a differing pair that replaced the previous candidate).
/// Null when nothing was stored. A diagnostic channel: it is the field evidence for whether apps populate
/// the raw fields at all.
/// </param>
public readonly record struct TrayChange(
    TrayChangeKind Kind,
    int Index,
    TrayIconState? Icon,
    bool Accepted,
    bool CreatedViaModify = false,
    bool LearnedCallback = false,
    (uint Callback, uint Version)? ObservedCallback = null)
{
    public static TrayChange Rejected => new(TrayChangeKind.None, -1, null, false);
}

/// <summary>
/// The notification-area icon list, in the order icons were added, driven by Shell_NotifyIcon calls exactly as the
/// shell interprets them. Not thread-safe: one thread (the tray window's) owns it.
/// </summary>
public sealed class TrayIconRegistry
{
    // Plausibility gates for learning a callback from unflagged updates (spec 0022 addendum). A real tray
    // callback is private to the app: WM_USER..WM_APP. Below WM_USER are plain window messages that must
    // never be adopted (a junk WM_CLOSE riding in an uninitialised struct field would otherwise be posted
    // to the app on every click); 0xC000 and above is the RegisterWindowMessage range, where an unflagged
    // value is far likelier junk than a callback.
    private const uint MinimumPlausibleCallback = 0x0400; // WM_USER
    private const uint MaximumPlausibleCallback = 0xBFFF; // top of WM_APP

    private readonly List<TrayIconState> _icons = [];

    // Raw (callback, version) pairs seen once on an unflagged update, waiting for the same pair to be
    // observed again (Learn). In-memory only: never persisted, cleared when the icon goes or flagged
    // data arrives.
    private readonly Dictionary<TrayIconId, (uint Callback, uint Version)> _candidates = [];

    public IReadOnlyList<TrayIconState> Icons => _icons;

    /// <summary>Applies one call.</summary>
    /// <param name="command">The parsed call.</param>
    /// <param name="knownToShell">
    /// Whether Explorer (which receives every call too) accepted it. Lets a modify for an icon we never saw being added
    /// (it was added while Explorer's tray window was in front of ours) add the icon instead of being lost.
    /// </param>
    public TrayChange Apply(NotifyIconCommand command, bool knownToShell)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Owner == 0)
        {
            // Without an owner window nothing could ever receive the icon's clicks.
            return TrayChange.Rejected;
        }

        var index = IndexOf(command.Key);
        switch (command.Message)
        {
            case NotifyIconMessage.Add:
                // Apps answer every TaskbarCreated broadcast with NIM_ADD, also for icons we already show: update in place.
                if (index < 0)
                {
                    return Add(command);
                }

                if (command.Has(NotifyIconFields.Message))
                {
                    // Flagged data is authoritative: drop any pending unflagged guess for this icon.
                    _candidates.Remove(_icons[index].Id);
                }

                return Update(index, command);

            case NotifyIconMessage.Modify:
                if (index >= 0)
                {
                    return Learn(index, command, Update(index, command));
                }

                if (!knownToShell)
                {
                    return TrayChange.Rejected;
                }

                var adopted = Add(command, createdViaModify: true);
                return Learn(adopted.Index, command, adopted);

            case NotifyIconMessage.Delete:
                if (index < 0)
                {
                    return TrayChange.Rejected;
                }

                var removed = _icons[index];
                _candidates.Remove(removed.Id);
                _icons.RemoveAt(index);
                return new TrayChange(TrayChangeKind.Removed, index, removed, true);

            case NotifyIconMessage.SetVersion:
                if (index < 0)
                {
                    return TrayChange.Rejected;
                }

                // Authoritative version: a pending unflagged pair (which carries its own version guess) is void.
                _candidates.Remove(_icons[index].Id);
                _icons[index] = _icons[index] with { Version = command.Version };
                return new TrayChange(TrayChangeKind.Updated, index, _icons[index], true);

            case NotifyIconMessage.SetFocus:
                return index >= 0 ? new TrayChange(TrayChangeKind.None, index, _icons[index], true) : TrayChange.Rejected;

            default:
                return TrayChange.Rejected;
        }
    }

    /// <summary>
    /// Removes icons whose owner is gone (the app crashed or exited without NIM_DELETE, which Explorer cleans up the
    /// same way). Returns the removals from the highest index down, so applying them in order keeps indices valid.
    /// </summary>
    public IReadOnlyList<TrayChange> RemoveWhere(Func<TrayIconState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var changes = new List<TrayChange>();
        for (var i = _icons.Count - 1; i >= 0; i--)
        {
            if (predicate(_icons[i]))
            {
                _candidates.Remove(_icons[i].Id);
                changes.Add(new TrayChange(TrayChangeKind.Removed, i, _icons[i], true));
                _icons.RemoveAt(i);
            }
        }

        return changes;
    }

    public int IndexOf(TrayIconId query) => _icons.FindIndex(icon => icon.Id.IsIdentifiedBy(query));

    /// <summary>
    /// Callback learning (spec 0022 addendum, KI-105): an app whose NIM_ADD reached Explorer alone (front
    /// gap) is known only through its later updates — typically tooltip-only NIM_MODIFYs without
    /// NIF_MESSAGE, leaving the entry click-dead. Apps nearly always reuse ONE NOTIFYICONDATA struct for
    /// every Shell_NotifyIcon call, so the raw uCallbackMessage/uVersion ride along on those updates even
    /// unflagged. A single unflagged value could still be an uninitialised-struct leftover, so the same
    /// plausible pair must be observed twice before it is adopted: apps answer every TaskbarCreated
    /// broadcast with a modify, so the host's startup broadcast and its ~2 s heal broadcast (spec 0022)
    /// deliver the two observations in practice, while requiring the app's own live struct to repeat
    /// itself makes false positives vanishingly rare. Flagged data always wins and clears the candidate;
    /// learning never overwrites a real (non-zero) callback.
    /// </summary>
    private TrayChange Learn(int index, NotifyIconCommand command, TrayChange change)
    {
        if (command.Has(NotifyIconFields.Message))
        {
            // NIF_MESSAGE: With() already applied the authoritative value; drop any pending guess.
            _candidates.Remove(_icons[index].Id);
            return change;
        }

        var state = _icons[index];
        if (state.CallbackMessage != 0)
        {
            return change; // a real callback is on record: the unflagged raw fields have nothing to teach.
        }

        var observed = (Callback: command.CallbackMessage, Version: command.Version);
        if (_candidates.TryGetValue(state.Id, out var candidate) && candidate == observed)
        {
            // Second identical observation: adopt both values from the confirmed pair, atomically.
            _candidates.Remove(state.Id);
            _icons[index] = state with { CallbackMessage = observed.Callback, Version = observed.Version };
            return change with { Icon = _icons[index], LearnedCallback = true };
        }

        if (observed.Callback is < MinimumPlausibleCallback or > MaximumPlausibleCallback
            || observed.Version is not (0 or TrayCallback.Version3 or TrayCallback.Version4))
        {
            // Junk (zero, a plain window message, a registered-message number, a balloon uTimeout union
            // value): neither store it nor clear an existing candidate — a good pair seen earlier may
            // still be confirmed by a later update.
            return change;
        }

        // First sighting of this pair; a differing plausible pair replaces the pending candidate.
        _candidates[state.Id] = observed;
        return change with { ObservedCallback = observed };
    }

    private TrayChange Add(NotifyIconCommand command, bool createdViaModify = false)
    {
        _icons.Add(TrayIconState.Create(command));
        return new TrayChange(TrayChangeKind.Added, _icons.Count - 1, _icons[^1], true, createdViaModify);
    }

    private TrayChange Update(int index, NotifyIconCommand command)
    {
        var previousId = _icons[index].Id;
        _icons[index] = _icons[index].With(command);

        // A GUID-identified icon may have moved to a new owner window (With follows it); a pending
        // candidate belongs to the icon, so it moves with the entry.
        if (_icons[index].Id != previousId && _candidates.Remove(previousId, out var candidate))
        {
            _candidates[_icons[index].Id] = candidate;
        }

        return new TrayChange(TrayChangeKind.Updated, index, _icons[index], true);
    }
}
