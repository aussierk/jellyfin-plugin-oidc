using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Services;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// Builds a real <see cref="GuardedHttpClientFactory"/> whose pinned client routes through the
/// supplied handler. Tests use IP-literal URLs, so the guard always resolves a pinned address.
public static class TestHttp
{
    public static GuardedHttpClientFactory GuardedFactory(HttpMessageHandler? handler = null)
    {
        var h = handler ?? new UnexpectedCallHandler();
        return new GuardedHttpClientFactory((_, _) => new HttpClient(h));
    }

    private sealed class UnexpectedCallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No outbound HTTP call was expected in this test.");
    }
}
