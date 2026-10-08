namespace WinGnome.Core.Input;

/// <summary>What a low-level keyboard hook should do with a key event.</summary>
public enum SuperKeyAction
{
    /// <summary>Let the event through untouched.</summary>
    PassThrough,

    /// <summary>Swallow the Windows key release (so Start does not open) and show the overview.</summary>
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
