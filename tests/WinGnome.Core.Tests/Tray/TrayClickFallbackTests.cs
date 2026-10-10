using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class TrayClickFallbackTests
{
    [Theory]
    [InlineData("securityhealthsystray.exe")]
    [InlineData("SecurityHealthSystray.EXE")] // case-insensitive
    [InlineData(@"C:\Windows\System32\SecurityHealthSystray.exe")] // the app layer resolves full paths
    [InlineData("/Windows/System32/SecurityHealthSystray.exe")]
    public void LaunchUriFor_KnownProcess_ReturnsTheMappedUri(string processFileName) =>
        Assert.Equal("windowsdefender://", TrayClickFallback.LaunchUriFor(processFileName));

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("securityhealthsystray2.exe")] // similar, not the same
    [InlineData("SecurityHealthService.exe")] // the service, not the tray
    [InlineData("securityhealthsystray")] // no extension
    [InlineData("windowsdefender.exe")]
    public void LaunchUriFor_UnknownProcess_ReturnsNull(string processFileName) =>
        Assert.Null(TrayClickFallback.LaunchUriFor(processFileName));

    [Fact]
    public void ShouldLaunch_FirstClick_Launches() =>
        Assert.True(TrayClickFallback.ShouldLaunch(10_000, null));

    [Fact]
    public void ShouldLaunch_InsideTheGap_DoesNotLaunch()
    {
        Assert.False(TrayClickFallback.ShouldLaunch(10_000, 10_000)); // same instant: one double-click sequence
        Assert.False(TrayClickFallback.ShouldLaunch(10_749, 10_000)); // gap − 1
    }

    [Fact]
    public void ShouldLaunch_AtOrAfterTheGap_Launches()
    {
        Assert.True(TrayClickFallback.ShouldLaunch(10_750, 10_000)); // exactly the gap
        Assert.True(TrayClickFallback.ShouldLaunch(99_999, 10_000));
    }
}
