using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class CaptionButtonHitTestTests
{
    // Circles of 14 DIPs, centres 22 DIPs apart (8 DIP gaps): targets are 22 DIPs wide.
    private static readonly CaptionOverlayLayout Layout = new(
        new PixelRect(0, 0, 100, 30),
        14,
        [
            new CaptionButtonSlot(CaptionButtonKind.Minimize, 30, 15),
            new CaptionButtonSlot(CaptionButtonKind.Maximize, 52, 15),
            new CaptionButtonSlot(CaptionButtonKind.Close, 74, 15),
        ]);

    [Theory]
    [InlineData(30, CaptionButtonKind.Minimize)]
    [InlineData(19, CaptionButtonKind.Minimize)]   // outer half-gap belongs to the first circle
    [InlineData(40.9, CaptionButtonKind.Minimize)]
    [InlineData(41, CaptionButtonKind.Maximize)]   // targets meet halfway between circles
    [InlineData(62.9, CaptionButtonKind.Maximize)]
    [InlineData(63, CaptionButtonKind.Close)]
    [InlineData(84.9, CaptionButtonKind.Close)]
    public void FindsTheButtonOwningThePoint(double x, CaptionButtonKind expected)
    {
        Assert.Equal(expected, CaptionButtonHitTest.Find(Layout, x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(18.9)]
    [InlineData(85)]
    [InlineData(99)]
    [InlineData(double.NaN)]
    public void PaddingOutsideTheGroupHitsNothing(double x)
    {
        Assert.Null(CaptionButtonHitTest.Find(Layout, x));
    }

    [Fact]
    public void TouchingCircles_UseTheDiameter()
    {
        var layout = Layout with
        {
            Buttons = [new CaptionButtonSlot(CaptionButtonKind.Close, 10, 5), new CaptionButtonSlot(CaptionButtonKind.Minimize, 24, 5)],
        };

        Assert.Equal(CaptionButtonKind.Close, CaptionButtonHitTest.Find(layout, 3));
        Assert.Equal(CaptionButtonKind.Minimize, CaptionButtonHitTest.Find(layout, 17));
        Assert.Null(CaptionButtonHitTest.Find(layout, 31));
    }

    [Fact]
    public void WorksWithRealLayouts()
    {
        var layout = CaptionButtonLayout.Compute(new PixelRect(1100, 50, 1238, 82), PixelRect.FromSize(100, 50, 1138, 600), 1, new());

        Assert.NotNull(layout);
        foreach (var slot in layout.Buttons)
        {
            Assert.Equal(slot.Kind, CaptionButtonHitTest.Find(layout, slot.CenterX));
        }
    }
}
