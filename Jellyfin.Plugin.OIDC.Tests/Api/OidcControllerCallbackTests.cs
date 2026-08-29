using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Api;

/// <summary>
/// End-to-end characterization of <see cref="OidcController.Callback"/> driven through a fake IdP
/// (<see cref="RoutingHttpMessageHandler"/> + <see cref="OidcTestTokens"/>). Pins the observable
/// behavior - status codes, response bodies, and the minted session - of the current monolithic
/// method so the LoginFlow/ClaimsResolver extraction can be shown to preserve it: every assertion
/// here must keep passing unchanged through that refactor.
/// </summary>
[Xunit.Collection("OidcPlugin")]
public class OidcControllerCallbackTests
{
    private const string Authority = "https://203.0.113.10";
    private const string ClientId = "test-client";
    private const string ProviderId = "p1";
    private const string Nonce = "nonce-abc";
    private const string CsrfToken = "csrf-token-value";

    private readonly PluginTestFixture _fixture;

    public OidcControllerCallbackTests(PluginTestFixture fixture) => _fixture = fixture;

    // ── happy paths ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_HappyPath_RendersCallbackHtmlAndMintsSession()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["staff"]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            discovery: () => OidcTestTokens.DiscoveryJson(Authority),
            token: () => OidcTestTokens.TokenResponseJson(idToken),
            jwks: () => OidcTestTokens.JwksJson(key));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var content = Assert.IsType<ContentResult>(result);
        Assert.StartsWith("text/html", content.ContentType);
        Assert.Contains("Completing authentication", content.Content);
        Assert.Contains("/sso/OIDC/Auth/", content.Content);

        var session = stateManager.PeekAuthorizedSession(ExtractToken(content.Content!));
        Assert.NotNull(session);
        Assert.Equal("alice", session!.Username);
        Assert.Equal(["staff"], session.Roles);
        Assert.Equal("sub-123", session.Subject);
        Assert.Equal(Authority, session.Issuer);
        Assert.Equal("alice@example.com", session.Email);
        Assert.True(session.EmailVerified);
    }

    [Fact]
    public async Task Callback_QuickConnectState_RendersQuickConnectHtml()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["staff"]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => OidcTestTokens.TokenResponseJson(idToken),
            () => OidcTestTokens.JwksJson(key));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: true);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("id=\"code\"", content.Content);
        Assert.Contains("/sso/OIDC/QuickConnect/Authorize/", content.Content);
    }

    [Fact]
    public async Task Callback_RolesOnlyInValidatedAccessToken_ResolvesThem()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: null);
        var accessToken = OidcTestTokens.SignRs256(
            key, Authority, audience: "resource-api",
            claims: [new Claim("sub", "sub-123"), new Claim("groups", "staff")]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => OidcTestTokens.TokenResponseJson(idToken, accessToken),
            () => OidcTestTokens.JwksJson(key));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var content = Assert.IsType<ContentResult>(result);
        var session = stateManager.PeekAuthorizedSession(ExtractToken(content.Content!));
        Assert.Equal(["staff"], session!.Roles);
    }

    [Fact]
    public async Task Callback_RolesOnlyInUserInfo_ResolvesThem()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: null);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority, userInfoEndpoint: $"{Authority}/userinfo"),
            () => OidcTestTokens.TokenResponseJson(idToken),
            () => OidcTestTokens.JwksJson(key),
            userInfo: () => """{ "sub": "sub-123", "groups": ["staff"] }""");

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var content = Assert.IsType<ContentResult>(result);
        var session = stateManager.PeekAuthorizedSession(ExtractToken(content.Content!));
        Assert.Equal(["staff"], session!.Roles);
        Assert.Equal(1, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Callback_PictureOnlyInUserInfo_ResolvesIt()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["staff"]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority, userInfoEndpoint: $"{Authority}/userinfo"),
            () => OidcTestTokens.TokenResponseJson(idToken),
            () => OidcTestTokens.JwksJson(key),
            userInfo: () => """{ "sub": "sub-123", "picture": "https://cdn.example/a.png" }""");

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var content = Assert.IsType<ContentResult>(result);
        var session = stateManager.PeekAuthorizedSession(ExtractToken(content.Content!));
        Assert.Equal("https://cdn.example/a.png", session!.PictureUrl);
    }

    // ── rejections ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_MissingCode_ReturnsBadRequest()
    {
        var (controller, stateManager) = Build(RoutingHttpMessageHandler.ForFlow(
            () => "{}", () => "{}", () => "{}"), Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, code: "", state: stateKey);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_BadCsrfCookie_ReturnsBadRequest()
    {
        var (controller, stateManager) = Build(RoutingHttpMessageHandler.ForFlow(
            () => "{}", () => "{}", () => "{}"), Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false, csrfCookie: "wrong-value");

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "Could not verify this browser started the sign-in. Please retry from the same browser/tab.",
            bad.Value);
    }

    [Fact]
    public async Task Callback_NonceMismatch_ReturnsBadRequest()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, nonce: "different-nonce", groups: ["staff"]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => OidcTestTokens.TokenResponseJson(idToken),
            () => OidcTestTokens.JwksJson(key));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Token validation failed: nonce mismatch", bad.Value);
    }

    [Fact]
    public async Task Callback_IdTokenAuthorizedPartyIsDifferentClient_ReturnsBadRequest()
    {
        var key = OidcTestTokens.CreateSigningKey();
        // Valid signature + audience, but azp names a different client (OIDC Core §3.1.3.7 r5).
        var idToken = OidcTestTokens.SignRs256(key, Authority, ClientId, new[]
        {
            new Claim("sub", "sub-123"),
            new Claim("preferred_username", "alice"),
            new Claim("nonce", Nonce),
            new Claim("azp", "a-different-client"),
        });
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => OidcTestTokens.TokenResponseJson(idToken),
            () => OidcTestTokens.JwksJson(key));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Token validation failed", bad.Value);
    }

    [Fact]
    public async Task Callback_TokenEndpointError_ReturnsBadRequest()
    {
        var handler = new RoutingHttpMessageHandler(
            new("/.well-known/openid-configuration", System.Net.HttpStatusCode.OK, () => OidcTestTokens.DiscoveryJson(Authority)),
            new("/token", System.Net.HttpStatusCode.BadRequest, () => """{ "error": "invalid_grant" }"""),
            new("/jwks", System.Net.HttpStatusCode.OK, () => "{ \"keys\": [] }"));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Token exchange failed. Check plugin logs for details.", bad.Value);
    }

    [Fact]
    public async Task Callback_NoIdToken_ReturnsBadRequest()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => """{ "access_token": "opaque", "token_type": "Bearer" }""",
            () => OidcTestTokens.JwksJson(key));

        var (controller, stateManager) = Build(handler, Provider());
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "IdP returned no id_token. Ensure the 'openid' scope is configured for this client.",
            bad.Value);
    }

    [Fact]
    public async Task Callback_AdmissionDenied_ReturnsBadRequest()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["guest"]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => OidcTestTokens.TokenResponseJson(idToken),
            () => OidcTestTokens.JwksJson(key));

        var provider = Provider();
        var (controller, stateManager) = Build(
            handler, provider, cfg => cfg.AllowedGroups = ["staff"]);
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Your account is not permitted to sign in to this server.", bad.Value);
    }

    [Fact]
    public async Task Callback_StrictAccessTokenValidationWithUnverifiableToken_ReturnsBadRequest()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var otherKey = OidcTestTokens.CreateSigningKey("other-key");
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: null);
        // Access token is a JWT signed by a key NOT in the JWKS → signature validation fails.
        var accessToken = OidcTestTokens.SignRs256(
            otherKey, Authority, audience: "resource-api",
            claims: [new Claim("sub", "sub-123"), new Claim("groups", "staff")]);
        var handler = RoutingHttpMessageHandler.ForFlow(
            () => OidcTestTokens.DiscoveryJson(Authority),
            () => OidcTestTokens.TokenResponseJson(idToken, accessToken),
            () => OidcTestTokens.JwksJson(key));

        var provider = Provider();
        provider.StrictAccessTokenValidation = true;
        var (controller, stateManager) = Build(handler, provider);
        var stateKey = Seed(controller, stateManager, quickConnect: false);

        var result = await controller.Callback(ProviderId, "auth-code", stateKey);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Access token validation failed", bad.Value);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static OidcProviderConfig Provider() => new()
    {
        ProviderId = ProviderId,
        DisplayName = "P1",
        Authority = Authority,
        ClientId = ClientId,
        Enabled = true,
    };

    private (OidcController Controller, StateManager StateManager) Build(
        RoutingHttpMessageHandler handler, OidcProviderConfig provider, Action<PluginConfiguration>? tweak = null)
    {
        var config = new PluginConfiguration { Providers = [provider] };
        tweak?.Invoke(config);
        _fixture.SetConfiguration(config);

        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var userManager = Substitute.For<IUserManager>();
        var guardedHttp = TestHttp.GuardedFactory(handler);
        var rbacService = new RbacService(
            userManager, Substitute.For<ILibraryManager>(), Substitute.For<ILocalizationManager>(),
            NullLogger<RbacService>.Instance);
        var profileImageService = new ProfileImageService(
            guardedHttp, userManager, Substitute.For<IServerConfigurationManager>(),
            Substitute.For<IProviderManager>(), NullLogger<ProfileImageService>.Instance);
        var userSyncService = new UserSyncService(
            userManager, rbacService, profileImageService, _fixture.MapStore, NullLogger<UserSyncService>.Instance);
        var protocol = new OidcProtocolService(guardedHttp, NullLogger<OidcProtocolService>.Instance);
        var loginFlow = new LoginFlowService(
            protocol, new ClaimsResolver(protocol, NullLogger<ClaimsResolver>.Instance),
            stateManager, NullLogger<LoginFlowService>.Instance);
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        var appHost = Substitute.For<IServerApplicationHost>();
        appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://jellyfin.test");

        var controller = new OidcController(
            stateManager, userSyncService, Substitute.For<ISessionManager>(),
            quickConnect, protocol, loginFlow, appHost,
            NullLogger<OidcController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        return (controller, stateManager);
    }

    private static string Seed(
        OidcController controller, StateManager stateManager, bool quickConnect, string? csrfCookie = null)
    {
        var stateKey = stateManager.StoreState(new OidcState
        {
            ProviderId = ProviderId,
            Nonce = Nonce,
            CodeVerifier = "code-verifier",
            RedirectUri = $"{Authority}/sso/OIDC/Callback/{ProviderId}",
            CsrfToken = CsrfToken,
            QuickConnect = quickConnect,
        })!;

        controller.HttpContext.Request.Headers["Cookie"] = $"oidc_csrf.{stateKey}={csrfCookie ?? CsrfToken}";
        return stateKey;
    }

    private static string ExtractToken(string html)
    {
        var match = Regex.Match(html, "const token = \"([^\"]+)\"");
        Assert.True(match.Success, "callback HTML did not carry a session token");
        return match.Groups[1].Value;
    }
}
