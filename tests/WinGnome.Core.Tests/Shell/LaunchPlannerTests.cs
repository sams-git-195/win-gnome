using WinGnome.Core.Settings;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class LaunchPlannerTests
{
    // A desktop app's explicit AUMID (Git Bash).
    private const string Desktop = "GitForWindows.Bash";

    // A UWP app (HostEnvironment 1) and a full-trust packaged app (HostEnvironment 2), as read on Windows 11.
    private const string Packaged = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";

    public enum PinState { None, Normal, AlwaysElevated }

    private static PinnedApp? Pin(string launchId, PinState state) => state switch
    {
        PinState.None => null,
        _ => new PinnedApp { Name = "App", LaunchId = launchId, RunAsAdministrator = state == PinState.AlwaysElevated },
    };

    [Theory]
    [InlineData(LaunchModifiers.None, PinState.None, false)]
    [InlineData(LaunchModifiers.None, PinState.Normal, false)]
    [InlineData(LaunchModifiers.None, PinState.AlwaysElevated, true)]
    [InlineData(LaunchModifiers.Control, PinState.None, false)]
    [InlineData(LaunchModifiers.Control, PinState.Normal, false)]
    [InlineData(LaunchModifiers.Control, PinState.AlwaysElevated, true)]
    [InlineData(LaunchModifiers.Shift, PinState.None, false)]
    [InlineData(LaunchModifiers.Shift, PinState.Normal, false)]
    [InlineData(LaunchModifiers.Shift, PinState.AlwaysElevated, true)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.None, true)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.Normal, true)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.AlwaysElevated, true)]
    public void Plan_DesktopApp_ElevatesOnCtrlShiftOrAlwaysPin(LaunchModifiers modifiers, PinState pin, bool elevate)
    {
        var request = LaunchPlanner.Plan(Desktop, modifiers, Pin(Desktop, pin), AppHost.Desktop);

        Assert.Equal(new LaunchRequest(Desktop, null, elevate), request);
    }

    [Theory]
    [InlineData(LaunchModifiers.None, PinState.None)]
    [InlineData(LaunchModifiers.None, PinState.Normal)]
    [InlineData(LaunchModifiers.None, PinState.AlwaysElevated)]
    [InlineData(LaunchModifiers.Control, PinState.AlwaysElevated)]
    [InlineData(LaunchModifiers.Shift, PinState.AlwaysElevated)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.None)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.Normal)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.AlwaysElevated)]
    public void Plan_UwpApp_NeverElevates(LaunchModifiers modifiers, PinState pin)
    {
        var request = LaunchPlanner.Plan(Packaged, modifiers, Pin(Packaged, pin), AppHost.Immersive);

        Assert.Equal(new LaunchRequest(Packaged, null, false), request);
    }

    [Theory]
    [InlineData(LaunchModifiers.None, PinState.None, false)]
    [InlineData(LaunchModifiers.None, PinState.Normal, false)]
    [InlineData(LaunchModifiers.None, PinState.AlwaysElevated, true)]
    [InlineData(LaunchModifiers.Control, PinState.None, false)]
    [InlineData(LaunchModifiers.Shift, PinState.None, false)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.None, true)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.Normal, true)]
    [InlineData(LaunchModifiers.Control | LaunchModifiers.Shift, PinState.AlwaysElevated, true)]
    public void Plan_FullTrustPackagedApp_ElevatesOnCtrlShiftOrAlwaysPin(LaunchModifiers modifiers, PinState pin, bool elevate)
    {
        var request = LaunchPlanner.Plan(Terminal, modifiers, Pin(Terminal, pin), AppHost.PackagedDesktop);

        Assert.Equal(new LaunchRequest(Terminal, null, elevate), request);
    }

    [Fact]
    public void Plan_PackagedAppWithUnknownHost_DoesNotElevate()
    {
        // Without the catalogue's HostEnvironment, a packaged app is assumed to be UWP.
        var request = LaunchPlanner.Plan(Terminal, LaunchPlanner.ElevateModifiers, null);

        Assert.Equal(new LaunchRequest(Terminal, null, false), request);
    }

    [Fact]
    public void PlanElevated_FullTrustPackagedApp_Elevates()
    {
        Assert.Equal(new LaunchRequest(Terminal, null, true), LaunchPlanner.PlanElevated(Terminal, null, AppHost.PackagedDesktop));
    }

    [Theory]
    [InlineData(null, AppHost.Unknown)]
    [InlineData(0u, AppHost.Desktop)]
    [InlineData(1u, AppHost.Immersive)]
    [InlineData(2u, AppHost.PackagedDesktop)]
    [InlineData(3u, AppHost.Unknown)]
    public void HostFromProperty_MapsHostEnvironmentValues(uint? value, AppHost expected)
    {
        Assert.Equal(expected, LaunchPlanner.HostFromProperty(value));
    }

    [Theory]
    [InlineData(AppHost.Unknown, LaunchTargetKind.PackagedApp)]
    [InlineData(AppHost.Immersive, LaunchTargetKind.PackagedApp)]
    [InlineData(AppHost.PackagedDesktop, LaunchTargetKind.FullTrustPackagedApp)]
    [InlineData(AppHost.Desktop, LaunchTargetKind.FullTrustPackagedApp)]
    public void Classify_PackagedApp_DependsOnItsHost(AppHost host, LaunchTargetKind expected)
    {
        Assert.Equal(expected, LaunchPlanner.Classify(Terminal, host));
    }

    [Theory]
    [InlineData("ms-settings:", AppHost.PackagedDesktop, LaunchTargetKind.Uri)]
    [InlineData(Desktop, AppHost.Immersive, LaunchTargetKind.DesktopApp)]
    public void Classify_NonPackagedIds_IgnoreTheHost(string launchId, AppHost host, LaunchTargetKind expected)
    {
        Assert.Equal(expected, LaunchPlanner.Classify(launchId, host));
    }

    [Theory]
    [InlineData(AppHost.Unknown, false)]
    [InlineData(AppHost.Immersive, false)]
    [InlineData(AppHost.PackagedDesktop, true)]
    public void CanElevate_PackagedApp_OnlyWhenFullTrust(AppHost host, bool expected)
    {
        Assert.Equal(expected, LaunchPlanner.CanElevate(Terminal, host));
    }

    [Theory]
    [InlineData("ms-settings:")]
    [InlineData(@"C:\Users\me\notes.txt")]
    public void Plan_CtrlShift_OnTargetsThatCannotElevate_LaunchesNormally(string launchId)
    {
        Assert.Equal(new LaunchRequest(launchId, null, false), LaunchPlanner.Plan(launchId, LaunchPlanner.ElevateModifiers, null));
    }

    [Fact]
    public void Plan_CtrlShiftWithAlt_StillElevates()
    {
        var request = LaunchPlanner.Plan(Desktop, LaunchModifiers.Control | LaunchModifiers.Shift | LaunchModifiers.Alt, null, AppHost.Desktop);

        Assert.True(request.Elevate);
    }

    [Fact]
    public void Plan_Pin_SuppliesItsArguments()
    {
        var pin = new PinnedApp { Name = "Calc", LaunchId = @"C:\Tools\calc.exe", Arguments = "/x" };

        var request = LaunchPlanner.Plan(@"C:\Tools\calc.exe", LaunchModifiers.None, pin);

        Assert.Equal(new LaunchRequest(@"C:\Tools\calc.exe", "/x", false), request);
    }

    [Fact]
    public void Plan_TrimsTheLaunchId()
    {
        var request = LaunchPlanner.Plan("  " + Desktop + " ", LaunchModifiers.None, null);

        Assert.Equal(Desktop, request.LaunchId);
    }

    [Fact]
    public void PlanElevated_DesktopApp_Elevates()
    {
        var pin = new PinnedApp { Name = "Tool", LaunchId = @"C:\Tools\tool.exe", Arguments = "--dev" };

        Assert.Equal(new LaunchRequest(@"C:\Tools\tool.exe", "--dev", true), LaunchPlanner.PlanElevated(@"C:\Tools\tool.exe", pin));
    }

    [Fact]
    public void PlanElevated_PackagedApp_DoesNotElevate()
    {
        Assert.Equal(new LaunchRequest(Packaged, null, false), LaunchPlanner.PlanElevated(Packaged, null));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", LaunchTargetKind.File)]
    [InlineData(@"C:\Users\me\Desktop\Tool.lnk", LaunchTargetKind.File)]
    [InlineData(@"\\server\share\tool.exe", LaunchTargetKind.File)]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Mozilla Firefox\firefox.exe", LaunchTargetKind.File)]
    [InlineData("ms-settings:", LaunchTargetKind.Uri)]
    [InlineData("shell:RecycleBinFolder", LaunchTargetKind.Uri)]
    [InlineData("https://example.com/", LaunchTargetKind.Uri)]
    [InlineData(Packaged, LaunchTargetKind.PackagedApp)]
    [InlineData(Desktop, LaunchTargetKind.DesktopApp)]
    [InlineData("Microsoft.AutoGenerated.{923DD477-5846-686B-A659-0FCCD73851A8}", LaunchTargetKind.DesktopApp)]
    public void Classify_RecognisesEachKindOfLaunchId(string launchId, LaunchTargetKind expected)
    {
        Assert.Equal(expected, LaunchPlanner.Classify(launchId));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", true)]
    [InlineData(@"C:\Users\me\Desktop\Tool.LNK", true)]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Mozilla Firefox\firefox.exe", true)]
    [InlineData(@"C:\Tools\build.cmd", true)]
    [InlineData(@"C:\Tools\setup.bat", true)]
    [InlineData(@"C:\Windows\System32\services.msc", true)]
    [InlineData(@"C:\Users\me\notes.txt", false)]
    [InlineData(@"C:\Tools", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("shell:RecycleBinFolder", false)]
    [InlineData(Packaged, false)]
    [InlineData(Desktop, false)] // a desktop AUMID needs the catalogue's host, see below
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void CanElevate_OnlyDesktopAppsAndExecutables(string launchId, bool expected)
    {
        Assert.Equal(expected, LaunchPlanner.CanElevate(launchId));
    }

    [Theory]
    [InlineData(Desktop, AppHost.Desktop, true)]
    [InlineData(Desktop, AppHost.Unknown, false)]
    [InlineData(Desktop, AppHost.Immersive, false)]
    [InlineData("Microsoft.Windows.Explorer", AppHost.Desktop, false)]
    [InlineData("microsoft.windows.explorer", AppHost.Desktop, false)]
    public void CanElevate_DesktopAumid_NeedsAWin32Host_AndNeverFileExplorer(string launchId, AppHost host, bool expected)
    {
        Assert.Equal(expected, LaunchPlanner.CanElevate(launchId, host));
    }

    [Fact]
    public void Plan_CtrlShift_OnFileExplorer_LaunchesNormally()
    {
        var request = LaunchPlanner.Plan("Microsoft.Windows.Explorer", LaunchPlanner.ElevateModifiers, null, AppHost.Desktop);

        Assert.False(request.Elevate);
    }
}
