using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class ConsentValueTests
{
    [Theory]
    [InlineData(null, ConsentState.Absent)]
    [InlineData("", ConsentState.Absent)]
    [InlineData("  ", ConsentState.Absent)]
    [InlineData("Allow", ConsentState.Allow)]
    [InlineData("allow", ConsentState.Allow)]
    [InlineData(" Deny ", ConsentState.Deny)]
    [InlineData("DENY", ConsentState.Deny)]
    [InlineData("Prompt", ConsentState.Prompt)]
    [InlineData("Maybe", ConsentState.Unknown)]
    [InlineData("1", ConsentState.Unknown)]
    public void Parse_ReadsTheStoredText(string? text, ConsentState expected)
    {
        Assert.Equal(expected, ConsentValue.Parse(text));
    }

    [Theory]
    [InlineData(ConsentState.Absent, true)]
    [InlineData(ConsentState.Allow, true)]
    [InlineData(ConsentState.Deny, false)]
    [InlineData(ConsentState.Prompt, false)]
    [InlineData(ConsentState.Unknown, false)]
    public void IsAllowed_AbsentCountsAsAllowed(ConsentState state, bool expected)
    {
        Assert.Equal(expected, ConsentValue.IsAllowed(state));
    }

    [Fact]
    public void ToText_WritesAllowAndDeny()
    {
        Assert.Equal("Allow", ConsentValue.ToText(true));
        Assert.Equal("Deny", ConsentValue.ToText(false));
    }

    [Theory]
    [InlineData(ConsentState.Allow, ConsentState.Allow, ConsentState.Allow, true, true)]
    [InlineData(ConsentState.Allow, ConsentState.Allow, ConsentState.Absent, true, true)]
    [InlineData(ConsentState.Allow, ConsentState.Allow, ConsentState.Deny, false, true)]
    [InlineData(ConsentState.Allow, ConsentState.Allow, ConsentState.Prompt, false, true)]
    [InlineData(ConsentState.Allow, ConsentState.Allow, ConsentState.Unknown, false, false)]
    [InlineData(ConsentState.Deny, ConsentState.Allow, ConsentState.Allow, false, false)]
    [InlineData(ConsentState.Deny, ConsentState.Deny, ConsentState.Deny, false, false)]
    [InlineData(ConsentState.Allow, ConsentState.Deny, ConsentState.Allow, true, false)]
    [InlineData(ConsentState.Allow, ConsentState.Deny, ConsentState.Deny, false, false)]
    [InlineData(ConsentState.Absent, ConsentState.Absent, ConsentState.Allow, true, true)]
    public void For_DeviceOffForcesOffAndUserOffLocksTheRow(ConsentState device, ConsentState user, ConsentState app, bool isOn, bool canChange)
    {
        Assert.Equal(new ConsentRowState(isOn, canChange), ConsentEffective.For(device, user, app));
    }

    [Theory]
    [InlineData(ConsentState.Allow, ConsentState.Allow, true, true)]
    [InlineData(ConsentState.Allow, ConsentState.Absent, true, true)]
    [InlineData(ConsentState.Allow, ConsentState.Deny, false, true)]
    [InlineData(ConsentState.Allow, ConsentState.Unknown, false, false)]
    [InlineData(ConsentState.Deny, ConsentState.Allow, false, false)]
    [InlineData(ConsentState.Deny, ConsentState.Deny, false, false)]
    public void ForMaster_DeviceOffForcesOffAndUnchangeable(ConsentState device, ConsentState master, bool isOn, bool canChange)
    {
        Assert.Equal(new ConsentRowState(isOn, canChange), ConsentEffective.ForMaster(device, master));
    }
}
