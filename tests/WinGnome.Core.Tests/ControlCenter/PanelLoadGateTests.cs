using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class PanelLoadGateTests
{
    [Fact]
    public void New_IsClosedAndDropsEveryLoad()
    {
        var gate = new PanelLoadGate();
        var sequence = gate.Begin("");

        Assert.False(gate.IsOpen);
        Assert.False(gate.MayShow(gate.Generation, "", sequence));
    }

    [Fact]
    public void MayShow_NewestLoadWhileOpen_True()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        var generation = gate.Generation;
        var sequence = gate.Begin("");

        Assert.True(gate.MayShow(generation, "", sequence));
    }

    [Fact]
    public void MayShow_SupersededOnTheSameChannel_False()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        var generation = gate.Generation;
        var older = gate.Begin("");
        var newer = gate.Begin("");

        Assert.False(gate.MayShow(generation, "", older));
        Assert.True(gate.MayShow(generation, "", newer));
    }

    [Fact]
    public void MayShow_DifferentChannels_NeverSupersedeEachOther()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        var generation = gate.Generation;
        var installed = gate.Begin("installed");
        var startup = gate.Begin("startup");

        Assert.True(gate.MayShow(generation, "installed", installed));
        Assert.True(gate.MayShow(generation, "startup", startup));
    }

    [Fact]
    public void Begin_SequenceIncreasesAcrossChannels()
    {
        var gate = new PanelLoadGate();
        gate.Opened();

        Assert.Equal(1, gate.Begin("a"));
        Assert.Equal(2, gate.Begin("b"));
        Assert.Equal(3, gate.Begin("a"));
    }

    [Fact]
    public void MayShow_AfterClose_False()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        var generation = gate.Generation;
        var sequence = gate.Begin("");
        gate.Closed();

        Assert.False(gate.IsOpen);
        Assert.False(gate.MayShow(generation, "", sequence));
    }

    [Fact]
    public void MayShow_LoadStartedWhileClosed_False()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        gate.Closed();
        var generation = gate.Generation;
        var sequence = gate.Begin("");

        // A reload posted from a writer thread can arrive after the panel closed; even though its generation and
        // channel match (nothing has reopened), its result must never be shown.
        Assert.False(gate.MayShow(generation, "", sequence));
    }

    [Fact]
    public void MayShow_StaleGenerationEvenWithAMatchingChannel_False()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        gate.Closed();
        gate.Opened();
        var sequence = gate.Begin("");

        Assert.False(gate.MayShow(gate.Generation - 1, "", sequence));
        Assert.True(gate.MayShow(gate.Generation, "", sequence));
    }

    [Fact]
    public void MayShow_LoadFromAPreviousOpen_False()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        var firstOpen = gate.Generation;
        var firstLoad = gate.Begin("");
        gate.Closed();
        gate.Opened();
        var secondLoad = gate.Begin("");

        Assert.False(gate.MayShow(firstOpen, "", firstLoad));
        Assert.True(gate.MayShow(gate.Generation, "", secondLoad));
    }

    [Fact]
    public void MayShow_ChannelFromAPreviousOpen_False()
    {
        var gate = new PanelLoadGate();
        gate.Opened();
        var load = gate.Begin("details:x");
        var generation = gate.Generation;
        gate.Closed();
        gate.Opened();

        // The reopen cleared the channel table: the old load must not show even with its generation and sequence.
        Assert.False(gate.MayShow(generation, "details:x", load));
    }

    [Fact]
    public void Opened_AndClosed_EachStartANewGeneration()
    {
        var gate = new PanelLoadGate();

        gate.Opened();
        Assert.Equal(1, gate.Generation);
        gate.Closed();
        Assert.Equal(2, gate.Generation);
        gate.Opened();
        Assert.Equal(3, gate.Generation);
    }
}
