using System.Globalization;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class AppSizeTextTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, null)]
    [InlineData(-5L, null)]
    [InlineData(1L, "1 KB")]
    [InlineData(1023L, "1023 KB")]
    [InlineData(1024L, "1.0 MB")]
    [InlineData(66458L, "64.9 MB")]
    [InlineData(1048575L, "1024.0 MB")]
    [InlineData(1048576L, "1.00 GB")]
    [InlineData(1268777L, "1.21 GB")]
    public void Format_UsesBinaryUnits(long? kb, string? expected) =>
        Assert.Equal(expected, AppSizeText.Format(kb, Invariant));

    [Fact]
    public void Format_UsesTheCulturesDecimalSeparator() =>
        Assert.Equal("64,9 MB", AppSizeText.Format(66458, CultureInfo.GetCultureInfo("de-DE")));
}

public class InstallDateTextTests
{
    [Theory]
    [InlineData("20240131", 2024, 1, 31)]
    [InlineData(" 20000229 ", 2000, 2, 29)]
    public void Parse_Yyyymmdd_GivesTheDate(string raw, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), InstallDateText.Parse(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2024-01-31")]
    [InlineData("31/01/2024")]
    [InlineData("20241301")]
    [InlineData("20230229")]
    [InlineData("2024013")]
    [InlineData("202401310")]
    [InlineData("abcdefgh")]
    public void Parse_Malformed_GivesNull(string? raw) =>
        Assert.Null(InstallDateText.Parse(raw));

    [Fact]
    public void Format_UsesTheCulturesShortDate()
    {
        Assert.Equal("31/01/2024", InstallDateText.Format("20240131", CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("1/31/2024", InstallDateText.Format("20240131", CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Format_Malformed_GivesNull() =>
        Assert.Null(InstallDateText.Format("2024", CultureInfo.InvariantCulture));
}
