using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// <summary>
/// HttpMessageHandler that throws instead of responding, used to simulate transport-level
/// failures (DNS failure, connection refused, TLS handshake failure) for IdentityModel calls.
/// </summary>
public sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw _exception;
}
