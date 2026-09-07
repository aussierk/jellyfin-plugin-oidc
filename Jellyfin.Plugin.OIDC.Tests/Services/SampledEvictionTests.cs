using System;
using System.Collections.Concurrent;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class SampledEvictionTests
{
    [Fact]
    public void EvictSampled_WithManyEntries_OnlyScansSampleSize()
    {
        // Enumeration order isn't guaranteed, so this only asserts that exactly one entry is
        // removed to make room - not which one (see StateManagerTests for that trade-off's effect
        // on callers).
        var map = new ConcurrentDictionary<string, DateTimeOffset>();
        for (var i = 0; i < 200; i++)
        {
            map[$"key-{i}"] = DateTimeOffset.UtcNow.AddSeconds(-i);
        }

        var removed = SampledEviction.EvictSampled(map, v => v);

        Assert.True(removed);
        Assert.Equal(199, map.Count);
    }

    [Fact]
    public void EvictSampled_EmptyMap_ReturnsFalse()
    {
        var map = new ConcurrentDictionary<string, DateTimeOffset>();

        Assert.False(SampledEviction.EvictSampled(map, v => v));
    }
}
