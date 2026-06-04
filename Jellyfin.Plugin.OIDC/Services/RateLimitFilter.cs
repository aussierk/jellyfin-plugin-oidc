using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Fixed-window per-IP rate limit applied as an MVC action filter.
/// Works inside Jellyfin's plugin model without requiring access to IApplicationBuilder.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RateLimitAttribute : Attribute, IAsyncActionFilter
{
    // Shared across all instances: key = "policyName:clientIP"
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _counters = new();

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

        var entry = _counters.AddOrUpdate(
            key,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if (now - existing.WindowStart >= window)
                {
                    // Window has expired — start a new one.
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

    private static string GetClientIp(HttpContext context)
    {
        // Jellyfin honours X-Forwarded-For for trusted proxies, which updates
        // RemoteIpAddress via its own middleware. Use it directly.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private sealed class RateLimitEntry
    {
        public int Count { get; init; }
        public DateTimeOffset WindowStart { get; init; }
    }
}
