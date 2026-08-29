using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that dispatches by request path so a single test can serve
/// the whole OIDC round-trip (discovery, token, JWKS, userinfo) through one handler. Unlike
/// <see cref="MockHttpMessageHandler"/> - which caches one response whose content stream is
/// single-read - this builds a fresh <see cref="HttpResponseMessage"/> and <see cref="StringContent"/>
/// on every call, and records a per-route hit count.
/// </summary>
public sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    /// <summary>A route: requests whose absolute path ends with <see cref="PathSuffix"/> get this response.</summary>
    public sealed record Route(
        string PathSuffix, HttpStatusCode Status, Func<string> Body, string ContentType = "application/json");

    private readonly List<Route> _routes;
    private readonly ConcurrentDictionary<string, int> _hits = new(StringComparer.OrdinalIgnoreCase);

    public RoutingHttpMessageHandler(params Route[] routes) => _routes = routes.ToList();

    /// <summary>Well-known convenience: discovery, token, JWKS, and (optional) userinfo bodies.</summary>
    public static RoutingHttpMessageHandler ForFlow(
        Func<string> discovery, Func<string> token, Func<string> jwks, Func<string>? userInfo = null)
    {
        var routes = new List<Route>
        {
            new("/.well-known/openid-configuration", HttpStatusCode.OK, discovery),
            new("/token", HttpStatusCode.OK, token),
            new("/jwks", HttpStatusCode.OK, jwks),
        };
        if (userInfo != null)
        {
            routes.Add(new("/userinfo", HttpStatusCode.OK, userInfo));
        }

        return new RoutingHttpMessageHandler(routes.ToArray());
    }

    /// <summary>Number of requests routed to the route whose <c>PathSuffix</c> equals <paramref name="pathSuffix"/>.</summary>
    public int HitCount(string pathSuffix) => _hits.GetValueOrDefault(pathSuffix);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var route = _routes.FirstOrDefault(r => path.EndsWith(r.PathSuffix, StringComparison.OrdinalIgnoreCase));
        if (route is null)
        {
            throw new InvalidOperationException(
                $"RoutingHttpMessageHandler: no route matches {request.RequestUri}. "
                + $"Known suffixes: {string.Join(", ", _routes.Select(r => r.PathSuffix))}");
        }

        _hits.AddOrUpdate(route.PathSuffix, 1, (_, n) => n + 1);

        return Task.FromResult(new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body(), Encoding.UTF8, route.ContentType),
        });
    }
}
