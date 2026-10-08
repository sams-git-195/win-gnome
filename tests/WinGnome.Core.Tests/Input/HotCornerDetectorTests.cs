using WinGnome.Core.Geometry;
using WinGnome.Core.Input;

namespace WinGnome.Core.Tests.Input;

public class HotCornerDetectorTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void FiresOnceAfterTheDwellTime()
    {
        var d = new HotCornerDetector(150);

        Assert.False(d.Update(0, 0, Monitor, 1000));
        Assert.False(d.Update(0, 0, Monitor, 1100));
        Assert.False(d.Update(0, 0, Monitor, 1149));
        Assert.True(d.Update(0, 0, Monitor, 1150));
        Assert.False(d.Update(0, 0, Monitor, 1200));
        Assert.False(d.Update(0, 0, Monitor, 5000));
    }

    [Fact]
    public void ZeroDwell_FiresOnTheFirstSampleInside()
    {
        var d = new HotCornerDetector(0);

        Assert.False(d.Update(100, 100, Monitor, 0));
        Assert.True(d.Update(0, 0, Monitor, 1));
        Assert.False(d.Update(0, 0, Monitor, 2));
    }

    [Fact]
    public void NegativeDwell_BehavesLikeZero()
    {
        var d = new HotCornerDetector(-50);
        Assert.True(d.Update(0, 0, Monitor, 10));
    }

    [Fact]
    public void MustLeaveTheCornerBeforeFiringAgain()
    {
        var d = new HotCornerDetector(100);
        d.Update(0, 0, Monitor, 0);
        Assert.True(d.Update(0, 0, Monitor, 100));

        Assert.False(d.Update(0, 0, Monitor, 500));
        Assert.False(d.Update(5, 5, Monitor, 600));

        Assert.False(d.Update(0, 0, Monitor, 700));
        Assert.False(d.Update(0, 0, Monitor, 799));
        Assert.True(d.Update(0, 0, Monitor, 800));
    }

    [Fact]
    public void LeavingTheCorner_ResetsTheDwellTimer()
    {
        var d = new HotCornerDetector(150);

        d.Update(0, 0, Monitor, 1000);
        d.Update(10, 10, Monitor, 1100);
        Assert.False(d.Update(0, 0, Monitor, 1120));
        Assert.False(d.Update(0, 0, Monitor, 1200));
        Assert.True(d.Update(0, 0, Monitor, 1270));
    }

    [Fact]
    public void DoesNotFire_WhenNeverInsideTheCorner()
    {
        var d = new HotCornerDetector(0);

        Assert.False(d.Update(1, 0, Monitor, 0));
        Assert.False(d.Update(0, 1, Monitor, 10));
        Assert.False(d.Update(1919, 1079, Monitor, 20));
        Assert.False(d.Update(-1, -1, Monitor, 30));
    }

    [Fact]
    public void DefaultBoxIsOnePixel()
    {
        var d = new HotCornerDetector(0);
        Assert.False(d.Update(1, 1, Monitor, 0));
        Assert.True(d.Update(0, 0, Monitor, 1));
    }

    [Fact]
    public void LargerBox_CoversTheConfiguredPixels()
    {
        var d = new HotCornerDetector(0, sizePx: 3);

        Assert.False(d.Update(3, 0, Monitor, 0));
        Assert.False(d.Update(0, 3, Monitor, 1));
        Assert.True(d.Update(2, 2, Monitor, 2));
    }

    [Fact]
    public void SizeBelowOne_IsTreatedAsOne()
    {
        var d = new HotCornerDetector(0, sizePx: 0);
        Assert.False(d.Update(1, 0, Monitor, 0));
        Assert.True(d.Update(0, 0, Monitor, 1));
    }

    [Fact]
    public void UsesTheMonitorsTopLeft_NotTheScreenOrigin()
    {
        var second = new PixelRect(1920, 0, 3840, 1080);
        var d = new HotCornerDetector(0);

        Assert.False(d.Update(0, 0, second, 0));
        Assert.True(d.Update(1920, 0, second, 1));
    }

    [Fact]
    public void WorksOnMonitorsWithNegativeCoordinates()
    {
        var left = new PixelRect(-1920, -200, 0, 880);
        var d = new HotCornerDetector(0);

        Assert.False(d.Update(0, 0, left, 0));
        Assert.True(d.Update(-1920, -200, left, 1));
    }

    [Fact]
    public void ThreeVisitsFireThreeTimes()
    {
        var d = new HotCornerDetector(50);
        var fired = 0;
        long t = 0;

        for (var visit = 0; visit < 3; visit++)
        {
            for (var i = 0; i < 10; i++)
            {
                if (d.Update(0, 0, Monitor, t += 20))
                {
                    fired++;
                }
            }

            d.Update(500, 500, Monitor, t += 20);
        }

        Assert.Equal(3, fired);
    }
}
