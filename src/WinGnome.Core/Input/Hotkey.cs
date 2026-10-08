namespace WinGnome.Core.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,      // MOD_ALT
    Control = 0x2,  // MOD_CONTROL
    Shift = 0x4,    // MOD_SHIFT
    Win = 0x8,      // MOD_WIN
}

/// <summary>A global hotkey such as "Ctrl+Alt+T", using Win32 virtual-key codes and MOD_* flags.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    // Canonical name first for each key; aliases follow and are only used for parsing.
    private static readonly (string Name, int Vk)[] KeyTable = BuildKeyTable();

    /// <summary>Parses "Modifier+...+Key". Modifiers: Ctrl/Control, Alt, Shift, Win/Super/Meta. Case-insensitive.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "SUPER" or "META" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (modifier == HotkeyModifiers.None || modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        var keyName = parts[^1];
        var match = Array.Find(KeyTable, k => string.Equals(k.Name, keyName, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null)
        {
            return false;
        }

        // Without modifiers only function keys are allowed; anything else would swallow normal typing.
        var isFunctionKey = match.Vk is >= 0x70 and <= 0x87;
        if (modifiers == HotkeyModifiers.None && !isFunctionKey)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, match.Vk);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        var vk = VirtualKey;
        var name = Array.Find(KeyTable, k => k.Vk == vk).Name;
        parts.Add(name ?? $"0x{VirtualKey:X2}");
        return string.Join('+', parts);
    }

    private static (string, int)[] BuildKeyTable()
    {
        var table = new List<(string, int)>
        {
            ("Space", 0x20), ("Spacebar", 0x20),
            ("Enter", 0x0D), ("Return", 0x0D),
            ("Tab", 0x09),
            ("Esc", 0x1B), ("Escape", 0x1B),
            ("Backspace", 0x08),
            ("Home", 0x24), ("End", 0x23),
            ("PageUp", 0x21), ("PageDown", 0x22),
            ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
            ("Insert", 0x2D), ("Delete", 0x2E),
            ("Grave", 0xC0), ("Minus", 0xBD), ("Plus", 0xBB), ("Comma", 0xBC), ("Period", 0xBE), ("Slash", 0xBF),
        };
        for (var c = 'A'; c <= 'Z'; c++) table.Add((c.ToString(), c));
        for (var d = '0'; d <= '9'; d++) table.Add((d.ToString(), d));
        for (var f = 1; f <= 24; f++) table.Add(($"F{f}", 0x6F + f));
        return [.. table];
    }
}
