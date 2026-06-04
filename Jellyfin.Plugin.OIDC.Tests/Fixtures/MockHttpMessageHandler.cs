using System.Net;
using System.Net.Http;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// <summary>
/// Simple HttpMessageHandler that returns a pre-configured response,
/// used to mock IdentityModel HTTP calls (discovery doc, JWKS, token endpoint).
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content, string contentType = "application/json")
    {
        _response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, contentType)
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_response);
}
