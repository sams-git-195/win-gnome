using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiLocationPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_None_CallsTheGatedApis(bool asked) =>
        Assert.Equal(new WifiLocationDecision(true), WifiLocationPolicy.Decide(WifiLocationBlock.None, asked));

    [Fact]
    public void Decide_NotAsked_DoesNotCallUntilTheUserPressesTheButton() =>
        Assert.Equal(new WifiLocationDecision(false), WifiLocationPolicy.Decide(WifiLocationBlock.NotAsked, false));

    [Fact]
    public void Decide_NotAskedButUserAsked_Calls() =>
        Assert.Equal(new WifiLocationDecision(true), WifiLocationPolicy.Decide(WifiLocationBlock.NotAsked, true));

    [Theory]
    [InlineData(WifiLocationBlock.Policy, false)]
    [InlineData(WifiLocationBlock.Policy, true)]
    [InlineData(WifiLocationBlock.DeviceOff, false)]
    [InlineData(WifiLocationBlock.DeviceOff, true)]
    [InlineData(WifiLocationBlock.UserOff, false)]
    [InlineData(WifiLocationBlock.UserOff, true)]
    [InlineData(WifiLocationBlock.DesktopAppsOff, false)]
    [InlineData(WifiLocationBlock.DesktopAppsOff, true)]
    [InlineData(WifiLocationBlock.AppDenied, false)]
    [InlineData(WifiLocationBlock.AppDenied, true)]
    public void Decide_KnownReason_NeverCalls(WifiLocationBlock block, bool asked) =>
        Assert.Equal(new WifiLocationDecision(false), WifiLocationPolicy.Decide(block, asked));
}
