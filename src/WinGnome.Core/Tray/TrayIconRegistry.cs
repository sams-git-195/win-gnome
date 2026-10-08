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
public readonly record struct TrayChange(TrayChangeKind Kind, int Index, TrayIconState? Icon, bool Accepted)
{
    public static TrayChange Rejected => new(TrayChangeKind.None, -1, null, false);
}

/// <summary>
/// The notification-area icon list, in the order icons were added, driven by Shell_NotifyIcon calls exactly as the
/// shell interprets them. Not thread-safe: one thread (the tray window's) owns it.
/// </summary>
public sealed class TrayIconRegistry
{
    private readonly List<TrayIconState> _icons = [];

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
                return index >= 0 ? Update(index, command) : Add(command);

            case NotifyIconMessage.Modify:
                return index >= 0 ? Update(index, command)
                    : knownToShell ? Add(command)
                    : TrayChange.Rejected;

            case NotifyIconMessage.Delete:
                if (index < 0)
                {
                    return TrayChange.Rejected;
                }

                var removed = _icons[index];
                _icons.RemoveAt(index);
                return new TrayChange(TrayChangeKind.Removed, index, removed, true);

            case NotifyIconMessage.SetVersion:
                if (index < 0)
                {
                    return TrayChange.Rejected;
                }

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
                changes.Add(new TrayChange(TrayChangeKind.Removed, i, _icons[i], true));
                _icons.RemoveAt(i);
            }
        }

        return changes;
    }

    public int IndexOf(TrayIconId query) => _icons.FindIndex(icon => icon.Id.IsIdentifiedBy(query));

    private TrayChange Add(NotifyIconCommand command)
    {
        _icons.Add(TrayIconState.Create(command));
        return new TrayChange(TrayChangeKind.Added, _icons.Count - 1, _icons[^1], true);
    }

    private TrayChange Update(int index, NotifyIconCommand command)
    {
        _icons[index] = _icons[index].With(command);
        return new TrayChange(TrayChangeKind.Updated, index, _icons[index], true);
    }
}
