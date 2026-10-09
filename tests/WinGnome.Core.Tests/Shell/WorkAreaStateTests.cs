using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class WorkAreaStateTests
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";

    // An owner above int.MaxValue: the field is an HWND, which is 64-bit.
    private static readonly WorkAreaRecord Bar = new(4_294_967_296, Display1,
        new PixelRect(0, 0, 2560, 1600), new PixelRect(0, 0, 2560, 1540), new PixelRect(0, 40, 2560, 1540));

    private static readonly WorkAreaRecord UpperBar = new(131_479, Display2,
        new PixelRect(-447, -1440, 2993, 0), new PixelRect(-447, -1440, 2993, 0), new PixelRect(-447, -1408, 2993, 0));

    [Fact]
    public void RoundTrip_KeepsEveryFieldAndTheOrder()
    {
        var file = WorkAreaState.Parse(WorkAreaState.Serialize([Bar, UpperBar]));

        Assert.False(file.Unreadable);
        Assert.Equal([Bar, UpperBar], file.Records);
    }

    [Fact]
    public void Serialize_NoRecords_ParsesBackAsNoRecords()
    {
        var file = WorkAreaState.Parse(WorkAreaState.Serialize([]));

        Assert.False(file.Unreadable);
        Assert.Empty(file.Records);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NoFile_NoRecordsAndNotUnreadable(string? json)
    {
        // An absent marker is the normal case: most runs never set a work area directly.
        var file = WorkAreaState.Parse(json);

        Assert.False(file.Unreadable);
        Assert.Empty(file.Records);
    }

    [Fact]
    public void Parse_EmptyRecordList_NoRecordsAndNotUnreadable()
    {
        var file = WorkAreaState.Parse("""{"Records":[]}""");

        Assert.False(file.Unreadable);
        Assert.Empty(file.Records);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"Records\":[")]
    [InlineData("{\"Records\":[{\"Owner\":1")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"Records\":{}}")]
    [InlineData("{}")]
    [InlineData("{\"Other\":1}")]
    public void Parse_CorruptOrForeignJson_IsUnreadable(string json)
    {
        // Never read as "no records": the shrinks it described may still be in effect.
        var file = WorkAreaState.Parse(json);

        Assert.True(file.Unreadable);
        Assert.Empty(file.Records);
    }

    [Theory]
    [InlineData("{\"Records\":[{\"Owner\":1,\"Key\":\"D\",\"Bounds\":\"0,0,2560\",\"Original\":\"0,0,2560,1600\",\"Applied\":\"0,40,2560,1600\"}]}")]
    [InlineData("{\"Records\":[{\"Owner\":1,\"Key\":\"D\",\"Bounds\":\"a,b,c,d\",\"Original\":\"0,0,2560,1600\",\"Applied\":\"0,40,2560,1600\"}]}")]
    [InlineData("{\"Records\":[{\"Owner\":1,\"Key\":\"D\",\"Bounds\":\"\",\"Original\":\"0,0,2560,1600\",\"Applied\":\"0,40,2560,1600\"}]}")]
    [InlineData("{\"Records\":[{\"Owner\":1,\"Key\":\"D\",\"Bounds\":\"0,0,2560,1600,9\",\"Original\":\"0,0,2560,1600\",\"Applied\":\"0,40,2560,1600\"}]}")]
    [InlineData("{\"Records\":[{\"Owner\":1,\"Key\":\"\",\"Bounds\":\"0,0,2560,1600\",\"Original\":\"0,0,2560,1600\",\"Applied\":\"0,40,2560,1600\"}]}")]
    [InlineData("{\"Records\":[{\"Owner\":1,\"Key\":\"D\",\"Bounds\":\"0,0,2560,1600\",\"Applied\":\"0,40,2560,1600\"}]}")]
    [InlineData("{\"Records\":[null]}")]
    public void Parse_BadRecord_IsUnreadable(string json)
    {
        // All or nothing: a half-read marker could strand the shrinks it dropped.
        var file = WorkAreaState.Parse(json);

        Assert.True(file.Unreadable);
        Assert.Empty(file.Records);
    }

    [Fact]
    public void Serialize_WritesRectanglesAsCoordinates()
    {
        Assert.Contains("\"Applied\":\"0,40,2560,1540\"", WorkAreaState.Serialize([Bar]));
        Assert.Contains("\"Key\":\"\\\\\\\\.\\\\DISPLAY1\"", WorkAreaState.Serialize([Bar]));
    }
}
