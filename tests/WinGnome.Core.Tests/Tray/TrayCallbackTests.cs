using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class TrayCallbackTests
{
    private static TrayIconState Icon(uint version, bool showTip = false) =>
        new(new TrayIconId(1, 5, Guid.Empty), 0x8000, version, "tip", NotifyIconStates.None, showTip, true);

    [Fact]
    public void Legacy_IdInWParam_EventInLParam()
    {
        var (wParam, lParam) = TrayCallback.Encode(0, 5, TrayCallback.LeftButtonUp, 100, 20);

        Assert.Equal((nint)5, wParam);
        Assert.Equal((nint)TrayCallback.LeftButtonUp, lParam);
    }

    [Fact]
    public void Version4_AnchorInWParam_EventAndIdInLParam()
    {
        var (wParam, lParam) = TrayCallback.Encode(4, 5, TrayCallback.ContextMenu, 1900, 32);

        Assert.Equal(1900, (short)(wParam & 0xFFFF));
        Assert.Equal(32, (short)((wParam >> 16) & 0xFFFF));
        Assert.Equal(TrayCallback.ContextMenu, (int)(lParam & 0xFFFF));
        Assert.Equal(5, (int)((lParam >> 16) & 0xFFFF));
    }

    [Fact]
    public void Version4_NegativeCoordinatesRoundTrip()
    {
        var (wParam, _) = TrayCallback.Encode(4, 1, TrayCallback.Select, -1280, -10);

        Assert.Equal(-1280, (short)(wParam & 0xFFFF));
        Assert.Equal(-10, (short)((wParam >> 16) & 0xFFFF));
    }

    [Fact]
    public void LegacyClicks_AreMouseMessagesOnly()
    {
        Assert.Equal([TrayCallback.LeftButtonUp], TrayCallback.Notifications(TrayPointerAction.LeftUp, Icon(0)));
        Assert.Equal([TrayCallback.RightButtonUp], TrayCallback.Notifications(TrayPointerAction.RightUp, Icon(0)));
        Assert.Empty(TrayCallback.Notifications(TrayPointerAction.Hover, Icon(0)));
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(4u)]
    public void ModernClicks_AddSelectAndContextMenu(uint version)
    {
        Assert.Equal([TrayCallback.LeftButtonUp, TrayCallback.Select], TrayCallback.Notifications(TrayPointerAction.LeftUp, Icon(version)));
        Assert.Equal([TrayCallback.RightButtonUp, TrayCallback.ContextMenu], TrayCallback.Notifications(TrayPointerAction.RightUp, Icon(version)));
        Assert.Equal([TrayCallback.LeftButtonDoubleClick], TrayCallback.Notifications(TrayPointerAction.LeftDoubleClick, Icon(version)));
        Assert.Equal([TrayCallback.MiddleButtonUp], TrayCallback.Notifications(TrayPointerAction.MiddleUp, Icon(version)));
    }

    [Fact]
    public void RichPopupOnlyForVersion4WithoutShowTip()
    {
        Assert.Equal([TrayCallback.PopupOpen], TrayCallback.Notifications(TrayPointerAction.Hover, Icon(4)));
        Assert.Equal([TrayCallback.PopupClose], TrayCallback.Notifications(TrayPointerAction.Leave, Icon(4)));
        Assert.Empty(TrayCallback.Notifications(TrayPointerAction.Hover, Icon(4, showTip: true)));
        Assert.Empty(TrayCallback.Notifications(TrayPointerAction.Hover, Icon(3)));
    }

    [Fact]
    public void OnlyButtonsMayTakeForeground()
    {
        Assert.True(TrayCallback.MayTakeForeground(TrayPointerAction.RightUp));
        Assert.False(TrayCallback.MayTakeForeground(TrayPointerAction.Enter));
        Assert.False(TrayCallback.MayTakeForeground(TrayPointerAction.Hover));
    }

    [Fact]
    public void PackPoint_IsMakeLong()
    {
        Assert.Equal((nint)0x0020_0064, TrayCallback.PackPoint(100, 32));
    }
}
