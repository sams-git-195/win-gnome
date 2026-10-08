using System.Text.Json;
using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class TweakBackupTests
{
    [Fact]
    public void NewBackup_IsEmpty()
    {
        var backup = new TweakBackup();

        Assert.Empty(backup.TweakIds);
        Assert.False(backup.Contains("x"));
        Assert.Null(backup.Get("x"));
    }

    [Fact]
    public void SetGetRemove()
    {
        var backup = new TweakBackup();
        var record = new TweakBackupRecord(false, [new TweakBackupEntry("A", "V", false, null)]);

        backup.Set("x", record);

        Assert.True(backup.Contains("X"));
        Assert.Same(record, backup.Get("x"));
        Assert.Equal(["x"], backup.TweakIds);
        Assert.True(backup.Remove("x"));
        Assert.False(backup.Remove("x"));
        Assert.Empty(backup.TweakIds);
    }

    [Fact]
    public void Set_RejectsBlankIdAndNullRecord()
    {
        var backup = new TweakBackup();
        Assert.Throws<ArgumentException>(() => backup.Set(" ", new TweakBackupRecord(false, [])));
        Assert.Throws<ArgumentNullException>(() => backup.Set("x", null!));
    }

    [Fact]
    public void RoundTrip_EveryValueKind()
    {
        var backup = new TweakBackup();
        backup.Set("everything", new TweakBackupRecord(
            KeyCreated: true,
            Entries:
            [
                new TweakBackupEntry(@"Software\A", "Dword", true, RegistryValue.DWord(-5)),
                new TweakBackupEntry(@"Software\A", "Qword", true, RegistryValue.QWord(long.MaxValue)),
                new TweakBackupEntry(@"Software\A", "Text", true, RegistryValue.Text("héllo \"quoted\" \\ back")),
                new TweakBackupEntry(@"Software\A", "Expand", true, RegistryValue.ExpandText("%APPDATA%\\x")),
                new TweakBackupEntry(@"Software\A", "Bin", true, RegistryValue.Binary([0, 1, 2, 254, 255])),
                new TweakBackupEntry(@"Software\A", "EmptyBin", true, RegistryValue.Binary([])),
                new TweakBackupEntry(@"Software\A", "EmptyText", true, RegistryValue.Text("")),
                new TweakBackupEntry(@"Software\B", null, true, RegistryValue.Text("default value")),
                new TweakBackupEntry(@"Software\C", "Missing", false, null),
            ]));
        backup.Set("second", new TweakBackupRecord(false, []));

        var copy = TweakBackup.FromJson(backup.ToJson());

        Assert.Equal(["everything", "second"], copy.TweakIds.OrderBy(i => i));
        var record = copy.Get("everything")!;
        Assert.True(record.KeyCreated);
        Assert.Equal(backup.Get("everything")!.Entries, record.Entries);
        Assert.False(copy.Get("second")!.KeyCreated);
        Assert.Empty(copy.Get("second")!.Entries);
    }

    [Fact]
    public void ToJson_WritesBinaryAsBase64()
    {
        var backup = new TweakBackup();
        backup.Set("x", new TweakBackupRecord(false, [new TweakBackupEntry("A", "Bin", true, RegistryValue.Binary([1, 2, 3]))]));

        var json = backup.ToJson();

        Assert.Contains(Convert.ToBase64String(new byte[] { 1, 2, 3 }), json);
    }

    [Fact]
    public void ToJson_OfAnEmptyBackup_RoundTripsToEmpty()
    {
        Assert.Empty(TweakBackup.FromJson(new TweakBackup().ToJson()).TweakIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("""{ "Tweaks": null }""")]
    [InlineData("""{ "Tweaks": [1, 2] }""")]
    public void FromJson_MalformedInput_GivesEmptyBackup(string? json)
    {
        var backup = TweakBackup.FromJson(json);

        Assert.NotNull(backup);
        Assert.Empty(backup.TweakIds);
    }

    [Fact]
    public void FromJson_SkipsDamagedRecords_ButKeepsGoodOnes()
    {
        const string json = """
            {
              "Version": 1,
              "Tweaks": {
                "good": { "KeyCreated": true, "Entries": [ { "SubKey": "A", "ValueName": "V", "Existed": true, "Kind": "DWord", "Data": "7" } ] },
                "bad-kind": { "Entries": [ { "SubKey": "A", "ValueName": "V", "Existed": true, "Kind": "Bogus", "Data": "7" } ] },
                "bad-number": { "Entries": [ { "SubKey": "A", "ValueName": "V", "Existed": true, "Kind": "DWord", "Data": "seven" } ] },
                "bad-base64": { "Entries": [ { "SubKey": "A", "ValueName": "V", "Existed": true, "Kind": "Binary", "Data": "***" } ] },
                "no-data": { "Entries": [ { "SubKey": "A", "ValueName": "V", "Existed": true, "Kind": "DWord" } ] },
                "no-key": { "Entries": [ { "ValueName": "V", "Existed": false } ] },
                "null-record": null,
                "no-entries": {}
              }
            }
            """;

        var backup = TweakBackup.FromJson(json);

        Assert.Equal(["good"], backup.TweakIds);
        var entry = Assert.Single(backup.Get("good")!.Entries);
        Assert.Equal(RegistryValue.DWord(7), entry.Previous);
        Assert.True(backup.Get("good")!.KeyCreated);
    }

    [Fact]
    public void FromJson_ToleratesCommentsTrailingCommasAndCaseDifferences()
    {
        const string json = """
            {
              // saved by an older build
              "tweaks": {
                "x": { "keycreated": false, "entries": [ { "subkey": "A", "existed": false, }, ], },
              },
            }
            """;

        var backup = TweakBackup.FromJson(json);

        var entry = Assert.Single(backup.Get("x")!.Entries);
        Assert.Equal("A", entry.SubKey);
        Assert.False(entry.Existed);
        Assert.Null(entry.Previous);
    }

    [Fact]
    public void ToJson_IsValidJson()
    {
        var backup = new TweakBackup();
        backup.Set("x", new TweakBackupRecord(true, [new TweakBackupEntry("A", null, true, RegistryValue.Text("v"))]));

        using var document = JsonDocument.Parse(backup.ToJson());

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
