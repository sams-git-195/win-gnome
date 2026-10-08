using WinGnome.Core.Collections;

namespace WinGnome.Core.Tests.Collections;

public class LruCacheTests
{
    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(2);

        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsTheValue()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void Set_WhenFull_EvictsTheLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void TryGet_MarksTheEntryAsRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.TryGet("a", out _);
        cache.Set("c", 3);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Set_ExistingKey_ReplacesWithoutEvicting()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("a", 10);

        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal(10, a);
        Assert.True(cache.TryGet("b", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Comparer_IsUsedForKeys()
    {
        var cache = new LruCache<string, int>(2, StringComparer.OrdinalIgnoreCase);
        cache.Set("Path:A", 1);

        Assert.True(cache.TryGet("path:a", out var value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Clear();

        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_CapacityBelowOne_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(capacity));
    }
}
