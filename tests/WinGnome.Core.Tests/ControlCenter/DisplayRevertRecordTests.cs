using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class DisplayRevertRecordTests
{
    private static readonly IReadOnlyList<DisplaySetting> Original =
    [
        new(@"\\.\DISPLAY1", 1920, 1080, 60, 0, 0, IsPrimary: true),
        new(@"\\.\DISPLAY2", 2560, 1440, 144, -2560, -200, IsPrimary: false),
    ];

    private static readonly IReadOnlyList<DisplaySetting> Target =
    [
        new(@"\\.\DISPLAY1", 1280, 720, 60, 0, 0, IsPrimary: true),
        new(@"\\.\DISPLAY2", 2560, 1440, 144, -2560, -200, IsPrimary: false),
    ];

    [Fact]
    public void SerializeThenParse_RoundTrips()
    {
        var parsed = DisplayRevertRecord.Parse(DisplayRevertRecord.Serialize(new DisplayRevert(Original, Target)));

        Assert.NotNull(parsed);
        Assert.Equal(Original, parsed.Original);
        Assert.Equal(Target, parsed.Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"Original\": 5}")]
    [InlineData("{}")]
    [InlineData("{\"Original\": [], \"Target\": []}")]
    [InlineData("null")]
    public void Parse_MalformedOrEmpty_ReturnsNull(string text)
    {
        Assert.Null(DisplayRevertRecord.Parse(text));
    }

    [Fact]
    public void Parse_WithoutTarget_ReturnsNull()
    {
        var text = DisplayRevertRecord.Serialize(new DisplayRevert(Original, []));

        Assert.Null(DisplayRevertRecord.Parse(text));
    }

    [Fact]
    public void Parse_DropsEntriesWithoutADeviceOrSize()
    {
        const string text = """
            {"Original":[
              {"DeviceName":"","Width":1920,"Height":1080,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":true},
              {"DeviceName":"\\\\.\\DISPLAY2","Width":0,"Height":1080,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":false},
              {"DeviceName":"\\\\.\\DISPLAY3","Width":1280,"Height":720,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":true}
            ],
            "Target":[
              {"DeviceName":"\\\\.\\DISPLAY3","Width":1920,"Height":1080,"RefreshHz":60,"X":0,"Y":0,"IsPrimary":true}
            ]}
            """;

        var parsed = DisplayRevertRecord.Parse(text);

        Assert.NotNull(parsed);
        Assert.Equal([new DisplaySetting(@"\\.\DISPLAY3", 1280, 720, 60, 0, 0, IsPrimary: true)], parsed.Original);
    }

    [Fact]
    public void IsStillApplied_CurrentEqualsTarget_True()
    {
        Assert.True(DisplayRevertRecord.IsStillApplied(new DisplayRevert(Original, Target), Target));
    }

    [Fact]
    public void IsStillApplied_CurrentIsTheOriginal_False()
    {
        // A reboot or a later change already dropped the unconfirmed mode: there is nothing to revert.
        Assert.False(DisplayRevertRecord.IsStillApplied(new DisplayRevert(Original, Target), Original));
    }

    [Fact]
    public void IsStillApplied_DifferentRefresh_False()
    {
        var current = new[] { Target[0] with { RefreshHz = 50 }, Target[1] };

        Assert.False(DisplayRevertRecord.IsStillApplied(new DisplayRevert(Original, Target), current));
    }

    [Fact]
    public void IsStillApplied_TargetDisplayMissing_False()
    {
        Assert.False(DisplayRevertRecord.IsStillApplied(new DisplayRevert(Original, Target), [Target[0]]));
    }

    [Fact]
    public void IsStillApplied_DeviceNamesIgnoreCase()
    {
        var current = Target.Select(t => t with { DeviceName = t.DeviceName.ToLowerInvariant() }).ToList();

        Assert.True(DisplayRevertRecord.IsStillApplied(new DisplayRevert(Original, Target), current));
    }
}
