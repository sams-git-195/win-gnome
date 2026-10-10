namespace WinGnome.Core.Connectivity;

public enum WifiConnectState
{
    Idle,
    AwaitingPassword,
    Connecting,
    Connected,
    Failed,
    TimedOut,
    Cancelled,
    HandedOff,
}

/// <summary>Who owns the saved profile of the current attempt, which decides what a failure may delete.</summary>
public enum WifiProfileOwnership
{
    /// <summary>The attempt uses a profile that was already saved, untouched.</summary>
    None,
    /// <summary>The attempt created the profile.</summary>
    Created,
    /// <summary>The attempt replaced a saved profile's key.</summary>
    Overwritten,
}

public enum WifiConnectCommandKind
{
    /// <summary>Write the password into a profile (<see cref="WifiConnectCommand.Flag"/>: overwrite a saved one).</summary>
    SetProfile,
    /// <summary>Ask Windows to connect with the profile.</summary>
    Connect,
    DeleteProfile,
    /// <summary>Show the password dialog (<see cref="WifiConnectCommand.Flag"/>: the last password was not accepted).</summary>
    PromptPassword,
    /// <summary>Open Windows Settings; WinGnome can't set this network up.</summary>
    HandOff,
}

/// <summary>One thing the panel must do, in order.</summary>
public readonly record struct WifiConnectCommand(WifiConnectCommandKind Kind, bool Flag = false)
{
    public static WifiConnectCommand SetProfile(bool overwrite) => new(WifiConnectCommandKind.SetProfile, overwrite);

    public static WifiConnectCommand Connect() => new(WifiConnectCommandKind.Connect);

    public static WifiConnectCommand DeleteProfile() => new(WifiConnectCommandKind.DeleteProfile);

    public static WifiConnectCommand PromptPassword(bool retry) => new(WifiConnectCommandKind.PromptPassword, retry);

    public static WifiConnectCommand HandOff() => new(WifiConnectCommandKind.HandOff);
}

/// <summary>
/// The rules of connecting to a network, as a state machine with no Windows calls: what to write, when a failed
/// attempt must delete the profile it made, when to ask for the password again, and the 30 second limit. The panel
/// does what the returned commands say and reports back. A newer attempt supersedes an older one, whose late results
/// are ignored. A profile is deleted only when this attempt created it or replaced its key; a saved profile that was
/// used as is is never deleted.
/// </summary>
public sealed class WifiConnectFlow(TimeSpan timeout)
{
    private static readonly IReadOnlyList<WifiConnectCommand> None = [];

    private WifiProfileKind _kind;
    private bool _profileSaved;

    public WifiConnectFlow()
        : this(TimeSpan.FromSeconds(30))
    {
    }

    /// <summary>The newest attempt's number; results and cancellations must quote it.</summary>
    public int Attempt { get; private set; }

    public WifiConnectState State { get; private set; }

    public WifiProfileOwnership Ownership { get; private set; }

    /// <summary>When the current attempt gives up, or null while nothing is connecting.</summary>
    public DateTimeOffset? Deadline { get; private set; }

    /// <summary>Starts an attempt for a network of <paramref name="kind"/>, superseding any earlier one.</summary>
    public IReadOnlyList<WifiConnectCommand> Begin(WifiProfileKind kind, bool isSaved, DateTimeOffset now)
    {
        Attempt++;
        _kind = kind;
        _profileSaved = isSaved;
        Ownership = WifiProfileOwnership.None;
        Deadline = null;

        if (kind == WifiProfileKind.HandOff)
        {
            State = WifiConnectState.HandedOff;
            return [WifiConnectCommand.HandOff()];
        }

        if (isSaved)
        {
            return StartConnecting(now, WifiConnectCommand.Connect());
        }

        if (!WifiSecurity.NeedsPassword(kind))
        {
            Ownership = WifiProfileOwnership.Created;
            return StartConnecting(now, WifiConnectCommand.SetProfile(false), WifiConnectCommand.Connect());
        }

        State = WifiConnectState.AwaitingPassword;
        return [WifiConnectCommand.PromptPassword(false)];
    }

    /// <summary>The user entered a valid password and chose Connect.</summary>
    public IReadOnlyList<WifiConnectCommand> PasswordSubmitted(int attempt, DateTimeOffset now)
    {
        if (attempt != Attempt || State != WifiConnectState.AwaitingPassword)
        {
            return None;
        }

        if (_profileSaved)
        {
            Ownership = WifiProfileOwnership.Overwritten;
            return StartConnecting(now, WifiConnectCommand.SetProfile(true), WifiConnectCommand.Connect());
        }

        Ownership = WifiProfileOwnership.Created;
        return StartConnecting(now, WifiConnectCommand.SetProfile(false), WifiConnectCommand.Connect());
    }

    /// <summary>The user cancelled the dialog, or the panel is going away mid-attempt.</summary>
    public IReadOnlyList<WifiConnectCommand> Cancel(int attempt)
    {
        if (attempt != Attempt || State is not (WifiConnectState.AwaitingPassword or WifiConnectState.Connecting))
        {
            return None;
        }

        State = WifiConnectState.Cancelled;
        Deadline = null;
        return DeleteOwnedProfile();
    }

    /// <summary>Windows reported how the attempt ended.</summary>
    public IReadOnlyList<WifiConnectCommand> Result(int attempt, WlanReasonClass outcome)
    {
        if (attempt != Attempt || State != WifiConnectState.Connecting)
        {
            return None;
        }

        Deadline = null;
        switch (outcome)
        {
            case WlanReasonClass.Success:
                State = WifiConnectState.Connected;
                return None;
            case WlanReasonClass.AuthFailure when WifiSecurity.NeedsPassword(_kind):
                State = WifiConnectState.AwaitingPassword;
                return [.. DeleteOwnedProfile(), WifiConnectCommand.PromptPassword(true)];
            case WlanReasonClass.ProfileInvalid:
                State = WifiConnectState.Failed;
                return DeleteOwnedProfile();
            default:
                State = WifiConnectState.Failed;
                return None;
        }
    }

    /// <summary>Gives up on the attempt when its deadline has passed (exactly at the limit counts).</summary>
    public IReadOnlyList<WifiConnectCommand> CheckTimeout(int attempt, DateTimeOffset now)
    {
        if (attempt != Attempt || State != WifiConnectState.Connecting || Deadline is not { } deadline || now < deadline)
        {
            return None;
        }

        State = WifiConnectState.TimedOut;
        Deadline = null;
        return DeleteOwnedProfile();
    }

    private List<WifiConnectCommand> StartConnecting(DateTimeOffset now, params WifiConnectCommand[] commands)
    {
        State = WifiConnectState.Connecting;
        Deadline = now + timeout;
        return [.. commands];
    }

    private IReadOnlyList<WifiConnectCommand> DeleteOwnedProfile()
    {
        if (Ownership == WifiProfileOwnership.None)
        {
            return None;
        }

        // The profile is gone (a rejected key is never kept), so the next try starts from nothing.
        Ownership = WifiProfileOwnership.None;
        _profileSaved = false;
        return [WifiConnectCommand.DeleteProfile()];
    }
}
