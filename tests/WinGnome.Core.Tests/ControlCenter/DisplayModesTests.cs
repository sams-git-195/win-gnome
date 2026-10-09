using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class DisplayModesTests
{
    private static readonly DisplayMode[] Modes =
    [
        new(1920, 1080, 60),
        new(1280, 720, 60),
        new(1920, 1080, 144),
        new(2560, 1440, 60),
        new(1920, 1080, 60),
        new(1920, 1200, 60),
        new(1920, 1080, 120),
        new(800, 600, 60),
    ];

    [Fact]
    public void Resolutions_AreDistinctAndLargestFirst()
    {
        Assert.Equal(
            [new Resolution(2560, 1440), new Resolution(1920, 1200), new Resolution(1920, 1080), new Resolution(1280, 720), new Resolution(800, 600)],
            DisplayModes.Resolutions(Modes));
    }

    [Fact]
    public void Resolutions_Empty_ReturnsEmpty()
    {
        Assert.Empty(DisplayModes.Resolutions([]));
    }

    [Fact]
    public void RefreshRates_ForOneResolution_AreDistinctAndFastestFirst()
    {
        Assert.Equal([144, 120, 60], DisplayModes.RefreshRates(Modes, new Resolution(1920, 1080)));
    }

    [Fact]
    public void RefreshRates_UnknownResolution_IsEmpty()
    {
        Assert.Empty(DisplayModes.RefreshRates(Modes, new Resolution(3840, 2160)));
    }

    public static TheoryData<int[], int, int> RefreshPicks { get; } = new()
    {
        { [144, 120, 60], 120, 120 },
        { [144, 120, 60], 75, 144 },
        { [], 60, 60 },
    };

    [Theory]
    [MemberData(nameof(RefreshPicks))]
    public void PickRefresh_KeepsTheCurrentRateWhenAvailable(int[] rates, int current, int expected)
    {
        Assert.Equal(expected, DisplayModes.PickRefresh(rates, current));
    }

    [Theory]
    [InlineData(1920, 1080, "16∶9")]
    [InlineData(1366, 768, "16∶9")]
    [InlineData(1360, 768, "16∶9")]
    [InlineData(1920, 1200, "16∶10")]
    [InlineData(1280, 800, "16∶10")]
    [InlineData(1024, 768, "4∶3")]
    [InlineData(1280, 1024, "5∶4")]
    [InlineData(2560, 1080, "21∶9")]
    [InlineData(3440, 1440, "21∶9")]
    [InlineData(3000, 2000, "3∶2")]
    [InlineData(5120, 1440, "32∶9")]
    [InlineData(1000, 700, "10∶7")]
    [InlineData(0, 700, "")]
    public void AspectRatio_UsesCommonNames(int width, int height, string expected)
    {
        Assert.Equal(expected, DisplayModes.AspectRatio(width, height));
    }

    [Fact]
    public void Label_CombinesSizeAndAspect()
    {
        Assert.Equal("1920 × 1080 (16∶9)", DisplayModes.Label(new Resolution(1920, 1080)));
    }

    [Theory]
    [InlineData(96, 100)]
    [InlineData(120, 125)]
    [InlineData(144, 150)]
    [InlineData(168, 175)]
    [InlineData(192, 200)]
    [InlineData(0, 100)]
    public void ScalePercent_FromDpi(int dpi, int expected)
    {
        Assert.Equal(expected, DisplayModes.ScalePercent(dpi));
    }
}
