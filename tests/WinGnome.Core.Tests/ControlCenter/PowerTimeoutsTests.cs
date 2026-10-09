using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class PowerTimeoutsTests
{
    [Theory]
    [InlineData(0, "Never")]
    [InlineData(30, "30 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(60, "1 minute")]
    [InlineData(300, "5 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(4800, "1 hour 20 minutes")]
    [InlineData(7200, "2 hours")]
    [InlineData(7260, "2 hours 1 minute")]
    [InlineData(90, "1 minute 30 seconds")]
    public void Label_ReadsLikeGnome(int seconds, string expected)
    {
        Assert.Equal(expected, PowerTimeouts.Label(seconds));
    }

    [Fact]
    public void ScreenBlankPresets_MatchGnome()
    {
        Assert.Equal([60, 120, 180, 240, 300, 480, 600, 720, 900, 0], PowerTimeouts.ScreenBlank);
    }

    [Fact]
    public void SuspendPresets_MatchGnome()
    {
        Assert.Equal([900, 1200, 1500, 1800, 2700, 3600, 4800, 5400, 6000, 7200, 0], PowerTimeouts.Suspend);
    }

    [Fact]
    public void Choices_CurrentIsAPreset_ReturnsThePresets()
    {
        var choices = PowerTimeouts.Choices([60, 300, 0], 300);

        Assert.Equal(
            [new TimeoutChoice(60, "1 minute"), new TimeoutChoice(300, "5 minutes"), new TimeoutChoice(0, "Never")],
            choices);
    }

    [Fact]
    public void Choices_CustomValue_IsInsertedInOrderBeforeNever()
    {
        var choices = PowerTimeouts.Choices([60, 300, 0], 420);

        Assert.Equal([60, 300, 420, 0], choices.Select(c => c.Seconds).ToList());
    }

    [Fact]
    public void Choices_CustomValueBelowEveryPreset_GoesFirst()
    {
        Assert.Equal([30, 60, 300, 0], PowerTimeouts.Choices([60, 300, 0], 30).Select(c => c.Seconds).ToList());
    }

    [Fact]
    public void Choices_CustomValueAboveEveryPreset_GoesLastBeforeNever()
    {
        Assert.Equal([60, 300, 18000, 0], PowerTimeouts.Choices([60, 300, 0], 18000).Select(c => c.Seconds).ToList());
    }

    [Fact]
    public void Choices_NeverIsCurrentButNotAPreset_IsAddedAtTheEnd()
    {
        Assert.Equal([60, 300, 0], PowerTimeouts.Choices([60, 300], 0).Select(c => c.Seconds).ToList());
    }

    [Fact]
    public void Choices_NegativeCurrent_IsIgnored()
    {
        Assert.Equal([60, 300, 0], PowerTimeouts.Choices([60, 300, 0], -5).Select(c => c.Seconds).ToList());
    }
}
