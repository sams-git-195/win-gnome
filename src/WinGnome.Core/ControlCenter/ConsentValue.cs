namespace WinGnome.Core.ControlCenter;

/// <summary>What a <c>CapabilityAccessManager\ConsentStore</c> <c>Value</c> says.</summary>
public enum ConsentState
{
    /// <summary>No value stored: Windows treats it as allowed.</summary>
    Absent,
    Allow,
    Deny,
    /// <summary>"Ask each time": shown as off; turning it on stores Allow.</summary>
    Prompt,
    /// <summary>Any other text. Shown as off and left alone: WinGnome won't overwrite a value it doesn't understand.</summary>
    Unknown,
}

/// <summary>The REG_SZ values of the privacy consent store.</summary>
public static class ConsentValue
{
    /// <summary>Reads the stored text (case-insensitive); null or blank means <see cref="ConsentState.Absent"/>.</summary>
    public static ConsentState Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ConsentState.Absent;
        }

        return text.Trim().ToUpperInvariant() switch
        {
            "ALLOW" => ConsentState.Allow,
            "DENY" => ConsentState.Deny,
            "PROMPT" => ConsentState.Prompt,
            _ => ConsentState.Unknown,
        };
    }

    /// <summary>True when the value lets the app use the device (an absent value does).</summary>
    public static bool IsAllowed(ConsentState state) => state is ConsentState.Absent or ConsentState.Allow;

    /// <summary>The text to store for a switch turned on or off.</summary>
    public static string ToText(bool allow) => allow ? "Allow" : "Deny";
}

/// <summary>How a switch is shown: its position and whether it can be changed.</summary>
public sealed record ConsentRowState(bool IsOn, bool CanChange);

/// <summary>Combines the device-wide, per-user and per-app consent values into what a row shows.</summary>
public static class ConsentEffective
{
    /// <summary>
    /// A per-app switch. With the device-wide switch off every row shows off and can't be changed (Windows ignores
    /// the per-app value then). With the user's "Let apps access" off the row keeps its own value but can't be changed.
    /// A value WinGnome doesn't understand shows off and can't be changed.
    /// </summary>
    public static ConsentRowState For(ConsentState device, ConsentState user, ConsentState app)
    {
        if (!ConsentValue.IsAllowed(device))
        {
            return new ConsentRowState(false, false);
        }

        return new ConsentRowState(ConsentValue.IsAllowed(app), ConsentValue.IsAllowed(user) && app != ConsentState.Unknown);
    }

    /// <summary>
    /// A "Let apps access" or "Let desktop apps access" switch. Off and unchangeable while the device-wide switch is off,
    /// which only an administrator can change.
    /// </summary>
    public static ConsentRowState ForMaster(ConsentState device, ConsentState master)
    {
        if (!ConsentValue.IsAllowed(device))
        {
            return new ConsentRowState(false, false);
        }

        return new ConsentRowState(ConsentValue.IsAllowed(master), master != ConsentState.Unknown);
    }
}
