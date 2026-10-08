using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class RegistryValueTests
{
    [Fact]
    public void Factories_SetKindAndData()
    {
        Assert.Equal(new RegistryValue(RegistryValueKind.String, "x"), RegistryValue.Text("x"));
        Assert.Equal(new RegistryValue(RegistryValueKind.ExpandString, "%PATH%"), RegistryValue.ExpandText("%PATH%"));
        Assert.Equal(new RegistryValue(RegistryValueKind.DWord, 5), RegistryValue.DWord(5));
        Assert.Equal(new RegistryValue(RegistryValueKind.QWord, 5L), RegistryValue.QWord(5));
        Assert.Equal(RegistryValueKind.Binary, RegistryValue.Binary([1]).Kind);
    }

    [Fact]
    public void BinaryValues_CompareByContent()
    {
        var a = RegistryValue.Binary([1, 2, 3]);
        var b = new RegistryValue(RegistryValueKind.Binary, new byte[] { 1, 2, 3 });

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void BinaryValues_WithDifferentContentOrLength_AreDifferent()
    {
        Assert.NotEqual(RegistryValue.Binary([1, 2, 3]), RegistryValue.Binary([1, 2, 4]));
        Assert.NotEqual(RegistryValue.Binary([1, 2, 3]), RegistryValue.Binary([1, 2]));
        Assert.Equal(RegistryValue.Binary([]), RegistryValue.Binary([]));
    }

    [Fact]
    public void Binary_FactoryCopiesTheArray()
    {
        var source = new byte[] { 1, 2 };
        var value = RegistryValue.Binary(source);

        source[0] = 99;

        Assert.Equal(new byte[] { 1, 2 }, (byte[])value.Data);
    }

    [Fact]
    public void KindMatters()
    {
        Assert.NotEqual(RegistryValue.Text("1"), RegistryValue.ExpandText("1"));
        Assert.NotEqual(RegistryValue.DWord(1), RegistryValue.QWord(1));
        Assert.NotEqual(RegistryValue.Text("1"), RegistryValue.DWord(1));
    }

    [Fact]
    public void ScalarValues_CompareByValue()
    {
        Assert.Equal(RegistryValue.DWord(7), RegistryValue.DWord(7));
        Assert.NotEqual(RegistryValue.DWord(7), RegistryValue.DWord(8));
        Assert.Equal(RegistryValue.Text("a"), RegistryValue.Text("a"));
        Assert.NotEqual(RegistryValue.Text("a"), RegistryValue.Text("A"));
        Assert.Equal(RegistryValue.DWord(7).GetHashCode(), RegistryValue.DWord(7).GetHashCode());
    }

    [Fact]
    public void NullIsNeverEqual()
    {
        Assert.False(RegistryValue.DWord(0).Equals(null));
    }
}
