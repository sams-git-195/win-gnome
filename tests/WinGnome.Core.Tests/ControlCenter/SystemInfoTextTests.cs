using System.Globalization;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class SystemInfoTextTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void WindowsVersion_Windows11_FixesTheRegistryProductName()
    {
        // Windows 11 still reports "Windows 10" in ProductName; the build number tells them apart.
        Assert.Equal("Windows 11 Home 25H2 (build 26200.6899)", SystemInfoText.WindowsVersion("Windows 10 Home", "25H2", 26200, 6899));
    }

    [Fact]
    public void WindowsVersion_Windows10_KeepsItsName()
    {
        Assert.Equal("Windows 10 Pro 22H2 (build 19045.4046)", SystemInfoText.WindowsVersion("Windows 10 Pro", "22H2", 19045, 4046));
    }

    [Fact]
    public void WindowsVersion_FirstWindows11Build()
    {
        Assert.Equal("Windows 11 Pro 21H2 (build 22000.1)", SystemInfoText.WindowsVersion("Windows 10 Pro", "21H2", 22000, 1));
    }

    [Fact]
    public void WindowsVersion_MissingParts_AreLeftOut()
    {
        Assert.Equal("Windows (build 26100)", SystemInfoText.WindowsVersion(null, "", 26100, 0));
    }

    [Theory]
    [InlineData(17_179_869_184UL, "16.0 GiB")]
    [InlineData(16_852_000_000UL, "15.7 GiB")]
    [InlineData(536_870_912UL, "512.0 MiB")]
    [InlineData(1_099_511_627_776UL, "1.0 TiB")]
    [InlineData(0UL, "0 bytes")]
    [InlineData(1000UL, "1000 bytes")]
    public void Memory_UsesBinaryUnits(ulong bytes, string expected)
    {
        Assert.Equal(expected, SystemInfoText.Memory(bytes, Invariant));
    }

    [Theory]
    [InlineData(512_110_190_592UL, "512.1 GB")]
    [InlineData(1_000_204_886_016UL, "1.0 TB")]
    [InlineData(999_000_000UL, "999.0 MB")]
    [InlineData(0UL, "0 bytes")]
    public void DiskCapacity_UsesDecimalUnitsLikeGnome(ulong bytes, string expected)
    {
        Assert.Equal(expected, SystemInfoText.DiskCapacity(bytes, Invariant));
    }

    [Fact]
    public void Memory_UsesTheCulturesDecimalSeparator()
    {
        Assert.Equal("16,0 GiB", SystemInfoText.Memory(17_179_869_184UL, CultureInfo.GetCultureInfo("de-DE")));
    }

    [Theory]
    [InlineData("  Intel(R) Core(TM) i7-1165G7 @ 2.80GHz  ", "Intel® Core™ i7-1165G7 @ 2.80GHz")]
    [InlineData("AMD Ryzen 7 5800X 8-Core Processor              ", "AMD Ryzen 7 5800X 8-Core Processor")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    public void Processor_TidiesTheRegistryName(string? raw, string expected)
    {
        Assert.Equal(expected, SystemInfoText.Processor(raw));
    }

    [Theory]
    [InlineData("@%SystemRoot%\\System32\\AudioSrv.Dll,-202", "Teams", "Teams")]
    [InlineData("Spotify", "Spotify.exe", "Spotify")]
    [InlineData("", "Firefox", "Firefox")]
    [InlineData(null, "", "Unknown app")]
    [InlineData("  ", null, "Unknown app")]
    public void AudioSessionName_PrefersAReadableDisplayName(string? displayName, string? fallback, string expected)
    {
        Assert.Equal(expected, SystemInfoText.AudioSessionName(displayName, fallback));
    }
}
