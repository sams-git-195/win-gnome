using WinGnome.Core.Connectivity;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiLocationGateTests
{
    private static WifiLocationInputs Open() => new(
        PolicyOff: false,
        ConsentState.Allow,
        ConsentState.Allow,
        ConsentState.Allow,
        ThisApp: ConsentState.Allow,
        WifiLocationAccess.Allowed);

    [Fact]
    public void Evaluate_PolicyOnAndCapabilityPromptRequired_IsPolicy()
    {
        // The probed machine: the policy is set, consent says Deny/Deny/Allow and the capability misreports "ask".
        var inputs = new WifiLocationInputs(true, ConsentState.Deny, ConsentState.Deny, ConsentState.Allow, ConsentState.Allow, WifiLocationAccess.UserPromptRequired);

        Assert.Equal(WifiLocationBlock.Policy, WifiLocationGate.Evaluate(inputs));
    }

    [Fact]
    public void Evaluate_PolicyOnButEverythingElseAllowed_IsStillPolicy() =>
        Assert.Equal(WifiLocationBlock.Policy, WifiLocationGate.Evaluate(Open() with { PolicyOff = true }));

    [Fact]
    public void Evaluate_DeviceDenyAndUserAllow_IsDeviceOff() =>
        Assert.Equal(WifiLocationBlock.DeviceOff, WifiLocationGate.Evaluate(Open() with { Device = ConsentState.Deny }));

    [Fact]
    public void Evaluate_DeviceDenyAndUserDeny_ReportsTheDeviceFirst() =>
        Assert.Equal(WifiLocationBlock.DeviceOff, WifiLocationGate.Evaluate(Open() with { Device = ConsentState.Deny, User = ConsentState.Deny }));

    [Fact]
    public void Evaluate_UserDeny_IsUserOff() =>
        Assert.Equal(WifiLocationBlock.UserOff, WifiLocationGate.Evaluate(Open() with { User = ConsentState.Deny }));

    [Fact]
    public void Evaluate_UserDenyAndDesktopDeny_ReportsTheUserFirst() =>
        Assert.Equal(WifiLocationBlock.UserOff, WifiLocationGate.Evaluate(Open() with { User = ConsentState.Deny, DesktopApps = ConsentState.Deny }));

    [Fact]
    public void Evaluate_DesktopAppsDeny_IsDesktopAppsOff() =>
        Assert.Equal(WifiLocationBlock.DesktopAppsOff, WifiLocationGate.Evaluate(Open() with { DesktopApps = ConsentState.Deny }));

    [Fact]
    public void Evaluate_DesktopAppsDenyAndAppDeny_ReportsDesktopAppsFirst() =>
        Assert.Equal(WifiLocationBlock.DesktopAppsOff, WifiLocationGate.Evaluate(Open() with { DesktopApps = ConsentState.Deny, ThisApp = ConsentState.Deny }));

    [Fact]
    public void Evaluate_ThisAppDeny_IsAppDenied() =>
        Assert.Equal(WifiLocationBlock.AppDenied, WifiLocationGate.Evaluate(Open() with { ThisApp = ConsentState.Deny }));

    [Fact]
    public void Evaluate_AppDeniedAndCapabilityPromptRequired_ReportsTheAppFirst() =>
        Assert.Equal(WifiLocationBlock.AppDenied, WifiLocationGate.Evaluate(Open() with { ThisApp = ConsentState.Deny, Capability = WifiLocationAccess.UserPromptRequired }));

    [Fact]
    public void Evaluate_NothingOffAndCapabilityPromptRequired_IsNotAsked() =>
        Assert.Equal(WifiLocationBlock.NotAsked, WifiLocationGate.Evaluate(Open() with { Capability = WifiLocationAccess.UserPromptRequired }));

    [Fact]
    public void Evaluate_EverythingAbsentAndAllowed_IsNone()
    {
        var inputs = new WifiLocationInputs(false, ConsentState.Absent, ConsentState.Absent, ConsentState.Absent, null, WifiLocationAccess.Allowed);

        Assert.Equal(WifiLocationBlock.None, WifiLocationGate.Evaluate(inputs));
    }

    [Fact]
    public void Evaluate_EverythingAbsentAndCapabilityUnknown_IsNone()
    {
        var inputs = new WifiLocationInputs(false, ConsentState.Absent, ConsentState.Absent, ConsentState.Absent, null, WifiLocationAccess.Unknown);

        Assert.Equal(WifiLocationBlock.None, WifiLocationGate.Evaluate(inputs));
    }

    [Theory]
    [InlineData(WifiLocationAccess.DeniedByUser, WifiLocationBlock.AppDenied)]
    [InlineData(WifiLocationAccess.DeniedBySystem, WifiLocationBlock.DeviceOff)]
    public void Evaluate_NothingOffInTheRegistryButCapabilityDenied_UsesTheCapability(WifiLocationAccess capability, WifiLocationBlock expected) =>
        Assert.Equal(expected, WifiLocationGate.Evaluate(Open() with { Capability = capability }));

    [Theory]
    [InlineData(ConsentState.Prompt)]
    [InlineData(ConsentState.Unknown)]
    public void Evaluate_DeviceValueThatIsNotAllow_CountsAsOff(ConsentState device) =>
        Assert.Equal(WifiLocationBlock.DeviceOff, WifiLocationGate.Evaluate(Open() with { Device = device }));

    [Theory]
    [InlineData(ConsentState.Prompt)]
    [InlineData(ConsentState.Unknown)]
    public void Evaluate_ThisAppValueThatIsNotAllow_CountsAsDenied(ConsentState app) =>
        Assert.Equal(WifiLocationBlock.AppDenied, WifiLocationGate.Evaluate(Open() with { ThisApp = app }));

    [Fact]
    public void Evaluate_DefaultInputs_IsNone() =>
        Assert.Equal(WifiLocationBlock.None, WifiLocationGate.Evaluate(default));

    [Theory]
    [InlineData(@"C:\Program Files\WinGnome\WinGnome.exe", "C:#Program Files#WinGnome#WinGnome.exe")]
    [InlineData(@"C:\Users\Sam Héard\Downloads\WinGnome.exe", "C:#Users#Sam Héard#Downloads#WinGnome.exe")]
    [InlineData(@"\\server\share\WinGnome.exe", "##server#share#WinGnome.exe")]
    [InlineData("WinGnome.exe", "WinGnome.exe")]
    [InlineData("", "")]
    public void AppKeyFor_ReplacesEveryBackslashWithAHash(string path, string expected) =>
        Assert.Equal(expected, WifiLocationGate.AppKeyFor(path));

    [Fact]
    public void AppKeyFor_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WifiLocationGate.AppKeyFor(null!));
}
