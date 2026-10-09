using WinGnome.Core.Input;

namespace WinGnome.Core.Shell;

public enum ShellAction
{
    FileExplorer,
    Run,
    Settings,
    ShowDesktop,
    ScreenClip,
    SnapLeft,
    SnapRight,
    Maximise,
    Minimise,
}

/// <summary>A shell-mode binding that collides with a user-configured hotkey.</summary>
public readonly record struct ShellHotkeyConflict(Hotkey Hotkey, ShellAction Action);

/// <summary>
/// The Win+ hotkeys Explorer normally owns and WinGnome registers itself in shell mode. Win+L and
/// Ctrl+Alt+Del belong to Winlogon and are never bound. "Win alone" (the overview) is handled by
/// <see cref="SuperKeyStateMachine"/>, not here.
/// </summary>
public sealed class ShellHotkeyMap
{
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;

    public static ShellHotkeyMap Default { get; } = new(
    [
        (new Hotkey(HotkeyModifiers.Win, 'E'), ShellAction.FileExplorer),
        (new Hotkey(HotkeyModifiers.Win, 'R'), ShellAction.Run),
        (new Hotkey(HotkeyModifiers.Win, 'I'), ShellAction.Settings),
        (new Hotkey(HotkeyModifiers.Win, 'D'), ShellAction.ShowDesktop),
        (new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, 'S'), ShellAction.ScreenClip),
        (new Hotkey(HotkeyModifiers.Win, VkLeft), ShellAction.SnapLeft),
        (new Hotkey(HotkeyModifiers.Win, VkRight), ShellAction.SnapRight),
        (new Hotkey(HotkeyModifiers.Win, VkUp), ShellAction.Maximise),
        (new Hotkey(HotkeyModifiers.Win, VkDown), ShellAction.Minimise),
    ]);

    private readonly (Hotkey Hotkey, ShellAction Action)[] _bindings;

    private ShellHotkeyMap((Hotkey, ShellAction)[] bindings) => _bindings = bindings;

    public IReadOnlyList<(Hotkey Hotkey, ShellAction Action)> Bindings => _bindings;

    /// <summary>Exact match on modifiers and key: Win+S is not Win+Shift+S.</summary>
    public bool TryGetAction(HotkeyModifiers modifiers, int virtualKey, out ShellAction action)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Hotkey.Modifiers == modifiers && binding.Hotkey.VirtualKey == virtualKey)
            {
                action = binding.Action;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>The shell binding for an action.</summary>
    public Hotkey GetHotkey(ShellAction action) => _bindings.First(b => b.Action == action).Hotkey;

    /// <summary>User hotkeys (such as the overview hotkey) that equal a shell binding, in the user's order.</summary>
    public IReadOnlyList<ShellHotkeyConflict> FindConflicts(IEnumerable<Hotkey> userHotkeys)
    {
        var conflicts = new List<ShellHotkeyConflict>();
        foreach (var hotkey in userHotkeys)
        {
            if (TryGetAction(hotkey.Modifiers, hotkey.VirtualKey, out var action))
            {
                conflicts.Add(new ShellHotkeyConflict(hotkey, action));
            }
        }

        return conflicts;
    }

    /// <summary>Hotkeys Winlogon owns (Win+L): WinGnome can't register them and shouldn't offer them.</summary>
    public static bool IsOperatingSystemOwned(Hotkey hotkey) =>
        hotkey.Modifiers == HotkeyModifiers.Win && hotkey.VirtualKey == 'L';
}
