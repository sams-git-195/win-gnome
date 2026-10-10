using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class WorkAreaFightDetectorTests
{
    [Fact]
    public void Fight_TwoApplicationsWithinTheWindow_DoNotFight()
    {
        var detector = new WorkAreaFightDetector();

        Assert.False(detector.Record(0));
        Assert.False(detector.Record(1_000));
        Assert.False(detector.IsFighting(2_000));
    }

    [Fact]
    public void Fight_ThirdApplicationWithinTheWindow_Fights()
    {
        var detector = new WorkAreaFightDetector();
        detector.Record(0);
        detector.Record(1_000);

        Assert.True(detector.Record(2_000));
        Assert.True(detector.IsFighting(2_000));
    }

    [Theory]
    [InlineData(599_999, true)]  // One millisecond before the first application leaves the window.
    [InlineData(600_000, false)] // Exactly WindowMs after it.
    public void Fight_ApplicationExactlyAtTheWindowEdge_LeavesTheWindow(long nowMs, bool expected)
    {
        var detector = new WorkAreaFightDetector();
        detector.Record(0);
        detector.Record(1_000);
        detector.Record(2_000);

        Assert.Equal(expected, detector.IsFighting(nowMs));
    }

    [Fact]
    public void Fight_ApplicationAfterTheWindowSlides_DropsTheOldest()
    {
        var detector = new WorkAreaFightDetector();
        detector.Record(0);
        detector.Record(60_000);

        // The application at 0 left the window 1 ms ago, so this one only faces two inside it.
        Assert.False(detector.Record(600_001));
        Assert.False(detector.IsFighting(600_001));
    }

    [Fact]
    public void Fight_IsFightingAfterTheWindow_IsFalseWithoutRecording()
    {
        var detector = new WorkAreaFightDetector();
        detector.Record(0);
        detector.Record(1_000);
        detector.Record(2_000);
        Assert.True(detector.IsFighting(3_000));

        // The window has slid clear, and asking did not itself count as an application: the next real one is the
        // first inside it again.
        Assert.False(detector.IsFighting(602_000));
        Assert.False(detector.IsFighting(602_000));
        Assert.False(detector.Record(602_000));
    }
}
