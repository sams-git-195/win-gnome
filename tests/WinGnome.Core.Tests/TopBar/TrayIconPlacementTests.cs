using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class TrayIconPlacementTests
{
    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(32, 32)]
    public void Choose_SourceFillsTheSlot_DrawsItOneToOne(int sourcePx, int slotPx)
    {
        Assert.Equal(new TrayIconPlacement(sourcePx, 0), TrayIconPlacement.Choose(sourcePx, slotPx));
    }

    [Theory]
    [InlineData(16, 20, 2)]
    [InlineData(16, 24, 4)]
    [InlineData(16, 28, 6)]
    [InlineData(17, 20, 1)]
    [InlineData(24, 40, 8)]
    public void Choose_NonIntegerUpscale_DrawsTheSourceOneToOneCentred(int sourcePx, int slotPx, int offsetPx)
    {
        Assert.Equal(new TrayIconPlacement(sourcePx, offsetPx), TrayIconPlacement.Choose(sourcePx, slotPx));
    }

    [Theory]
    [InlineData(16, 32, 32, 0)]
    [InlineData(20, 40, 40, 0)]
    [InlineData(16, 40, 32, 4)]
    [InlineData(8, 20, 16, 2)]
    public void Choose_SlotHoldsAWholeMultiple_ScalesUpByAnInteger(int sourcePx, int slotPx, int sizePx, int offsetPx)
    {
        Assert.Equal(new TrayIconPlacement(sizePx, offsetPx), TrayIconPlacement.Choose(sourcePx, slotPx));
    }

    [Theory]
    [InlineData(32, 20)]
    [InlineData(24, 20)]
    [InlineData(21, 20)]
    public void Choose_LargerSource_ScalesDownToTheSlot(int sourcePx, int slotPx)
    {
        Assert.Equal(new TrayIconPlacement(slotPx, 0), TrayIconPlacement.Choose(sourcePx, slotPx));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-16)]
    public void Choose_UnknownSourceSize_FillsTheSlot(int sourcePx)
    {
        Assert.Equal(new TrayIconPlacement(20, 0), TrayIconPlacement.Choose(sourcePx, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-20)]
    public void Choose_EmptySlot_DrawsNothing(int slotPx)
    {
        Assert.Equal(new TrayIconPlacement(0, 0), TrayIconPlacement.Choose(16, slotPx));
    }
}
