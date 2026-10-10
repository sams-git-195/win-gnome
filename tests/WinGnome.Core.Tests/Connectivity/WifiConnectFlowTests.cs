using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiConnectFlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static WifiConnectCommand Set(bool overwrite) => WifiConnectCommand.SetProfile(overwrite);

    private static WifiConnectCommand Connect => WifiConnectCommand.Connect();

    private static WifiConnectCommand Delete => WifiConnectCommand.DeleteProfile();

    private static WifiConnectCommand Prompt(bool retry) => WifiConnectCommand.PromptPassword(retry);

    /// <summary>The password was entered and Windows accepted the profile write.</summary>
    private static void Submit(WifiConnectFlow flow)
    {
        flow.PasswordSubmitted(flow.Attempt, T0);
        flow.ProfileWritten(flow.Attempt);
    }

    [Fact]
    public void Begin_SavedNetwork_ConnectsWithoutTouchingTheProfile()
    {
        var flow = new WifiConnectFlow();

        var commands = flow.Begin(WifiProfileKind.Wpa2Psk, isSaved: true, T0);

        Assert.Equal([Connect], commands);
        Assert.Equal(WifiProfileOwnership.None, flow.Ownership);
        Assert.Equal(WifiConnectState.Connecting, flow.State);
    }

    [Fact]
    public void Begin_OpenUnsavedNetwork_CreatesAProfileAndConnects()
    {
        var flow = new WifiConnectFlow();

        var commands = flow.Begin(WifiProfileKind.Open, isSaved: false, T0);

        Assert.Equal([Set(false), Connect], commands);
        flow.ProfileWritten(flow.Attempt);
        Assert.Equal(WifiProfileOwnership.Created, flow.Ownership);
    }

    [Fact]
    public void Begin_SecuredUnsavedNetwork_AsksForThePassword()
    {
        var flow = new WifiConnectFlow();

        var commands = flow.Begin(WifiProfileKind.Wpa2Psk, isSaved: false, T0);

        Assert.Equal([Prompt(false)], commands);
        Assert.Equal(WifiConnectState.AwaitingPassword, flow.State);
    }

    [Fact]
    public void Begin_Enterprise_HandsOff()
    {
        var flow = new WifiConnectFlow();

        Assert.Equal([WifiConnectCommand.HandOff()], flow.Begin(WifiProfileKind.HandOff, isSaved: false, T0));
        Assert.Equal(WifiConnectState.HandedOff, flow.State);
    }

    [Fact]
    public void PasswordSubmitted_NewNetwork_WritesANewProfileAndConnects()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);

        var commands = flow.PasswordSubmitted(flow.Attempt, T0);
        flow.ProfileWritten(flow.Attempt);

        Assert.Equal([Set(false), Connect], commands);
        Assert.Equal(WifiProfileOwnership.Created, flow.Ownership);
    }

    [Fact]
    public void WrongPassword_OnNewNetwork_DeletesTheProfileAndAsksAgain()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);

        var commands = flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);

        Assert.Equal([Delete, Prompt(true)], commands);
        Assert.Equal(WifiConnectState.AwaitingPassword, flow.State);
        Assert.Equal(WifiProfileOwnership.None, flow.Ownership);
    }

    [Fact]
    public void RetryAfterADeletedProfile_CreatesItAgainWithoutOverwrite()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);
        flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);

        var commands = flow.PasswordSubmitted(flow.Attempt, T0);

        Assert.Equal([Set(false), Connect], commands);
    }

    [Fact]
    public void SavedNetworkWithAStaleKey_PromptsAndTheRetryOverwritesTheProfile()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, isSaved: true, T0);

        var prompt = flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);
        var retry = flow.PasswordSubmitted(flow.Attempt, T0);
        flow.ProfileWritten(flow.Attempt);

        Assert.Equal([Prompt(true)], prompt);
        Assert.Equal([Set(true), Connect], retry);
        Assert.Equal(WifiProfileOwnership.Overwritten, flow.Ownership);
    }

    [Fact]
    public void SavedNetworkWithAStaleKey_AWrongRetryDeletesTheOverwrittenProfile()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, isSaved: true, T0);
        flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);
        Submit(flow);

        var commands = flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);

        Assert.Equal([Delete, Prompt(true)], commands);
    }

    [Fact]
    public void SavedNetworkWithAStaleKey_CancellingTheFirstPromptKeepsTheSavedProfile()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, isSaved: true, T0);
        flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);

        Assert.Empty(flow.Cancel(flow.Attempt));
        Assert.Equal(WifiConnectState.Cancelled, flow.State);
    }

    [Fact]
    public void Cancel_WhileConnectingWithACreatedProfile_DeletesIt()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);

        Assert.Equal([Delete], flow.Cancel(flow.Attempt));
    }

    [Fact]
    public void Cancel_WhileConnectingWithAnOverwrittenProfile_DeletesIt()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, true, T0);
        flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);
        Submit(flow);

        Assert.Equal([Delete], flow.Cancel(flow.Attempt));
    }

    [Fact]
    public void Cancel_AfterTheProfileWasDeleted_DoesNotDeleteAgain()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);
        flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);

        Assert.Empty(flow.Cancel(flow.Attempt));
    }

    [Fact]
    public void Success_KeepsTheProfile()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);

        var commands = flow.Result(flow.Attempt, WlanReasonClass.Success);

        Assert.Empty(commands);
        Assert.Equal(WifiConnectState.Connected, flow.State);
        Assert.Null(flow.Deadline);
    }

    [Fact]
    public void NetworkNotAvailable_FailsWithoutDeleting()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);

        Assert.Empty(flow.Result(flow.Attempt, WlanReasonClass.NetworkNotAvailable));
        Assert.Equal(WifiConnectState.Failed, flow.State);
    }

    [Fact]
    public void InvalidProfile_DeletesTheProfileItCreated()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);

        Assert.Equal([Delete], flow.Result(flow.Attempt, WlanReasonClass.ProfileInvalid));
    }

    [Fact]
    public void AuthFailureOnASavedOpenNetwork_FailsInsteadOfAskingForAPassword()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Open, isSaved: true, T0);

        Assert.Empty(flow.Result(flow.Attempt, WlanReasonClass.AuthFailure));
        Assert.Equal(WifiConnectState.Failed, flow.State);
    }

    [Fact]
    public void CheckTimeout_JustBeforeThirtySeconds_KeepsWaiting()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Open, false, T0);

        Assert.Empty(flow.CheckTimeout(flow.Attempt, T0.AddSeconds(30).AddTicks(-1)));
        Assert.Equal(WifiConnectState.Connecting, flow.State);
    }

    [Fact]
    public void CheckTimeout_AtExactlyThirtySeconds_TimesOutAndDeletesTheCreatedProfile()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Open, false, T0);
        flow.ProfileWritten(flow.Attempt);

        var commands = flow.CheckTimeout(flow.Attempt, T0.AddSeconds(30));

        Assert.Equal([Delete], commands);
        Assert.Equal(WifiConnectState.TimedOut, flow.State);
    }

    [Fact]
    public void CheckTimeout_SavedProfileUsedAsIs_IsNeverDeleted()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, true, T0);

        Assert.Empty(flow.CheckTimeout(flow.Attempt, T0.AddSeconds(31)));
        Assert.Equal(WifiConnectState.TimedOut, flow.State);
    }

    [Fact]
    public void CheckTimeout_TimeStartsWhenConnectingStarts_NotWhenThePromptOpened()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        flow.PasswordSubmitted(flow.Attempt, T0.AddSeconds(100));

        Assert.Empty(flow.CheckTimeout(flow.Attempt, T0.AddSeconds(129)));
        Assert.Equal(T0.AddSeconds(130), flow.Deadline);
    }

    [Fact]
    public void ASupersededAttempt_IsIgnoredEverywhere()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);
        var old = flow.Attempt;
        flow.Begin(WifiProfileKind.Open, isSaved: true, T0);

        Assert.Empty(flow.Result(old, WlanReasonClass.AuthFailure));
        Assert.Empty(flow.CheckTimeout(old, T0.AddMinutes(5)));
        Assert.Empty(flow.Cancel(old));
        Assert.Empty(flow.PasswordSubmitted(old, T0));
        Assert.Equal(WifiConnectState.Connecting, flow.State);
    }

    [Fact]
    public void PasswordSubmitted_WhenNotAwaitingOne_DoesNothing()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, true, T0);

        Assert.Empty(flow.PasswordSubmitted(flow.Attempt, T0));
    }

    [Fact]
    public void ProfileWriteFailed_NothingIsOwnedSoNothingIsDeleted()
    {
        // WlanSetProfile refused (for example the profile already existed): whatever is saved under that name is not ours.
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        flow.PasswordSubmitted(flow.Attempt, T0);

        Assert.Empty(flow.ProfileWriteFailed(flow.Attempt));
        Assert.Equal(WifiConnectState.Failed, flow.State);
        Assert.Equal(WifiProfileOwnership.None, flow.Ownership);
        Assert.Empty(flow.Cancel(flow.Attempt));
    }

    [Fact]
    public void ProfileWriteFailed_OnAnOverwrite_DoesNotDeleteTheSavedProfile()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, isSaved: true, T0);
        flow.Result(flow.Attempt, WlanReasonClass.AuthFailure);
        flow.PasswordSubmitted(flow.Attempt, T0);

        Assert.Empty(flow.ProfileWriteFailed(flow.Attempt));
        Assert.Empty(flow.CheckTimeout(flow.Attempt, T0.AddMinutes(5)));
    }

    [Fact]
    public void ConnectFailsAfterASuccessfulWrite_DeletesTheProfileItWrote()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        flow.PasswordSubmitted(flow.Attempt, T0);
        flow.ProfileWritten(flow.Attempt);

        Assert.Equal([Delete], flow.Cancel(flow.Attempt));
    }

    [Fact]
    public void ProfileWritten_ForASupersededAttempt_ClaimsNothing()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        flow.PasswordSubmitted(flow.Attempt, T0);
        var old = flow.Attempt;
        flow.Begin(WifiProfileKind.Wpa2Psk, true, T0);

        flow.ProfileWritten(old);

        Assert.Equal(WifiProfileOwnership.None, flow.Ownership);
    }

    [Fact]
    public void Abandon_KeepsTheProfile_AndIgnoresTheAttemptsLateResults()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.Wpa2Psk, false, T0);
        Submit(flow);
        var attempt = flow.Attempt;

        flow.Abandon();

        Assert.Equal(WifiConnectState.Idle, flow.State);
        Assert.Equal(WifiProfileOwnership.None, flow.Ownership);
        Assert.Empty(flow.Cancel(attempt));
        Assert.Empty(flow.Result(attempt, WlanReasonClass.AuthFailure));
        Assert.Empty(flow.CheckTimeout(attempt, T0.AddMinutes(5)));
    }

    [Fact]
    public void Begin_SavedEnterpriseNetwork_Connects()
    {
        var flow = new WifiConnectFlow();

        Assert.Equal([Connect], flow.Begin(WifiProfileKind.HandOff, isSaved: true, T0));
        Assert.Equal(WifiConnectState.Connecting, flow.State);
    }

    [Fact]
    public void AuthFailureOnASavedEnterpriseNetwork_Fails()
    {
        var flow = new WifiConnectFlow();
        flow.Begin(WifiProfileKind.HandOff, isSaved: true, T0);

        Assert.Empty(flow.Result(flow.Attempt, WlanReasonClass.AuthFailure));
        Assert.Equal(WifiConnectState.Failed, flow.State);
    }
}
