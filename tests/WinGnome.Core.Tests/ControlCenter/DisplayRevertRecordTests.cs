using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class DisplayRevertRecordTests
{
    private static readonly IReadOnlyList<DisplaySetting> Sample =
    [
        new(@"\\.\DISPLAY1", 1920, 1080, 60, 0, 0, IsPrimary: true),
        new(@"\\.\DISPLAY2", 2560, 1440, 144, -2560, -200, IsPrimary: false),
    ];

    [Fact]
    public void SerializeThenParse_RoundTrips()
    {
        var parsed = DisplayRevertRecord.Parse(DisplayRevertRecord.Serialize(Sample));

        Assert.NotNull(parsed);
        Assert.Equal(Sample, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"Displays\": 5}")]
    [InlineData("{}")]
    [InlineData("{\"Displays\": []}")]
    [InlineData("null")]
    public void Parse_MalformedOrEmpty_ReturnsNull(string text)
    {
        Assert.Null(DisplayRevertRecord.Parse(text));
    }

    [Fact]
    public void Parse_DropsEntriesWithoutADeviceOrSize()
    {
        const string text = """
            {"Displays":[
              {"DeviceName":"","Width":1920,"Height":1080,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":true},
              {"DeviceName":"\\\\.\\DISPLAY2","Width":0,"Height":1080,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":false},
              {"DeviceName":"\\\\.\\DISPLAY3","Width":1280,"Height":720,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":true}
            ]}
            """;

        var parsed = DisplayRevertRecord.Parse(text);

        Assert.Equal([new DisplaySetting(@"\\.\DISPLAY3", 1280, 720, 60, 0, 0, IsPrimary: true)], parsed);
    }
}
