using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tests.Geometry;

public class BackdropPlacementTests
{
    private static readonly PixelRect Body = new(100, 200, 900, 264);

    [Fact]
    public void SquareBody_CoversTheBodyExactly()
    {
        Assert.Equal(new BackdropPlacement(Body, BackdropCorners.Square), BackdropPlacement.Compute(Body, 0, 1.5));
    }

    [Theory]
    [InlineData(1.9, BackdropCorners.Square)]
    [InlineData(2, BackdropCorners.Small)]
    [InlineData(5.9, BackdropCorners.Small)]
    [InlineData(6, BackdropCorners.Round)]
    [InlineData(24, BackdropCorners.Round)]
    public void PicksTheClosestDwmRounding_InDips(double radiusDip, BackdropCorners expected)
    {
        Assert.Equal(expected, BackdropPlacement.Compute(Body, radiusDip, 1).Corners);
        Assert.Equal(expected, BackdropPlacement.Compute(Body, radiusDip * 2, 2).Corners);
    }

    [Theory]
    [InlineData(4, 1.0, 0)] // Exactly DWM's small radius: no inset.
    [InlineData(8, 1.0, 0)] // Exactly DWM's radius: no inset.
    [InlineData(12, 1.0, 2)] // (12 - 8) × 0.293 = 1.17 → 2.
    [InlineData(18, 1.5, 2)] // 12 DIP at 150%: 1.17 × 1.5 = 1.76 → 2.
    [InlineData(24, 1.0, 5)] // (24 - 8) × 0.293 = 4.69 → 5.
    [InlineData(1, 1.0, 1)] // Square DWM corners under a 1 DIP radius: 0.29 → 1.
    public void InsetsJustEnough(double radiusPx, double scale, int inset)
    {
        Assert.Equal(Body.Inflate(-inset, -inset), BackdropPlacement.Compute(Body, radiusPx, scale).Bounds);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void BackdropCorners_StayInsideTheBodyOutline(double scale)
    {
        for (var radiusPx = 0.0; radiusPx <= 60; radiusPx += 0.5)
        {
            var placement = BackdropPlacement.Compute(Body, radiusPx, scale);
            var inset = placement.Bounds.Left - Body.Left;
            var r = placement.Corners switch
            {
                BackdropCorners.Round => BackdropPlacement.RoundRadiusDip * scale,
                BackdropCorners.Small => BackdropPlacement.SmallRadiusDip * scale,
                _ => 0,
            };

            // The backdrop's top-left arc, at 45°, measured from the centre of the body's top-left arc.
            var arc = inset + r - (r / Math.Sqrt(2));
            var distance = (radiusPx - arc) * Math.Sqrt(2);
            if (arc < radiusPx)
            {
                Assert.True(distance <= radiusPx + 1e-9, $"radius {radiusPx}px at {scale}: corner pokes out");
            }
        }
    }

    [Fact]
    public void InvalidInputs_AreSafe()
    {
        Assert.Equal(Body, BackdropPlacement.Compute(Body, double.NaN, 1).Bounds);
        Assert.Equal(Body, BackdropPlacement.Compute(Body, -5, 1).Bounds);
        Assert.Equal(BackdropPlacement.Compute(Body, 12, 1), BackdropPlacement.Compute(Body, 12, double.NaN));
    }

    [Fact]
    public void BodyTooSmallForTheInset_IsEmpty()
    {
        Assert.True(BackdropPlacement.Compute(new PixelRect(0, 0, 3, 3), 24, 1).Bounds.IsEmpty);
    }
}
