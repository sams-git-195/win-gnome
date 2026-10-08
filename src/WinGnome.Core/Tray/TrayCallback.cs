namespace WinGnome.Core.Tray;

/// <summary>A pointer interaction with a tray icon in the bar.</summary>
public enum TrayPointerAction
{
    /// <summary>The pointer moved onto the icon.</summary>
    Enter,
    /// <summary>The pointer has rested on the icon for the hover time (rich pop-up UI may open).</summary>
    Hover,
    /// <summary>The pointer left the icon after <see cref="Hover"/>.</summary>
    Leave,
    LeftDown,
    LeftUp,
    LeftDoubleClick,
    RightDown,
    RightUp,
    RightDoubleClick,
    MiddleDown,
    MiddleUp,
    MiddleDoubleClick,
}

/// <summary>
/// Builds the callback messages the shell sends to an icon's owner, in both formats: legacy and version 3 put the
/// icon ID in wParam and the event in lParam; version 4 packs the event and icon ID into lParam and puts the
/// anchor point (screen pixels) in wParam.
/// </summary>
public static class TrayCallback
{
    public const uint Version3 = 3;
    public const uint Version4 = 4;

    public const int ContextMenu = 0x007B;
    public const int MouseMove = 0x0200;
    public const int LeftButtonDown = 0x0201;
    public const int LeftButtonUp = 0x0202;
    public const int LeftButtonDoubleClick = 0x0203;
    public const int RightButtonDown = 0x0204;
    public const int RightButtonUp = 0x0205;
    public const int RightButtonDoubleClick = 0x0206;
    public const int MiddleButtonDown = 0x0207;
    public const int MiddleButtonUp = 0x0208;
    public const int MiddleButtonDoubleClick = 0x0209;

    public const int Select = 0x0400;
    public const int PopupOpen = 0x0406;
    public const int PopupClose = 0x0407;

    /// <summary>
    /// The notifications for <paramref name="action"/>, in the order Explorer sends them. Version 3+ icons also get
    /// NIN_SELECT after a left click and WM_CONTEXTMENU after a right click; version 4 icons without a standard
    /// tooltip get NIN_POPUPOPEN/NIN_POPUPCLOSE around a hover.
    /// </summary>
    public static IReadOnlyList<int> Notifications(TrayPointerAction action, TrayIconState icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        var modern = icon.Version >= Version3;
        var richPopup = icon.Version >= Version4 && !icon.ShowTip;
        return action switch
        {
            TrayPointerAction.Enter => [MouseMove],
            TrayPointerAction.Hover => richPopup ? [PopupOpen] : [],
            TrayPointerAction.Leave => richPopup ? [PopupClose] : [],
            TrayPointerAction.LeftDown => [LeftButtonDown],
            TrayPointerAction.LeftUp => modern ? [LeftButtonUp, Select] : [LeftButtonUp],
            TrayPointerAction.LeftDoubleClick => [LeftButtonDoubleClick],
            TrayPointerAction.RightDown => [RightButtonDown],
            TrayPointerAction.RightUp => modern ? [RightButtonUp, ContextMenu] : [RightButtonUp],
            TrayPointerAction.RightDoubleClick => [RightButtonDoubleClick],
            TrayPointerAction.MiddleDown => [MiddleButtonDown],
            TrayPointerAction.MiddleUp => [MiddleButtonUp],
            TrayPointerAction.MiddleDoubleClick => [MiddleButtonDoubleClick],
            _ => [],
        };
    }

    /// <summary>Button presses may open the app's menu or window, so the app must be allowed to take the foreground.</summary>
    public static bool MayTakeForeground(TrayPointerAction action) =>
        action is not (TrayPointerAction.Enter or TrayPointerAction.Hover or TrayPointerAction.Leave);

    /// <summary>wParam and lParam of the callback message for one notification.</summary>
    /// <param name="version">The icon's NOTIFYICON_VERSION.</param>
    /// <param name="iconId">The icon's uID.</param>
    /// <param name="notification">WM_* mouse message or NIN_* notification.</param>
    /// <param name="anchorX">Version 4 only: anchor point, screen pixels.</param>
    /// <param name="anchorY">Version 4 only: anchor point, screen pixels.</param>
    public static (nint WParam, nint LParam) Encode(uint version, uint iconId, int notification, int anchorX, int anchorY) =>
        version >= Version4
            ? (PackPoint(anchorX, anchorY), Pack(notification, (int)iconId))
            : ((nint)iconId, notification);

    /// <summary>MAKELONG(x, y): signed 16-bit coordinates, so GET_X_LPARAM/GET_Y_LPARAM read negative ones back.</summary>
    public static nint PackPoint(int x, int y) => Pack(x, y);

    private static nint Pack(int low, int high) => (nint)(uint)((ushort)low | ((uint)(ushort)high << 16));
}
