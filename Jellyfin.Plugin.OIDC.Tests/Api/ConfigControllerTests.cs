using System.Net;
using System.Net.Http;
using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Api;

[Xunit.Collection("OidcPlugin")]
public class ConfigControllerTests
{
    // Authority values below deliberately use IP literals (never bare hostnames) so
    // AuthorityGuard's DNS-resolution path is never exercised against real DNS in tests —
    // it short-circuits on IPAddress.TryParse instead.

    private readonly PluginTestFixture _fixture;

    public ConfigControllerTests(PluginTestFixture fixture) => _fixture = fixture;

    private static string DiscoveryDocument(string authority) => $$"""
        {
            "issuer": "{{authority}}",
            "authorization_endpoint": "{{authority}}/authorize",
            "token_endpoint": "{{authority}}/token",
            "userinfo_endpoint": "{{authority}}/userinfo",
            "jwks_uri": "{{authority}}/jwks",
            "scopes_supported": ["openid", "profile", "email"]
        }
        """;

    private static ConfigController MakeController(HttpMessageHandler handler, ILocalizationManager? localization = null)
    {
        var userManager = Substitute.For<IUserManager>();
        var libraryManager = Substitute.For<ILibraryManager>();
        localization ??= Substitute.For<ILocalizationManager>();
        var rbacService = new RbacService(userManager, libraryManager, localization, NullLogger<RbacService>.Instance);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("OidcPlugin").Returns(new HttpClient(handler));

        // Tests use IP-literal Authorities, so AuthorityGuard always resolves a pinned address —
        // route the "pinned" path through the same mock handler instead of a real socket
        // connection, matching how the fallback ("OidcPlugin") path is already mocked above.
        HttpClient PinnedClientFactory(IPAddress _, bool __) => new(handler);

        return new ConfigController(
            rbacService, localization, httpClientFactory, PinnedClientFactory, NullLogger<ConfigController>.Instance);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_EmptyAuthority_ReturnsValidationMessage()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var controller = MakeController(new MockHttpMessageHandler(HttpStatusCode.OK, DiscoveryDocument("https://203.0.113.10")));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = " " });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Authority URL is required", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_HttpErrorFromDiscoveryEndpoint_ReturnsGenericMessage()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var controller = MakeController(new MockHttpMessageHandler(HttpStatusCode.NotFound, "not found", "text/plain"));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = "https://203.0.113.10" });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Unable to retrieve a discovery document", json);
        Assert.DoesNotContain("NotFound", json);
        Assert.DoesNotContain("404", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_TransportExceptionDuringDiscovery_ReturnsGenericMessage()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var controller = MakeController(new ThrowingHttpMessageHandler(new HttpRequestException("No route to host")));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = "https://10.0.40.253:9" });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("Unable to retrieve a discovery document", json);
        Assert.DoesNotContain("No route to host", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_DifferentFailureCauses_ReturnIdenticalErrorText()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var httpErrorController = MakeController(new MockHttpMessageHandler(HttpStatusCode.NotFound, "not found", "text/plain"));
        var transportErrorController = MakeController(new ThrowingHttpMessageHandler(new HttpRequestException("No route to host")));

        var httpErrorResult = await httpErrorController.TestProvider(new ProviderTestRequest { Authority = "https://203.0.113.10" });
        var transportErrorResult = await transportErrorController.TestProvider(new ProviderTestRequest { Authority = "https://10.0.40.253:9" });

        var httpErrorJson = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(httpErrorResult).Value);
        var transportErrorJson = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(transportErrorResult).Value);

        Assert.Equal(httpErrorJson, transportErrorJson);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_LoopbackAuthority_ReturnsBlockedMessageWithoutFetching()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        // Handler would throw if the discovery fetch were ever attempted — proves the guard
        // short-circuits before any network call, not just that the fetch happened to fail.
        var controller = MakeController(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = "https://127.0.0.1/realms/test" });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("loopback address", json);
        Assert.DoesNotContain("Unable to retrieve a discovery document", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_LinkLocalAuthority_ReturnsBlockedMessage()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var controller = MakeController(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = "https://169.254.169.254/" });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("link-local address", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_LoopbackAuthority_WithLoopbackOptOut_ProceedsToFetch()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://127.0.0.1";
        var controller = MakeController(new MockHttpMessageHandler(HttpStatusCode.OK, DiscoveryDocument(authority)));

        var result = await controller.TestProvider(new ProviderTestRequest
        {
            Authority = authority,
            AllowLoopbackAuthority = true
        });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"Success\":true", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_LinkLocalAuthority_WithOnlyLoopbackOptOut_StillBlocked()
    {
        // Opt-outs must be independent: allowing loopback must not also allow link-local.
        _fixture.SetConfiguration(new PluginConfiguration());
        var controller = MakeController(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        var result = await controller.TestProvider(new ProviderTestRequest
        {
            Authority = "https://169.254.169.254/",
            AllowLoopbackAuthority = true
        });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("link-local address", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_Rfc1918Authority_DefaultConfig_ProceedsToFetch()
    {
        // RFC1918 is deliberately not blocked by default — no opt-out required.
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://10.0.40.10";
        var controller = MakeController(new MockHttpMessageHandler(HttpStatusCode.OK, DiscoveryDocument(authority)));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = authority });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"Success\":true", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_Rfc1918Authority_WithGlobalBlockEnabled_ReturnsBlockedMessageWithoutFetching()
    {
        _fixture.SetConfiguration(new PluginConfiguration { BlockPrivateNetworkAuthorities = true });
        var controller = MakeController(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = "https://10.0.40.10/" });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("private-network address", json);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestProvider_SuccessfulDiscovery_ReturnsIssuerAndEndpoints()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        var controller = MakeController(new MockHttpMessageHandler(HttpStatusCode.OK, DiscoveryDocument(authority)));

        var result = await controller.TestProvider(new ProviderTestRequest { Authority = authority });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"Success\":true", json);
        Assert.Contains("https://203.0.113.10/authorize", json);
        Assert.Contains("https://203.0.113.10/jwks", json);
    }

    [Fact]
    public void GetRatings_ProjectsNameScoreSubScore_OrderedByScore_Deduped()
    {
        var localization = Substitute.For<MediaBrowser.Model.Globalization.ILocalizationManager>();
        localization.GetParentalRatings().Returns(new List<MediaBrowser.Model.Entities.ParentalRating>
        {
            new("PG-13", new MediaBrowser.Model.Entities.ParentalRatingScore(9, null)),
            new("G", new MediaBrowser.Model.Entities.ParentalRatingScore(1, null)),
            new("pg-13", new MediaBrowser.Model.Entities.ParentalRatingScore(9, null)), // dup by name
            new(string.Empty, new MediaBrowser.Model.Entities.ParentalRatingScore(0, null)), // dropped
        });

        var controller = MakeController(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"), localization);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.GetRatings());
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);

        Assert.Equal("[{\"Name\":\"G\",\"Score\":1,\"SubScore\":null},{\"Name\":\"PG-13\",\"Score\":9,\"SubScore\":null}]", json);
    }
}
