using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class UpdateClassificationTests
{
    [Theory]
    [InlineData(1, false, false, false, false)] // ordinary software update
    [InlineData(1, true, false, false, true)]   // browse-only software update (an optional feature or preview)
    [InlineData(1, false, true, false, false)]
    [InlineData(2, false, false, false, true)]  // driver nobody auto-selected: Optional updates
    [InlineData(2, false, true, false, false)]  // driver Windows installs itself
    [InlineData(2, false, false, true, false)]  // mandatory driver
    [InlineData(2, true, true, false, true)]    // browse-only wins even for an auto-selected driver
    [InlineData(3, false, false, false, false)] // a type we don't know is treated as waiting
    public void IsOptional_ClassifiesByTypeAndFlags(int type, bool browseOnly, bool autoSelect, bool mandatory, bool expected)
    {
        Assert.Equal(expected, UpdateClassification.IsOptional(type, browseOnly, autoSelect, mandatory));
    }
}
