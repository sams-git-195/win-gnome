using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class NotificationAppListTests
{
    private static NotificationKeySnapshot Key(string id, int? enabled = null, int? banner = null, int? centre = null, bool last = true) =>
        new(id, enabled, banner, centre, last);

    private static string? NoNames(string id) => null;

    [Fact]
    public void Build_NoKeys_IsEmpty()
    {
        Assert.Empty(NotificationAppList.Build([], NoNames));
    }

    [Fact]
    public void Build_AbsentValuesMeanOn_ZeroMeansOff()
    {
        var rows = NotificationAppList.Build([Key("CodexBar"), Key("Slack", enabled: 0, banner: 0, centre: 0)], NoNames);

        Assert.Equal(
            [new NotificationAppRow("CodexBar", "CodexBar", true, true, true), new NotificationAppRow("Slack", "Slack", false, false, false)],
            rows);
    }

    [Fact]
    public void Build_KeyWithNoValuesAndNoLastTime_IsSkipped()
    {
        var rows = NotificationAppList.Build([Key("Empty", last: false), Key("OnlyBanner", banner: 0, last: false)], NoNames);

        Assert.Equal(["OnlyBanner"], rows.Select(r => r.Id));
    }

    [Fact]
    public void Build_UsesTheLookedUpNameAndSortsIgnoringCase()
    {
        var names = new Dictionary<string, string> { ["Zeta_1!App"] = "alpha", ["Beta_1!App"] = "Bravo" };
        var rows = NotificationAppList.Build([Key("Zeta_1!App"), Key("Beta_1!App"), Key("charlie")], id => names.GetValueOrDefault(id));

        Assert.Equal(["alpha", "Bravo", "charlie"], rows.Select(r => r.Name));
    }

    [Fact]
    public void Build_SameNameFallsBackToIdOrder()
    {
        var rows = NotificationAppList.Build([Key("b.id"), Key("A.id")], _ => "Same");

        Assert.Equal(["A.id", "b.id"], rows.Select(r => r.Id));
    }

    [Fact]
    public void Build_KnownSystemSourceGetsItsBuiltInName()
    {
        var rows = NotificationAppList.Build([Key("Windows.SystemToast.Bthprops"), Key("windows.systemtoast.autoplay")], NoNames);

        Assert.Equal(["AutoPlay", "Bluetooth"], rows.Select(r => r.Name));
    }

    [Theory]
    [InlineData("Windows.SystemToast.PinConsent")]
    [InlineData("Windows.ActionCenter.SmartOptOut")]
    [InlineData("NotifyIconGeneratedAumid_13130853981540323094")]
    [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel")]
    [InlineData("Contoso.App_8wekyb3d8bbwe!App")]
    public void Build_UnknownSystemGeneratedAndUnnamedPackagedIdsAreHidden(string id)
    {
        Assert.Empty(NotificationAppList.Build([Key(id)], NoNames));
    }

    [Fact]
    public void Build_SystemIdIsHiddenEvenWhenTheLookupNamesIt()
    {
        Assert.Empty(NotificationAppList.Build([Key("Windows.SystemToast.PinConsent")], _ => "Something"));
    }

    [Theory]
    [InlineData("C:/ProgramData/ASUS/AsusSurvey/AsusSurvey.exe", "AsusSurvey")]
    [InlineData(@"C:\Tools\Tool.v2.exe", "Tool.v2")]
    [InlineData("com.squirrel.wmux.wmux", "com.squirrel.wmux.wmux")]
    public void Build_UnnamedDesktopIdShowsItsFileNameOrItself(string id, string expectedName)
    {
        var rows = NotificationAppList.Build([Key(id)], NoNames);

        Assert.Equal([expectedName], rows.Select(r => r.Name));
    }

    [Fact]
    public void Build_UnnamedPathEndingInSlash_IsHidden()
    {
        Assert.Empty(NotificationAppList.Build([Key("C:/Apps/")], NoNames));
    }
}
