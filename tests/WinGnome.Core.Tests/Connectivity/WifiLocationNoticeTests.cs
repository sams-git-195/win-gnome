using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiLocationNoticeTests
{
    private const string Hedged = "Windows needs location access to show nearby networks";
    private const string HedgedSubtitle = "Turn on location access for WinGnome (and for desktop apps) in Windows Settings, then press Refresh. Turning Wi-Fi on or off and disconnecting still work.";

    [Fact]
    public void For_Policy_OffersOnlyTheWindowsList()
    {
        var notice = WifiLocationNotice.For(WifiLocationBlock.Policy, callRefused: false);

        Assert.Equal(
            new WifiLocationNoticeText(
                "Location is turned off by a policy on this PC",
                "Windows won't tell apps which networks are nearby. Your saved networks below still work. To list nearby networks here, an administrator must remove the policy, then turn on location for desktop apps.",
                OfferShowNearby: false,
                OfferLocationSettings: false,
                OfferWindowsList: true),
            notice);
    }

    [Fact]
    public void For_NotAsked_OffersOnlyShowNearby()
    {
        var notice = WifiLocationNotice.For(WifiLocationBlock.NotAsked, callRefused: false);

        Assert.Equal(
            new WifiLocationNoticeText(
                "Windows needs your permission to show nearby networks",
                "Windows will ask whether WinGnome may use your location. Turning Wi-Fi on or off and disconnecting work without it.",
                OfferShowNearby: true,
                OfferLocationSettings: false,
                OfferWindowsList: false),
            notice);
    }

    [Theory]
    [InlineData(WifiLocationBlock.DeviceOff, "Location services are off")]
    [InlineData(WifiLocationBlock.UserOff, "Location access is off for desktop apps")]
    [InlineData(WifiLocationBlock.DesktopAppsOff, "Location access is off for desktop apps")]
    [InlineData(WifiLocationBlock.AppDenied, "WinGnome isn't allowed to use your location")]
    public void For_SettingsReasons_NameTheReasonAndOfferSettingsAndTheWindowsList(WifiLocationBlock block, string title)
    {
        var notice = WifiLocationNotice.For(block, callRefused: false);

        Assert.Equal(title, notice.Title);
        Assert.NotEmpty(notice.Subtitle);
        Assert.False(notice.OfferShowNearby);
        Assert.True(notice.OfferLocationSettings);
        Assert.True(notice.OfferWindowsList);
    }

    [Fact]
    public void For_EveryReasonWithAnExplanation_HasADistinctSubtitle()
    {
        var subtitles = new[]
        {
            WifiLocationBlock.Policy, WifiLocationBlock.DeviceOff, WifiLocationBlock.UserOff, WifiLocationBlock.DesktopAppsOff,
            WifiLocationBlock.AppDenied, WifiLocationBlock.NotAsked,
        }.Select(b => WifiLocationNotice.For(b, false).Subtitle).ToList();

        Assert.Equal(6, subtitles.Distinct().Count());
    }

    [Theory]
    [InlineData(WifiLocationBlock.None)]
    [InlineData(WifiLocationBlock.NotAsked)]
    public void For_CallRefusedWithNoKnownReason_GivesTheHedgedText(WifiLocationBlock block)
    {
        var notice = WifiLocationNotice.For(block, callRefused: true);

        Assert.Equal(new WifiLocationNoticeText(Hedged, HedgedSubtitle, OfferShowNearby: false, OfferLocationSettings: true, OfferWindowsList: true), notice);
    }

    [Theory]
    [InlineData(WifiLocationBlock.Policy)]
    [InlineData(WifiLocationBlock.DeviceOff)]
    [InlineData(WifiLocationBlock.AppDenied)]
    public void For_CallRefusedWithAKnownReason_KeepsTheSpecificText(WifiLocationBlock block) =>
        Assert.Equal(WifiLocationNotice.For(block, callRefused: false), WifiLocationNotice.For(block, callRefused: true));

    [Fact]
    public void For_NoneAndNotRefused_HasNothingToShow() =>
        Assert.Equal(new WifiLocationNoticeText("", "", false, false, false), WifiLocationNotice.For(WifiLocationBlock.None, callRefused: false));
}
