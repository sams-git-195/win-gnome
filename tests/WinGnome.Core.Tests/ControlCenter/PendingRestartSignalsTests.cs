using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class PendingRestartSignalsTests
{
    [Fact]
    public void NoSignals_NeedsNothing()
    {
        var signals = new PendingRestartSignals(false, false, false, null);

        Assert.False(signals.RestartNeeded);
        Assert.False(signals.InstallOnShutdownAvailable);
    }

    [Fact]
    public void FlyoutZero_NeedsNothing()
    {
        var signals = new PendingRestartSignals(false, false, false, 0);

        Assert.False(signals.RestartNeeded);
        Assert.False(signals.InstallOnShutdownAvailable);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void AnyRebootSignalAlone_NeedsRestartWithoutOfferingInstall(bool wua, bool autoUpdate, bool cbs)
    {
        var signals = new PendingRestartSignals(wua, autoUpdate, cbs, null);

        Assert.True(signals.RestartNeeded);
        Assert.False(signals.InstallOnShutdownAvailable);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public void NonZeroFlyout_OffersInstallWithoutSayingRestartNeeded(int flyout)
    {
        var signals = new PendingRestartSignals(false, false, false, flyout);

        Assert.False(signals.RestartNeeded);
        Assert.True(signals.InstallOnShutdownAvailable);
    }
}
