using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class WriteCoalescerTests
{
    [Fact]
    public void Post_WhenIdle_WritesImmediately()
    {
        var coalescer = new WriteCoalescer();

        Assert.False(coalescer.IsBusy);
        Assert.Equal(40, coalescer.Post(40));
        Assert.True(coalescer.IsBusy);
    }

    [Fact]
    public void Post_WhileBusy_QueuesAndKeepsOnlyTheLatest()
    {
        var coalescer = new WriteCoalescer();
        coalescer.Post(40);

        Assert.Null(coalescer.Post(41));
        Assert.Null(coalescer.Post(42));
        Assert.Null(coalescer.Post(43));

        Assert.Equal(43, coalescer.Complete());
    }

    [Fact]
    public void Complete_WithNothingQueued_GoesIdle()
    {
        var coalescer = new WriteCoalescer();
        coalescer.Post(40);

        Assert.Null(coalescer.Complete());
        Assert.False(coalescer.IsBusy);
        Assert.Equal(55, coalescer.Post(55));
    }

    [Fact]
    public void Complete_QueuedValueEqualToTheOneJustWritten_IsNotWrittenAgain()
    {
        var coalescer = new WriteCoalescer();
        coalescer.Post(40);
        coalescer.Post(41);
        coalescer.Post(40);

        Assert.Null(coalescer.Complete());
        Assert.False(coalescer.IsBusy);
    }

    [Fact]
    public void Complete_ChainsThroughSeveralBatches()
    {
        var coalescer = new WriteCoalescer();
        coalescer.Post(10);
        coalescer.Post(20);

        Assert.Equal(20, coalescer.Complete());
        Assert.Null(coalescer.Post(30));
        Assert.Equal(30, coalescer.Complete());
        Assert.Null(coalescer.Complete());
        Assert.False(coalescer.IsBusy);
    }

    [Fact]
    public void Complete_WhenNothingIsRunning_StaysIdle()
    {
        var coalescer = new WriteCoalescer();

        Assert.Null(coalescer.Complete());
        Assert.False(coalescer.IsBusy);
    }
}
