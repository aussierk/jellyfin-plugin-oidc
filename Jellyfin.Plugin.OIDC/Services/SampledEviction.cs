using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Evicts one entry from a capped <see cref="ConcurrentDictionary{TKey,TValue}"/> to make room
/// under a flood, without an O(n) scan of the whole table: only the first <paramref
/// name="sampleSize"/> entries the enumerator yields are ranked, and the lowest of those is
/// removed. Shared by <see cref="StateManager"/> and <see cref="RateLimitFilter"/>.
/// </summary>
internal static class SampledEviction
{
    internal const int DefaultSampleSize = 64;

    internal static bool EvictSampled<TValue>(
        ConcurrentDictionary<string, TValue> map,
        Func<TValue, DateTimeOffset> rank,
        int sampleSize = DefaultSampleSize)
    {
        string? lowestKey = null;
        var lowest = DateTimeOffset.MaxValue;
        var seen = 0;
        foreach (var (key, value) in map)
        {
            var candidate = rank(value);
            if (candidate < lowest)
            {
                lowest = candidate;
                lowestKey = key;
            }

            if (++seen >= sampleSize)
            {
                break;
            }
        }

        return lowestKey != null && map.TryRemove(lowestKey, out _);
    }
}
