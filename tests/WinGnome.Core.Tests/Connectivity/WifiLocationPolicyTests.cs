using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiLocationPolicyTests
{
    [Theory]
    [InlineData(WifiLocationAccess.Allowed, false)]
    [InlineData(WifiLocationAccess.Allowed, true)]
    [InlineData(WifiLocationAccess.Unknown, false)]
    public void Decide_AllowedOrUnknown_CallsTheGatedApis(WifiLocationAccess access, bool asked)
    {
        Assert.Equal(new WifiLocationDecision(true, false, false), WifiLocationPolicy.Decide(access, asked));
    }

    [Theory]
    [InlineData(WifiLocationAccess.DeniedByUser, false)]
    [InlineData(WifiLocationAccess.DeniedByUser, true)]
    [InlineData(WifiLocationAccess.DeniedBySystem, false)]
    [InlineData(WifiLocationAccess.DeniedBySystem, true)]
    public void Decide_Denied_NeverCallsAndHasNoButton(WifiLocationAccess access, bool asked)
    {
        Assert.Equal(new WifiLocationDecision(false, true, false), WifiLocationPolicy.Decide(access, asked));
    }

    [Fact]
    public void Decide_NotAskedYet_DoesNotCallUntilTheUserPressesTheButton()
    {
        Assert.Equal(new WifiLocationDecision(false, true, true), WifiLocationPolicy.Decide(WifiLocationAccess.UserPromptRequired, false));
    }

    [Fact]
    public void Decide_NotAskedYetButUserAsked_Calls()
    {
        Assert.Equal(new WifiLocationDecision(true, false, false), WifiLocationPolicy.Decide(WifiLocationAccess.UserPromptRequired, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecideFromBlock_None_CallsTheGatedApis(bool asked) =>
        Assert.Equal(new WifiLocationDecision(true, false, false), WifiLocationPolicy.Decide(WifiLocationBlock.None, asked));

    [Fact]
    public void DecideFromBlock_NotAsked_DoesNotCallUntilTheUserPressesTheButton() =>
        Assert.Equal(new WifiLocationDecision(false, true, true), WifiLocationPolicy.Decide(WifiLocationBlock.NotAsked, false));

    [Fact]
    public void DecideFromBlock_NotAskedButUserAsked_Calls() =>
        Assert.Equal(new WifiLocationDecision(true, false, false), WifiLocationPolicy.Decide(WifiLocationBlock.NotAsked, true));

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
    public void DecideFromBlock_KnownReason_NeverCallsAndHasNoShowNearbyButton(WifiLocationBlock block, bool asked) =>
        Assert.Equal(new WifiLocationDecision(false, true, false), WifiLocationPolicy.Decide(block, asked));
}
