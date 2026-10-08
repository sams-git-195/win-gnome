using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class VolumeLevelTests
{
    [Theory]
    [InlineData(0.0, VolumeIcon.Silent)]
    [InlineData(0.004, VolumeIcon.Silent)]
    [InlineData(0.01, VolumeIcon.Low)]
    [InlineData(0.33, VolumeIcon.Low)]
    [InlineData(0.34, VolumeIcon.Medium)]
    [InlineData(0.66, VolumeIcon.Medium)]
    [InlineData(0.67, VolumeIcon.High)]
    [InlineData(1.0, VolumeIcon.High)]
    public void IconFor_FollowsLevel(double level, VolumeIcon expected)
    {
        Assert.Equal(expected, VolumeLevel.IconFor(level, muted: false));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void IconFor_MutedWins(double level)
    {
        Assert.Equal(VolumeIcon.Muted, VolumeLevel.IconFor(level, muted: true));
    }

    [Fact]
    public void Nudge_OneNotchIsTwoPercent()
    {
        Assert.Equal(0.52, VolumeLevel.Nudge(0.5, 120), 6);
        Assert.Equal(0.48, VolumeLevel.Nudge(0.5, -120), 6);
    }

    [Fact]
    public void Nudge_IsProportionalForHighResolutionWheels()
    {
        Assert.Equal(0.51, VolumeLevel.Nudge(0.5, 60), 6);
        Assert.Equal(0.56, VolumeLevel.Nudge(0.5, 360), 6);
    }

    [Theory]
    [InlineData(0.99, 240, 1.0)]
    [InlineData(0.01, -240, 0.0)]
    [InlineData(double.NaN, 120, 0.02)]
    [InlineData(1.5, 0, 1.0)]
    public void Nudge_Clamps(double level, int delta, double expected)
    {
        Assert.Equal(expected, VolumeLevel.Nudge(level, delta), 6);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.455, 46)]
    [InlineData(0.454, 45)]
    [InlineData(1.0, 100)]
    [InlineData(-1.0, 0)]
    [InlineData(double.NaN, 0)]
    public void ToPercent_RoundsAndClamps(double level, int expected)
    {
        Assert.Equal(expected, VolumeLevel.ToPercent(level));
    }
}
