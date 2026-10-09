namespace WinGnome.Core.ControlCenter;

/// <summary>
/// The <c>STICKYKEYS.dwFlags</c> bits WinGnome reads and changes. Only <see cref="On"/> is ever changed; every other
/// bit (Windows' Shift ×5 shortcut, its confirmation, sounds, the indicator) is kept as Windows reported it.
/// </summary>
public static class StickyKeysFlags
{
    /// <summary>SKF_STICKYKEYSON: sticky keys are on.</summary>
    public const uint On = 0x0001;

    /// <summary>SKF_AVAILABLE: the feature is available; a valid read always has it.</summary>
    public const uint Available = 0x0002;

    /// <summary>SKF_HOTKEYACTIVE: pressing Shift five times toggles sticky keys.</summary>
    public const uint HotkeyActive = 0x0004;

    /// <summary>SKF_CONFIRMHOTKEY: Windows asks before the shortcut turns sticky keys on.</summary>
    public const uint ConfirmHotkey = 0x0008;

    /// <summary>Windows' defaults, used in place of a read that lacks <see cref="Available"/>.</summary>
    public const uint Defaults = Available | HotkeyActive | ConfirmHotkey;

    public static bool IsOn(uint flags) => (flags & On) != 0;

    /// <summary>
    /// The flags to write to turn sticky keys on or off. A read without <see cref="Available"/> (a failed or zeroed
    /// read) is replaced by <see cref="Defaults"/>, so a bad read can never write a state the keyboard shortcut can't
    /// undo; a valid read is kept bit for bit apart from <see cref="On"/>.
    /// </summary>
    public static uint With(uint current, bool on)
    {
        var flags = (current & Available) != 0 ? current : Defaults;
        return on ? flags | On : flags & ~On;
    }
}
