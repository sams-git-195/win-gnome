using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class PrinterStatusTextTests
{
    [Theory]
    [InlineData(0x0u, "Ready")]
    [InlineData(0x1u, "Paused")]
    [InlineData(0x2u, "Error")]
    [InlineData(0x4u, "Being deleted")]
    [InlineData(0x8u, "Paper jam")]
    [InlineData(0x10u, "Out of paper")]
    [InlineData(0x40u, "Paper problem")]
    [InlineData(0x80u, "Offline")]
    [InlineData(0x200u, "Busy")]
    [InlineData(0x400u, "Printing")]
    [InlineData(0x800u, "Output tray full")]
    [InlineData(0x1000u, "Not available")]
    [InlineData(0x4000u, "Processing")]
    [InlineData(0x8000u, "Starting up")]
    [InlineData(0x10000u, "Warming up")]
    [InlineData(0x20000u, "Low on toner")]
    [InlineData(0x40000u, "Out of toner")]
    [InlineData(0x100000u, "Needs attention")]
    [InlineData(0x200000u, "Out of memory")]
    [InlineData(0x400000u, "Door open")]
    [InlineData(0x800000u, "Offline")]
    [InlineData(0x2000000u, "Offline")]
    public void Describe_SingleStatusBit_NamesIt(uint status, string expected)
    {
        Assert.Equal(expected, PrinterStatusText.Describe(status, 0, 0));
    }

    [Theory]
    [InlineData(0x8u | 0x80u | 0x400u, "Paper jam")]
    [InlineData(0x80u | 0x2u, "Offline")]
    [InlineData(0x2u | 0x1u, "Error")]
    [InlineData(0x1u | 0x400u, "Paused")]
    [InlineData(0x40000u | 0x10u, "Out of toner")]
    public void Describe_CombinedBits_PicksTheMostSerious(uint status, string expected)
    {
        Assert.Equal(expected, PrinterStatusText.Describe(status, 0, 0));
    }

    [Fact]
    public void Describe_WorkOfflineAttribute_IsOffline()
    {
        Assert.Equal("Offline", PrinterStatusText.Describe(0, PrinterStatusText.AttributeWorkOffline, 0));
    }

    [Fact]
    public void Describe_WorkOfflineWithPaperJam_StillNamesTheJam()
    {
        Assert.Equal("Paper jam", PrinterStatusText.Describe(0x8, PrinterStatusText.AttributeWorkOffline, 0));
    }

    [Fact]
    public void Describe_OtherAttributeBits_AreIgnored()
    {
        Assert.Equal("Ready", PrinterStatusText.Describe(0, 0x1000u | 0x40u, 0));
    }

    [Theory]
    [InlineData(0, "Ready")]
    [InlineData(1, "1 job")]
    [InlineData(2, "2 jobs")]
    [InlineData(25, "25 jobs")]
    [InlineData(-3, "Ready")]
    public void Describe_JobsOnAReadyPrinter_AreNamedOnTheirOwn(int jobs, string expected)
    {
        Assert.Equal(expected, PrinterStatusText.Describe(0, 0, jobs));
    }

    [Theory]
    [InlineData(0x400u, 1, "Printing, 1 job")]
    [InlineData(0x80u, 3, "Offline, 3 jobs")]
    public void Describe_JobsOnAPrinterWithAState_AreAppended(uint status, int jobs, string expected)
    {
        Assert.Equal(expected, PrinterStatusText.Describe(status, 0, jobs));
    }
}
