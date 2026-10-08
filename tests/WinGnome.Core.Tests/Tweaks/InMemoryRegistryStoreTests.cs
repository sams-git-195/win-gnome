using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class InMemoryRegistryStoreTests
{
    [Fact]
    public void SetThenGet()
    {
        var store = new InMemoryRegistryStore();

        store.SetValue(@"Software\Test", "Value", RegistryValue.DWord(3));

        Assert.Equal(RegistryValue.DWord(3), store.GetValue(@"Software\Test", "Value"));
    }

    [Fact]
    public void MissingKeyOrValue_IsNull()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue(@"Software\Test", "Value", RegistryValue.DWord(3));

        Assert.Null(store.GetValue(@"Software\Other", "Value"));
        Assert.Null(store.GetValue(@"Software\Test", "Other"));
    }

    [Fact]
    public void KeysAndValueNamesAreCaseInsensitive()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue(@"Software\Test", "Value", RegistryValue.DWord(3));

        Assert.Equal(RegistryValue.DWord(3), store.GetValue(@"SOFTWARE\test", "VALUE"));
        Assert.True(store.KeyExists(@"software\TEST"));

        store.SetValue(@"software\test", "value", RegistryValue.DWord(4));
        Assert.Equal(RegistryValue.DWord(4), store.GetValue(@"Software\Test", "Value"));
        Assert.Equal(1, store.KeyCount);
    }

    [Fact]
    public void NullAndEmptyValueNames_AreTheDefaultValue()
    {
        var store = new InMemoryRegistryStore();

        store.SetValue("A", null, RegistryValue.Text("x"));
        Assert.Equal(RegistryValue.Text("x"), store.GetValue("A", ""));

        store.SetValue("A", "", RegistryValue.Text("y"));
        Assert.Equal(RegistryValue.Text("y"), store.GetValue("A", null));

        store.DeleteValue("A", null);
        Assert.Null(store.GetValue("A", ""));
    }

    [Fact]
    public void DefaultValue_IsSeparateFromNamedValues()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue("A", null, RegistryValue.Text("default"));
        store.SetValue("A", "Named", RegistryValue.Text("named"));

        store.DeleteValue("A", "Named");

        Assert.Equal(RegistryValue.Text("default"), store.GetValue("A", null));
    }

    [Fact]
    public void SetValue_Overwrites_AndCanChangeKind()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue("A", "V", RegistryValue.DWord(1));

        store.SetValue("A", "V", RegistryValue.Text("one"));

        Assert.Equal(RegistryValue.Text("one"), store.GetValue("A", "V"));
    }

    [Fact]
    public void DeleteValue_RemovesIt_ButKeepsTheKey()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue("A", "V", RegistryValue.DWord(1));

        store.DeleteValue("A", "V");

        Assert.Null(store.GetValue("A", "V"));
        Assert.True(store.KeyExists("A"));
    }

    [Fact]
    public void DeleteValue_OnMissingKeyOrValue_IsANoOp()
    {
        var store = new InMemoryRegistryStore();

        store.DeleteValue("Nope", "V");
        store.SetValue("A", "V", RegistryValue.DWord(1));
        store.DeleteValue("A", "Other");

        Assert.Equal(RegistryValue.DWord(1), store.GetValue("A", "V"));
    }

    [Fact]
    public void KeyExists_ImpliesParentsOfCreatedKeys()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue(@"A\B\C", "V", RegistryValue.DWord(1));

        Assert.True(store.KeyExists("A"));
        Assert.True(store.KeyExists(@"A\B"));
        Assert.True(store.KeyExists(@"A\B\C"));
        Assert.False(store.KeyExists(@"A\B\C\D"));
        Assert.False(store.KeyExists(@"A\X"));
        Assert.False(store.KeyExists("AB"));
    }

    [Fact]
    public void KeyPaths_AreNormalised()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue(@"\A\B\", "V", RegistryValue.DWord(1));

        Assert.Equal(RegistryValue.DWord(1), store.GetValue(@"A\B", "V"));
        Assert.Equal(RegistryValue.DWord(1), store.GetValue("A/B", "V"));
    }

    [Fact]
    public void DeleteKeyTree_RemovesTheKeyAndAllDescendants_ButNotSiblings()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue(@"A\B", "V", RegistryValue.DWord(1));
        store.SetValue(@"A\B\C", "V", RegistryValue.DWord(2));
        store.SetValue(@"A\B\C\D", "V", RegistryValue.DWord(3));
        store.SetValue(@"A\BC", "V", RegistryValue.DWord(4));
        store.SetValue(@"A\Z", "V", RegistryValue.DWord(5));

        store.DeleteKeyTree(@"A\B");

        Assert.False(store.KeyExists(@"A\B"));
        Assert.False(store.KeyExists(@"A\B\C\D"));
        Assert.Null(store.GetValue(@"A\B\C", "V"));
        Assert.Equal(RegistryValue.DWord(4), store.GetValue(@"A\BC", "V"));
        Assert.Equal(RegistryValue.DWord(5), store.GetValue(@"A\Z", "V"));
        Assert.True(store.KeyExists("A"));
    }

    [Fact]
    public void DeleteKeyTree_OnMissingKey_IsANoOp()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue("A", "V", RegistryValue.DWord(1));

        store.DeleteKeyTree("Nope");

        Assert.Equal(RegistryValue.DWord(1), store.GetValue("A", "V"));
    }

    [Fact]
    public void DeleteKeyTree_CanRemoveAKeyThatOnlyExistsAsAParent()
    {
        var store = new InMemoryRegistryStore();
        store.SetValue(@"A\B\C", "V", RegistryValue.DWord(1));

        store.DeleteKeyTree(@"A\B");

        Assert.False(store.KeyExists(@"A\B"));
        Assert.False(store.KeyExists(@"A\B\C"));
    }

    [Fact]
    public void BinaryData_IsIsolatedFromCallers()
    {
        var store = new InMemoryRegistryStore();
        var data = new byte[] { 1, 2, 3 };
        store.SetValue("A", "Bin", new RegistryValue(RegistryValueKind.Binary, data));

        data[0] = 99;
        var read = store.GetValue("A", "Bin")!;
        ((byte[])read.Data)[1] = 99;

        Assert.Equal(RegistryValue.Binary([1, 2, 3]), store.GetValue("A", "Bin"));
    }

    [Fact]
    public void SetValue_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryRegistryStore().SetValue("A", "V", null!));
    }
}
