using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiProfileScopeTests
{
    [Theory]
    [InlineData(0u, WifiProfileScope.AllUsers)]
    [InlineData(2u, WifiProfileScope.ThisUser)]
    [InlineData(1u, WifiProfileScope.GroupPolicy)]
    [InlineData(3u, WifiProfileScope.GroupPolicy)]
    [InlineData(4u, WifiProfileScope.AllUsers)]
    [InlineData(6u, WifiProfileScope.ThisUser)]
    public void FromFlags_ReadsTheProfileFlags(uint flags, WifiProfileScope expected) =>
        Assert.Equal(expected, WifiProfileScopes.FromFlags(flags));

    [Fact]
    public void OverwriteFlags_AllUsers_WritesForAllUsers() =>
        Assert.Equal(0u, WifiProfileScopes.OverwriteFlags(WifiProfileScope.AllUsers));

    [Fact]
    public void OverwriteFlags_ThisUser_KeepsThePerUserProfile() =>
        Assert.Equal(2u, WifiProfileScopes.OverwriteFlags(WifiProfileScope.ThisUser));

    [Fact]
    public void OverwriteFlags_GroupPolicy_IsNotWritable() =>
        Assert.Null(WifiProfileScopes.OverwriteFlags(WifiProfileScope.GroupPolicy));
}
