using System.Text.Json;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class TaskbarMarkerTests
{
    [Theory]
    [InlineData("""{"WasAutoHide":true}""", true)]
    [InlineData("""{"WasAutoHide":false}""", false)]
    public void Parse_MarkerFromBeforeRegions_LoadsWithNoRecordedRegions(string json, bool wasAutoHide)
    {
        var marker = TaskbarMarker.Parse(json);

        Assert.NotNull(marker);
        Assert.Equal(wasAutoHide, marker.WasAutoHide);
        Assert.Empty(marker.EmptiedRegions);
    }

    [Fact]
    public void Parse_NullRegionList_IsTreatedAsEmpty()
    {
        var marker = TaskbarMarker.Parse("""{"WasAutoHide":true,"EmptiedRegions":null}""");

        Assert.NotNull(marker);
        Assert.Empty(marker.EmptiedRegions);
    }

    [Fact]
    public void Parse_RecordedRegions_AreRead()
    {
        var marker = TaskbarMarker.Parse("""{"WasAutoHide":false,"EmptiedRegions":[65816,131072]}""");

        Assert.NotNull(marker);
        Assert.Equal([65816L, 131072L], marker.EmptiedRegions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("""{"WasAutoHide":"yes"}""")]
    public void Parse_UnusableText_ReturnsNull(string json)
    {
        Assert.Null(TaskbarMarker.Parse(json));
    }

    [Fact]
    public void Serialize_ThenParse_KeepsEverything()
    {
        var marker = new TaskbarMarker { WasAutoHide = true }.WithEmptiedRegion(7).WithEmptiedRegion(9);

        var parsed = TaskbarMarker.Parse(marker.Serialize());

        Assert.NotNull(parsed);
        Assert.True(parsed.WasAutoHide);
        Assert.Equal([7L, 9L], parsed.EmptiedRegions);
    }

    [Fact]
    public void Serialize_KeepsTheFieldAnOlderBuildReads()
    {
        var json = new TaskbarMarker { WasAutoHide = true }.Serialize();

        // An older build reads only WasAutoHide and ignores unknown members; the field must keep its name and type.
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("WasAutoHide").GetBoolean());
    }

    [Fact]
    public void WithEmptiedRegion_AlreadyRecorded_IsUnchanged()
    {
        var marker = new TaskbarMarker().WithEmptiedRegion(5);

        Assert.Equal([5L], marker.WithEmptiedRegion(5).EmptiedRegions);
    }

    [Fact]
    public void WithoutEmptiedRegions_KeepsTheOriginalAutoHideState()
    {
        var marker = new TaskbarMarker { WasAutoHide = true }.WithEmptiedRegion(5).WithoutEmptiedRegions();

        Assert.True(marker.WasAutoHide);
        Assert.Empty(marker.EmptiedRegions);
    }
}
