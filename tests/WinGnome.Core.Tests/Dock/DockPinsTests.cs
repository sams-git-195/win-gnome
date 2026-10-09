using WinGnome.Core.Dock;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Dock;

public class DockPinsTests
{
    private static List<PinnedApp> Pins(params string[] ids) =>
        ids.Select(id => new PinnedApp { Name = id, LaunchId = id }).ToList();

    private static string[] Ids(IEnumerable<PinnedApp> pins) => pins.Select(p => p.LaunchId).ToArray();

    private static string Joined(IEnumerable<PinnedApp> pins) => string.Join(',', Ids(pins));

    [Fact]
    public void IsPinned_IgnoresCase()
    {
        Assert.True(DockPins.IsPinned(Pins("A", "b"), "B"));
        Assert.False(DockPins.IsPinned(Pins("A"), "C"));
    }

    [Fact]
    public void Insert_AtIndex_KeepsOrderOfAddedPins()
    {
        var result = DockPins.Insert(Pins("a", "b", "c"), Pins("x", "y"), 1);

        Assert.Equal(["a", "x", "y", "b", "c"], Ids(result));
    }

    [Fact]
    public void Insert_SkipsDuplicatesAndBlankIds()
    {
        var added = Pins("B", "x", "x");
        added.Add(new PinnedApp { LaunchId = " " });

        var result = DockPins.Insert(Pins("a", "b"), added, 99);

        Assert.Equal(["a", "b", "x"], Ids(result));
    }

    [Theory]
    [InlineData(-4, "x,a")]
    [InlineData(10, "a,x")]
    public void Insert_ClampsTheIndex(int index, string expected)
    {
        Assert.Equal(expected, Joined(DockPins.Insert(Pins("a"), Pins("x"), index)));
    }

    [Fact]
    public void Insert_DoesNotModifyTheInput()
    {
        var pins = Pins("a");
        DockPins.Insert(pins, Pins("x"), 0);

        Assert.Equal(["a"], Ids(pins));
    }

    [Fact]
    public void Remove_IgnoresCase()
    {
        Assert.Equal(["a", "c"], Ids(DockPins.Remove(Pins("a", "B", "c"), "b")));
    }

    [Theory]
    [InlineData("a", 2, "b,c,a")]
    [InlineData("c", 0, "c,a,b")]
    [InlineData("b", 1, "a,b,c")]
    [InlineData("a", 50, "b,c,a")]
    [InlineData("zz", 0, "a,b,c")]
    public void Move(string id, int index, string expected)
    {
        Assert.Equal(expected, Joined(DockPins.Move(Pins("a", "b", "c"), id, index)));
    }

    [Fact]
    public void Reorder_FollowsTheGivenOrder()
    {
        Assert.Equal(["c", "a", "b"], Ids(DockPins.Reorder(Pins("a", "b", "c"), ["C", "a", "b"])));
    }

    [Fact]
    public void Reorder_IgnoresUnknownIds_AndKeepsMissingPinsAtTheEnd()
    {
        Assert.Equal(["b", "a", "c"], Ids(DockPins.Reorder(Pins("a", "b", "c"), ["zz", "b", "b"])));
    }

    [Theory]
    [InlineData(@"C:\Apps\tool.exe", true)]
    [InlineData(@"C:\Users\me\Desktop\Game.LNK", true)]
    [InlineData(@"C:\docs\readme.txt", false)]
    [InlineData(@"C:\folder", false)]
    [InlineData(".exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPinnableFile(string? path, bool expected)
    {
        Assert.Equal(expected, DockPins.IsPinnableFile(path));
    }

    [Fact]
    public void FromFile_UsesTheFileNameAsTheName()
    {
        var pin = DockPins.FromFile(@"C:\Users\me\Desktop\paint.net.lnk");

        Assert.Equal("Paint.net", pin.Name);
        Assert.Equal(@"C:\Users\me\Desktop\paint.net.lnk", pin.LaunchId);
    }

    [Fact]
    public void Find_IgnoresCase_AndReturnsNullWhenMissing()
    {
        var pins = Pins("a", "B");

        Assert.Same(pins[1], DockPins.Find(pins, "b"));
        Assert.Null(DockPins.Find(pins, "c"));
    }

    [Fact]
    public void SetRunAsAdministrator_ChangesOnlyThatPin_AndKeepsItsOtherFields()
    {
        var pins = Pins("a", "b");
        pins[1].Arguments = "--dev";

        var result = DockPins.SetRunAsAdministrator(pins, "B", true);

        Assert.Equal(["a", "b"], Ids(result));
        Assert.False(result[0].RunAsAdministrator);
        Assert.True(result[1].RunAsAdministrator);
        Assert.Equal("b", result[1].Name);
        Assert.Equal("--dev", result[1].Arguments);
    }

    [Fact]
    public void SetRunAsAdministrator_DoesNotModifyTheInput()
    {
        var pins = Pins("a");

        _ = DockPins.SetRunAsAdministrator(pins, "a", true);

        Assert.False(pins[0].RunAsAdministrator);
    }

    [Fact]
    public void SetRunAsAdministrator_Off_ClearsTheFlag()
    {
        var pins = Pins("a");
        pins[0].RunAsAdministrator = true;

        Assert.False(DockPins.SetRunAsAdministrator(pins, "a", false)[0].RunAsAdministrator);
    }

    [Fact]
    public void SetRunAsAdministrator_UnknownId_ReturnsAnUnchangedCopy()
    {
        var pins = Pins("a");

        var result = DockPins.SetRunAsAdministrator(pins, "zz", true);

        Assert.Equal(["a"], Ids(result));
        Assert.False(result[0].RunAsAdministrator);
    }
}
