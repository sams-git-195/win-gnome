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
/// are ignored. A profile is deleted only when this attempt created it or replaced its key <em>and Windows confirmed the
/// write</em> (<see cref="ProfileWritten"/>): a write that failed, for example because the profile already existed,
/// never makes the attempt the owner of whatever is saved under that name. A saved profile that was used as is is
/// never deleted.
/// </summary>
public sealed class WifiConnectFlow(TimeSpan timeout)
{
    private static readonly IReadOnlyList<WifiConnectCommand> None = [];

    private WifiProfileKind _kind;
    private bool _profileSaved;
    private WifiProfileOwnership _pendingOwnership;

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
        _pendingOwnership = WifiProfileOwnership.None;
        Deadline = null;

        // A saved profile connects whatever its security (a saved enterprise network works from the panel); only
        // an unsaved network WinGnome can't set up is handed off.
        if (isSaved)
        {
            return StartConnecting(now, WifiConnectCommand.Connect());
        }

        if (kind == WifiProfileKind.HandOff)
        {
            State = WifiConnectState.HandedOff;
            return [WifiConnectCommand.HandOff()];
        }

        if (!WifiSecurity.NeedsPassword(kind))
        {
            _pendingOwnership = WifiProfileOwnership.Created;
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
            _pendingOwnership = WifiProfileOwnership.Overwritten;
            return StartConnecting(now, WifiConnectCommand.SetProfile(true), WifiConnectCommand.Connect());
        }

        _pendingOwnership = WifiProfileOwnership.Created;
        return StartConnecting(now, WifiConnectCommand.SetProfile(false), WifiConnectCommand.Connect());
    }

    /// <summary>Windows confirmed the profile write: from now on this attempt owns the profile it wrote.</summary>
    public void ProfileWritten(int attempt)
    {
        if (attempt == Attempt && State == WifiConnectState.Connecting)
        {
            Ownership = _pendingOwnership;
        }
    }

    /// <summary>
    /// Windows refused the profile write. The attempt fails and owns nothing, so nothing is deleted: whatever is saved
    /// under that name is not ours.
    /// </summary>
    public IReadOnlyList<WifiConnectCommand> ProfileWriteFailed(int attempt)
    {
        if (attempt != Attempt || State != WifiConnectState.Connecting)
        {
            return None;
        }

        State = WifiConnectState.Failed;
        Deadline = null;
        _pendingOwnership = WifiProfileOwnership.None;
        return None;
    }

    /// <summary>
    /// The panel is going away while an attempt may still be running. Windows carries on connecting, so nothing is
    /// deleted (a profile this attempt wrote stays saved and can be forgotten from the list); later results of the
    /// attempt are ignored (the flow is idle).
    /// </summary>
    public void Abandon()
    {
        State = WifiConnectState.Idle;
        Ownership = WifiProfileOwnership.None;
        _pendingOwnership = WifiProfileOwnership.None;
        Deadline = null;
    }

    /// <summary>The user cancelled the dialog, or the connection failed to start.</summary>
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
        _pendingOwnership = WifiProfileOwnership.None;
        _profileSaved = false;
        return [WifiConnectCommand.DeleteProfile()];
    }
}
