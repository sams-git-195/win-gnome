namespace WinGnome.Core.Tray;

/// <summary>The NIM_* operation of a Shell_NotifyIcon call.</summary>
public enum NotifyIconMessage : uint
{
    Add = 0,
    Modify = 1,
    Delete = 2,
    SetFocus = 3,
    SetVersion = 4,
}

/// <summary>NIF_* flags: which fields of a Shell_NotifyIcon call are valid.</summary>
[Flags]
public enum NotifyIconFields : uint
{
    None = 0,
    Message = 0x01,
    Icon = 0x02,
    Tip = 0x04,
    State = 0x08,
    Info = 0x10,
    ItemGuid = 0x20,
    Realtime = 0x40,
    ShowTip = 0x80,
}

/// <summary>NIS_* icon states.</summary>
[Flags]
public enum NotifyIconStates : uint
{
    None = 0,
    Hidden = 0x01,
    SharedIcon = 0x02,
}

/// <summary>
/// Identifies a notification icon the way the shell does: by its GUID when the app supplied one, otherwise by the
/// owner window and the app-chosen ID.
/// </summary>
/// <param name="Owner">Window that receives the icon's callback messages.</param>
/// <param name="Id">App-defined icon ID (uID).</param>
/// <param name="ItemGuid">guidItem, or <see cref="Guid.Empty"/> when the call did not set NIF_GUID.</param>
public readonly record struct TrayIconId(nint Owner, uint Id, Guid ItemGuid)
{
    /// <summary>True when a call (or query) identified by <paramref name="query"/> refers to this icon.</summary>
    public bool IsIdentifiedBy(TrayIconId query) =>
        query.ItemGuid != Guid.Empty ? ItemGuid == query.ItemGuid : Owner == query.Owner && Id == query.Id;
}

/// <summary>One Shell_NotifyIcon call as received by a tray window (the NOTIFYICONDATA inside SHELLTRAYDATA).</summary>
/// <param name="Message">NIM_ADD, NIM_MODIFY, ...</param>
/// <param name="Owner">hWnd, sign-extended from the 32-bit wire format.</param>
/// <param name="Id">uID.</param>
/// <param name="Flags">uFlags.</param>
/// <param name="CallbackMessage">uCallbackMessage (valid with NIF_MESSAGE).</param>
/// <param name="Icon">hIcon (valid with NIF_ICON); owned by the caller and only valid during the call.</param>
/// <param name="Tip">szTip (valid with NIF_TIP).</param>
/// <param name="State">dwState (valid with NIF_STATE).</param>
/// <param name="StateMask">dwStateMask: which bits of <paramref name="State"/> to apply.</param>
/// <param name="Version">uVersion (valid with NIM_SETVERSION): 0, 3 or 4.</param>
/// <param name="ItemGuid">guidItem (valid with NIF_GUID).</param>
public sealed record NotifyIconCommand(
    NotifyIconMessage Message,
    nint Owner,
    uint Id,
    NotifyIconFields Flags,
    uint CallbackMessage,
    nint Icon,
    string Tip,
    NotifyIconStates State,
    NotifyIconStates StateMask,
    uint Version,
    Guid ItemGuid)
{
    public TrayIconId Key => new(Owner, Id, Has(NotifyIconFields.ItemGuid) ? ItemGuid : Guid.Empty);

    public bool Has(NotifyIconFields flag) => (Flags & flag) != 0;
}
