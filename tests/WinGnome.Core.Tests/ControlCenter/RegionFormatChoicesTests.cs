using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class RegionFormatChoicesTests
{
    [Fact]
    public void Build_KeepsLocaleOrder()
    {
        Assert.Equal(["dd/MM/yyyy", "d/M/yy", "yyyy-MM-dd"], RegionFormatChoices.Build(["dd/MM/yyyy", "d/M/yy", "yyyy-MM-dd"], "d/M/yy"));
    }

    [Fact]
    public void Build_RemovesRepeatsAndBlanks()
    {
        Assert.Equal(["HH:mm", "H:mm"], RegionFormatChoices.Build(["HH:mm", "", "HH:mm", "H:mm", "H:mm"], "HH:mm"));
    }

    [Fact]
    public void Build_CurrentMissing_IsPlacedFirst()
    {
        Assert.Equal(["dd.MM.yy", "dd/MM/yyyy", "d/M/yy"], RegionFormatChoices.Build(["dd/MM/yyyy", "d/M/yy"], "dd.MM.yy"));
    }

    [Fact]
    public void Build_PatternsDifferingOnlyByCase_AreKeptApart()
    {
        Assert.Equal(["h:mm", "h:MM"], RegionFormatChoices.Build(["h:mm", "h:MM"], "h:mm"));
    }

    [Fact]
    public void Build_CurrentDiffersFromListedOnlyByCase_IsStillAddedFirst()
    {
        Assert.Equal(["H:MM", "H:mm"], RegionFormatChoices.Build(["H:mm"], "H:MM"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Build_NoCurrent_AddsNothing(string? current)
    {
        Assert.Equal(["H:mm"], RegionFormatChoices.Build(["H:mm"], current));
    }

    [Fact]
    public void Build_NoPatterns_OnlyCurrent()
    {
        Assert.Equal(["h:mm tt"], RegionFormatChoices.Build([], "h:mm tt"));
    }
}
