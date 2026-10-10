using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiProfileNameTests
{
    [Fact]
    public void Choose_NoCollision_UsesTheSsidText() =>
        Assert.Equal("Cafe", WifiProfileName.Choose("Cafe", "Cafe"u8, ["Home", "Work"]));

    [Fact]
    public void Choose_Collision_AddsAStableHexSuffix()
    {
        var first = WifiProfileName.Choose("Cafe", "Cafe"u8, ["cafe"]);
        var second = WifiProfileName.Choose("Cafe", "Cafe"u8, ["cafe"]);

        Assert.Equal("Cafe (6D68)", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Choose_DifferentBytesSameText_GetDifferentSuffixes() =>
        Assert.NotEqual(
            WifiProfileName.Choose("Net", [0x4E, 0x65, 0x74], ["Net"]),
            WifiProfileName.Choose("Net", [0x4E, 0x65, 0x74, 0x20], ["Net"]));

    [Fact]
    public void Choose_EmptyText_FallsBackToAName() =>
        Assert.Equal("Wi-Fi", WifiProfileName.Choose("", [], []));
}
