namespace WinGnome.Core.Input;

/// <summary>What a low-level keyboard hook should do with a key event.</summary>
public enum SuperKeyAction
{
    /// <summary>Let the event through untouched.</summary>
    PassThrough,

    /// <summary>
    /// Stop Start from opening and show the overview. Do not simply drop the Windows key release: the key-down
    /// already reached the system, so Windows would treat the key as still held and turn the next keystrokes into
    /// Win+key shortcuts. Instead swallow it and inject (SendInput) an unassigned "mask" key tap (VK 0xE8) followed
    /// by the Windows key release; the intervening key stops Start from opening. Tag the injected events so the hook
    /// can recognise them (they come back through it).
    /// </summary>
    SuppressAndOpenOverview,
}

/// <summary>
/// Detects "Windows key pressed and released on its own". Feed it every key down/up from a low-level hook;
/// any other key or mouse click while the Windows key is held turns the press into a combo and disarms it.
/// </summary>
public sealed class SuperKeyStateMachine
{
    /// <summary>VK_LWIN.</summary>
    public const int VkLeftWin = 0x5B;

    /// <summary>VK_RWIN.</summary>
    public const int VkRightWin = 0x5C;

    private bool _leftHeld;
    private bool _rightHeld;
    private bool _armed;

    private bool AnyHeld => _leftHeld || _rightHeld;

    /// <summary>Handles a key-down event (auto-repeats included). Always passes the event through.</summary>
    public SuperKeyAction OnKeyDown(int vk)
    {
        if (IsWin(vk))
        {
            var wasHeld = AnyHeld;
            Set(vk, true);
            if (!wasHeld)
            {
                _armed = true;
            }
        }
        else if (AnyHeld)
        {
            _armed = false;
        }

        return SuperKeyAction.PassThrough;
    }

    /// <summary>Handles a key-up event. Releasing the last Windows key while still armed opens the overview.</summary>
    public SuperKeyAction OnKeyUp(int vk)
    {
        if (!IsWin(vk))
        {
            return SuperKeyAction.PassThrough;
        }

        Set(vk, false);
        if (AnyHeld)
        {
            return SuperKeyAction.PassThrough;
        }

        var open = _armed;
        _armed = false;
        return open ? SuperKeyAction.SuppressAndOpenOverview : SuperKeyAction.PassThrough;
    }

    /// <summary>Call on any mouse button press; it turns a held Windows key into a combo.</summary>
    public void OnMouseButton()
    {
        if (AnyHeld)
        {
            _armed = false;
        }
    }

    /// <summary>Forgets all state (for example after the hook was suspended).</summary>
    public void Reset()
    {
        _leftHeld = false;
        _rightHeld = false;
        _armed = false;
    }

    private static bool IsWin(int vk) => vk is VkLeftWin or VkRightWin;

    private void Set(int vk, bool held)
    {
        if (vk == VkLeftWin)
        {
            _leftHeld = held;
        }
        else
        {
            _rightHeld = held;
        }
    }
}
