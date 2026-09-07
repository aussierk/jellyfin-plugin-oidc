using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.OIDC.Services;

/// 
/// Fixed-window per-IP rate limit applied as an MVC action filter.
/// Works inside Jellyfin's plugin model without requiring access to IApplicationBuilder.
/// 
[AttributeUsage(AttributeTargets.Method)]
public sealed class RateLimitAttribute : Attribute, IAsyncActionFilter
{
    // Shared across all instances: key = "policyName:clientIP"
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _counters = new();

    // Hard cap prevents unbounded memory growth from a flood of distinct client IPs.
    private const int MaxCounters = 20_000;

    // Well above the largest windowSeconds configured on any [RateLimit] usage (60s today).
    private static readonly TimeSpan MaxStaleAge = TimeSpan.FromSeconds(600);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private static readonly Timer _cleanupTimer = new(Cleanup, null, CleanupInterval, CleanupInterval);

    private readonly int _maxRequests;
    private readonly int _windowSeconds;
    private readonly string _policyName;

    public RateLimitAttribute(string policyName, int maxRequests, int windowSeconds)
    {
        _policyName = policyName;
        _maxRequests = maxRequests;
        _windowSeconds = windowSeconds;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var ip = GetClientIp(context.HttpContext);
        var key = $"{_policyName}:{ip}";
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(_windowSeconds);

        if (!_counters.ContainsKey(key) && _counters.Count >= MaxCounters)
        {
            // Sample-and-evict, not a full O(n) scan: this path runs on every request once the cap
            // is hit, i.e. mid-flood. The Cleanup timer does the thorough sweep.
            SampledEviction.EvictSampled(_counters, e => e.WindowStart);
        }

        var entry = _counters.AddOrUpdate(
            key,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if (now - existing.WindowStart >= window)
                {
                    // Window has expired - start a new one.
                    return new RateLimitEntry { Count = 1, WindowStart = now };
                }

                return new RateLimitEntry { Count = existing.Count + 1, WindowStart = existing.WindowStart };
            });

        if (entry.Count > _maxRequests)
        {
            var retryAfter = (int)Math.Ceiling((_windowSeconds - (now - entry.WindowStart).TotalSeconds));
            context.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
            return;
        }

        await next().ConfigureAwait(false);
    }

    // Jellyfin's own middleware already resolves X-Forwarded-For into RemoteIpAddress for trusted proxies.
    private static string GetClientIp(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    internal static void Cleanup(object? state)
    {
        var cutoff = DateTimeOffset.UtcNow - MaxStaleAge;
        foreach (var (key, entry) in _counters)
        {
            if (entry.WindowStart < cutoff)
            {
                _counters.TryRemove(key, out _);
            }
        }
    }

    private sealed class RateLimitEntry
    {
        public int Count { get; init; }
        public DateTimeOffset WindowStart { get; init; }
    }
}
