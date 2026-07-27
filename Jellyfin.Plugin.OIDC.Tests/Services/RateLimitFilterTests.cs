using System.Net;
using System.Reflection;
using Jellyfin.Plugin.OIDC.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class RateLimitFilterTests
{
    // Each test uses a unique policy name so the static counter dictionary
    // doesn't bleed state between tests.
    private static int _policyCounter;
    private static string NextPolicy() =>
        $"test-policy-{System.Threading.Interlocked.Increment(ref _policyCounter)}";

    private static ActionExecutingContext MakeContext(string ip = "10.0.0.1")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ip);

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            filters: [],
            actionArguments: new Dictionary<string, object?>(),
            controller: new object());
    }

    private static Task NoopNext(ActionExecutingContext _) => Task.CompletedTask;

    private static ActionExecutionDelegate MakeDelegate() =>
        () => Task.FromResult(new ActionExecutedContext(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new ActionDescriptor()),
            filters: [],
            controller: new object()));

    // ── happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestWithinLimit_CallsNext()
    {
        var policy = NextPolicy();
        var filter = new RateLimitAttribute(policy, maxRequests: 5, windowSeconds: 60);
        var context = MakeContext();
        var nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return MakeDelegate()();
        };

        await filter.OnActionExecutionAsync(context, next);

        Assert.True(nextCalled);
        Assert.Null(context.Result); // no early-exit result set
    }

    // ── rate limit enforcement ─────────────────────────────────────────────────

    [Fact]
    public async Task RequestOverLimit_Returns429()
    {
        var policy = NextPolicy();
        var filter = new RateLimitAttribute(policy, maxRequests: 3, windowSeconds: 60);

        // Fire 3 requests (all allowed)
        for (var i = 0; i < 3; i++)
            await filter.OnActionExecutionAsync(MakeContext(), MakeDelegate());

        // 4th request should be rejected
        var over = MakeContext();
        await filter.OnActionExecutionAsync(over, MakeDelegate());

        var statusResult = Assert.IsType<StatusCodeResult>(over.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, statusResult.StatusCode);
    }

    [Fact]
    public async Task RequestOverLimit_SetsRetryAfterHeader()
    {
        var policy = NextPolicy();
        var filter = new RateLimitAttribute(policy, maxRequests: 1, windowSeconds: 60);

        await filter.OnActionExecutionAsync(MakeContext(), MakeDelegate());

        var over = MakeContext();
        await filter.OnActionExecutionAsync(over, MakeDelegate());

        Assert.True(over.HttpContext.Response.Headers.ContainsKey("Retry-After"));
    }

    // ── per-IP isolation ───────────────────────────────────────────────────────

    [Fact]
    public async Task DifferentIPs_TrackedIndependently()
    {
        var policy = NextPolicy();
        var filter = new RateLimitAttribute(policy, maxRequests: 1, windowSeconds: 60);

        // IP-A uses its 1 allowed request
        await filter.OnActionExecutionAsync(MakeContext("1.2.3.4"), MakeDelegate());

        // IP-B should still get through (fresh counter)
        var ipBContext = MakeContext("5.6.7.8");
        var ipBCalled = false;
        ActionExecutionDelegate ipBNext = () =>
        {
            ipBCalled = true;
            return MakeDelegate()();
        };
        await filter.OnActionExecutionAsync(ipBContext, ipBNext);

        Assert.True(ipBCalled);
        Assert.Null(ipBContext.Result);
    }

    // ── window reset ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterWindowExpires_CounterResets()
    {
        var policy = NextPolicy();
        // 1-second window so we can actually wait it out in a unit test
        var filter = new RateLimitAttribute(policy, maxRequests: 1, windowSeconds: 1);

        // Use the single allowed request
        await filter.OnActionExecutionAsync(MakeContext(), MakeDelegate());

        // Wait for the window to expire
        await Task.Delay(TimeSpan.FromSeconds(1.2));

        // Should be allowed again
        var afterReset = MakeContext();
        var called = false;
        ActionExecutionDelegate next = () =>
        {
            called = true;
            return MakeDelegate()();
        };
        await filter.OnActionExecutionAsync(afterReset, next);

        Assert.True(called);
        Assert.Null(afterReset.Result);
    }

    // ── stale-entry cleanup ────────────────────────────────────────────────────

    [Fact]
    public async Task Cleanup_RemovesEntriesOlderThanMaxStaleAge_KeepsFreshOnes()
    {
        var stalePolicy = NextPolicy();
        var freshPolicy = NextPolicy();

        var freshFilter = new RateLimitAttribute(freshPolicy, maxRequests: 5, windowSeconds: 60);
        await freshFilter.OnActionExecutionAsync(MakeContext("1.1.1.1"), MakeDelegate());

        var staleFilter = new RateLimitAttribute(stalePolicy, maxRequests: 5, windowSeconds: 60);
        await staleFilter.OnActionExecutionAsync(MakeContext("2.2.2.2"), MakeDelegate());

        // Rewrite the stale entry's WindowStart far in the past via reflection — the only way
        // to simulate staleness without an actual 10+ minute sleep in the test.
        var countersField = typeof(RateLimitAttribute).GetField("_counters", BindingFlags.NonPublic | BindingFlags.Static)!;
        var counters = (System.Collections.IDictionary)countersField.GetValue(null)!;
        var staleKey = $"{stalePolicy}:2.2.2.2";
        var freshKey = $"{freshPolicy}:1.1.1.1";
        var entryType = counters[staleKey]!.GetType();
        var staleEntry = Activator.CreateInstance(entryType)!;
        entryType.GetProperty("Count")!.SetValue(staleEntry, 1);
        entryType.GetProperty("WindowStart")!.SetValue(staleEntry, DateTimeOffset.UtcNow.AddMinutes(-20));
        counters[staleKey] = staleEntry;

        var cleanup = typeof(RateLimitAttribute).GetMethod("Cleanup", BindingFlags.NonPublic | BindingFlags.Static)!;
        cleanup.Invoke(null, [null]);

        Assert.False(counters.Contains(staleKey));
        Assert.True(counters.Contains(freshKey));
    }
}
