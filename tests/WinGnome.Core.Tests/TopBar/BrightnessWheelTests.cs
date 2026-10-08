using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class BrightnessWheelTests
{
    private static readonly int[] Full = Enumerable.Range(0, 101).ToArray();
    private static readonly int[] Coarse = [0, 25, 50, 75, 100];

    /// <summary>Feeds the deltas in order, as the controller does (each result becomes the next current level).</summary>
    private static int[] Run(int start, IReadOnlyList<int> levels, params int[] deltas)
    {
        var wheel = new BrightnessWheel();
        var current = start;
        var results = new List<int>();
        foreach (var delta in deltas)
        {
            current = wheel.Apply(current, delta, levels);
            results.Add(current);
        }

        return [.. results];
    }

    [Fact]
    public void Apply_OneNotchIsFivePercent()
    {
        Assert.Equal([55], Run(50, Full, 120));
        Assert.Equal([45], Run(50, Full, -120));
    }

    [Fact]
    public void Apply_LargeDeltaIsProportional()
    {
        Assert.Equal([65], Run(50, Full, 360));
    }

    [Fact]
    public void Apply_SubNotchDeltasAddUpToExactlyOneNotch()
    {
        Assert.Equal([51, 52, 53, 55], Run(50, Full, 30, 30, 30, 30));
        Assert.Equal([50, 51], Run(50, Full, 12, 12));
        Assert.Equal([50, 50, 51, 51, 52, 52, 52, 53, 53, 54, 54, 55], Run(50, Full, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10));
    }

    [Fact]
    public void Apply_TinyDeltaAloneDoesNothing()
    {
        Assert.Equal([50], Run(50, Full, 10));
    }

    [Fact]
    public void Apply_OppositeDeltasCancelOut()
    {
        Assert.Equal([52, 50], Run(50, Full, 60, -60));
    }

    [Fact]
    public void Apply_ZeroDelta_KeepsTheLevel()
    {
        Assert.Equal([50, 50], Run(50, Coarse, 0, 0));
    }

    [Theory]
    [InlineData(98, 240, 100)]
    [InlineData(2, -240, 0)]
    public void Apply_ClampsAtTheEnds(int start, int delta, int expected)
    {
        Assert.Equal([expected], Run(start, Full, delta));
    }

    [Fact]
    public void Apply_AtAnEnd_DoesNotBankDeltasThatCouldNotBeUsed()
    {
        // Scrolling up at 100 must not leave a debt that delays the first notch back down.
        Assert.Equal([100, 100, 95], Run(100, Full, 120, 120, -120));
    }

    [Fact]
    public void Apply_CoarseLevels_AWholeNotchReachesTheNeighbour()
    {
        Assert.Equal([75], Run(50, Coarse, 120));
        Assert.Equal([25], Run(50, Coarse, -120));
        Assert.Equal([0], Run(0, Coarse, -120));
        Assert.Equal([25], Run(0, Coarse, 120));
    }

    [Fact]
    public void Apply_CoarseLevels_SubNotchDeltasMoveOneLevelOnceTheyAddUpToANotch()
    {
        Assert.Equal([50, 50, 50, 75], Run(50, Coarse, 30, 30, 30, 30));
        Assert.Equal([50, 50, 50, 25], Run(50, Coarse, -30, -30, -30, -30));
    }

    [Fact]
    public void Apply_CoarseLevels_AfterAStepTheRemainderStartsFromZero()
    {
        Assert.Equal([75, 75, 75, 75, 100], Run(50, Coarse, 120, 30, 30, 30, 30));
    }

    [Fact]
    public void Apply_CoarseLevels_AJumpPastTheTravelDoesNotLeaveADebt()
    {
        // 312 units only pay for 13 %, but the snapped move is 25 %; the next small delta must start from zero.
        Assert.Equal([75, 75], Run(50, Coarse, 312, 24));
    }

    [Fact]
    public void Apply_NoLevels_KeepsTheLevel()
    {
        Assert.Equal([40], Run(40, [], 120));
    }

    [Fact]
    public void Reset_DropsTheBankedRemainder()
    {
        var wheel = new BrightnessWheel();
        Assert.Equal(50, wheel.Apply(50, 20, Full));

        wheel.Reset();

        // Without the reset the two 20s would add up to 40 and move one step.
        Assert.Equal(50, wheel.Apply(50, 20, Full));
    }
}
