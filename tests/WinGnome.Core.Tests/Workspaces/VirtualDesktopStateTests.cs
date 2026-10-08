using WinGnome.Core.Workspaces;

namespace WinGnome.Core.Tests.Workspaces;

public class VirtualDesktopStateTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static byte[] Blob(params Guid[] ids) => ids.SelectMany(g => g.ToByteArray()).ToArray();

    [Fact]
    public void ParsesCountAndCurrentIndex()
    {
        var state = VirtualDesktopState.Parse(Blob(A, B, C), B.ToByteArray());

        Assert.Equal(new VirtualDesktopState(3, 1), state);
    }

    [Fact]
    public void FirstAndLastDesktopsAreFound()
    {
        Assert.Equal(0, VirtualDesktopState.Parse(Blob(A, B, C), A.ToByteArray()).CurrentIndex);
        Assert.Equal(2, VirtualDesktopState.Parse(Blob(A, B, C), C.ToByteArray()).CurrentIndex);
    }

    [Fact]
    public void SingleDesktop()
    {
        Assert.Equal(new VirtualDesktopState(1, 0), VirtualDesktopState.Parse(Blob(A), A.ToByteArray()));
    }

    [Fact]
    public void UnknownCurrentId_MeansIndexZero()
    {
        Assert.Equal(new VirtualDesktopState(3, 0), VirtualDesktopState.Parse(Blob(A, B, C), Guid.NewGuid().ToByteArray()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void MissingOrMalformedCurrentId_MeansIndexZero(byte[]? current)
    {
        Assert.Equal(new VirtualDesktopState(2, 0), VirtualDesktopState.Parse(Blob(A, B), current));
    }

    [Fact]
    public void NullOrEmptyDesktopList_IsOneDesktop()
    {
        Assert.Equal(new VirtualDesktopState(1, 0), VirtualDesktopState.Parse(null, A.ToByteArray()));
        Assert.Equal(new VirtualDesktopState(1, 0), VirtualDesktopState.Parse([], A.ToByteArray()));
        Assert.Equal(new VirtualDesktopState(1, 0), VirtualDesktopState.Parse(null, null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(31)]
    public void MalformedLength_IsOneDesktop(int length)
    {
        var blob = new byte[length];
        Assert.Equal(new VirtualDesktopState(1, 0), VirtualDesktopState.Parse(blob, A.ToByteArray()));
    }

    [Fact]
    public void MalformedBlob_IgnoresEvenAMatchingCurrentId()
    {
        var blob = Blob(A, B).Concat(new byte[] { 9 }).ToArray();
        Assert.Equal(new VirtualDesktopState(1, 0), VirtualDesktopState.Parse(blob, B.ToByteArray()));
    }

    [Fact]
    public void ManyDesktops()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(new VirtualDesktopState(20, 13), VirtualDesktopState.Parse(Blob(ids), ids[13].ToByteArray()));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 3, 3)]
    [InlineData(3, 0, -3)]
    [InlineData(2, 1, -1)]
    public void StepsTo_IsSigned(int from, int to, int expected)
    {
        Assert.Equal(expected, VirtualDesktopState.StepsTo(from, to));
    }
}
