using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using System.Net;
using System.Net.Http;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Api;

[Xunit.Collection("OidcPlugin")]
public class OidcControllerTests
{
    private readonly PluginTestFixture _fixture;

    public OidcControllerTests(PluginTestFixture fixture) => _fixture = fixture;

    private OidcController MakeController(
        IServerApplicationHost? appHost = null,
        StateManager? stateManager = null,
        IUserManager? userManager = null,
        IQuickConnect? quickConnect = null,
        HttpMessageHandler? httpHandler = null,
        Microsoft.Extensions.Logging.ILogger<OidcController>? logger = null)
    {
        stateManager ??= new StateManager(NullLogger<StateManager>.Instance);
        userManager ??= Substitute.For<IUserManager>();
        var libraryManager = Substitute.For<ILibraryManager>();
        var rbacService = new RbacService(
            userManager, libraryManager, Substitute.For<ILocalizationManager>(), NullLogger<RbacService>.Instance);

        // One factory for both the controller and the (unused here - sessions carry a null
        // PictureUrl) profile-image path; routes every client through the passed handler.
        var guardedHttp = TestHttp.GuardedFactory(httpHandler);

        var profileImageService = new ProfileImageService(
            guardedHttp,
            userManager,
            Substitute.For<IServerConfigurationManager>(),
            Substitute.For<IProviderManager>(),
            NullLogger<ProfileImageService>.Instance);
        var userSyncService = new UserSyncService(userManager, rbacService, profileImageService, _fixture.MapStore, NullLogger<UserSyncService>.Instance);
        var protocol = new OidcProtocolService(guardedHttp, NullLogger<OidcProtocolService>.Instance);
        var loginFlow = new LoginFlowService(
            protocol, new ClaimsResolver(protocol, NullLogger<ClaimsResolver>.Instance),
            stateManager, NullLogger<LoginFlowService>.Instance);
        var sessionManager = Substitute.For<ISessionManager>();
        if (quickConnect == null)
        {
            quickConnect = Substitute.For<IQuickConnect>();
            quickConnect.IsEnabled.Returns(true);
        }

        if (appHost == null)
        {
            appHost = Substitute.For<IServerApplicationHost>();
            appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://jellyfin.test");
        }

        var controller = new OidcController(
            stateManager, userSyncService, sessionManager, quickConnect,
            protocol, loginFlow, appHost, logger ?? NullLogger<OidcController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    // ── GetProviders ───────────────────────────────────────────────────────────

    [Fact]
    public void GetProviders_NoEnabledProviders_ReturnsEmptyList()
    {
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        var result = MakeController().GetProviders();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("[]", System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void GetProviders_OneEnabledProvider_StartUrlContainsSmartApiUrl()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }
            ]
        });
        var appHost = Substitute.For<IServerApplicationHost>();
        appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://jellyfin.local");

        var result = MakeController(appHost).GetProviders();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains(
            "https://jellyfin.local/sso/OIDC/Start/keycloak",
            System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void GetProviders_PathBaseSet_StartUrlIncludesPathBase()
    {
        // Jellyfin running under a reverse-proxy base path (Networking > Base URL).
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }
            ]
        });
        var appHost = Substitute.For<IServerApplicationHost>();
        appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://jellyfin.local");
        var controller = MakeController(appHost);
        controller.HttpContext.Request.PathBase = new PathString("/jellyfin");

        var result = controller.GetProviders();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains(
            "https://jellyfin.local/jellyfin/sso/OIDC/Start/keycloak",
            System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void GetProviders_DisabledProvider_NotIncluded()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "active", DisplayName = "Active", Enabled = true },
                new OidcProviderConfig { ProviderId = "inactive", DisplayName = "Inactive", Enabled = false }
            ]
        });

        var result = MakeController().GetProviders();

        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Contains("active", json);
        Assert.DoesNotContain("inactive", json);
    }

    // Private helpers below are tested directly - the PKCE/routing logic they hold is only
    // reachable through Start/Callback, which need a live IdP round-trip.

    // ── CreateCodeChallenge ────────────────────────────────────────────────────

    [Fact]
    public void CreateCodeChallenge_OutputIsBase64UrlSha256OfVerifier()
    {
        var method = typeof(OidcController).GetMethod(
            "CreateCodeChallenge",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        const string verifier = "my-test-code-verifier";

        var result = (string)method.Invoke(null, [verifier])!;

        using var sha256 = SHA256.Create();
        var expected = Base64UrlEncoder.Encode(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        Assert.Equal(expected, result);
    }

    // Admission-gate coverage moved to Services/AdmissionServiceTests.cs (F0 extracted
    // CheckAdmission to the public AdmissionService.Evaluate).

    // ── LogDiscoveryPreflightWarnings (non-blocking sanity check before the authorize redirect) ──

    private static async Task<DiscoveryDocumentResponse> PreflightDiscoAsync(string? extraFields)
    {
        const string authority = "https://203.0.113.10";
        var extra = string.IsNullOrEmpty(extraFields) ? string.Empty : ",\n    " + extraFields;
        var doc = $$"""
            {
                "issuer": "{{authority}}",
                "authorization_endpoint": "{{authority}}/authorize",
                "token_endpoint": "{{authority}}/token",
                "jwks_uri": "{{authority}}/jwks"{{extra}}
            }
            """;
        using var http = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, doc));
        return await http.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = authority,
            Policy = new DiscoveryPolicy { ValidateIssuerName = true, ValidateEndpoints = false }
        });
    }

    private static void InvokePreflight(OidcController controller, DiscoveryDocumentResponse disco, string scopes)
        => typeof(OidcController)
            .GetMethod("LogDiscoveryPreflightWarnings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, [disco, new OidcProviderConfig { Scopes = scopes }, "keycloak"]);

    [Fact]
    public async Task Preflight_MethodsAdvertisedWithoutS256_LogsWarning()
    {
        var logger = new CollectingLogger<OidcController>();
        InvokePreflight(
            MakeController(logger: logger),
            await PreflightDiscoAsync("\"code_challenge_methods_supported\": [\"plain\"]"),
            "openid");
        Assert.True(logger.Any(LogLevel.Warning, "S256"));
    }

    [Fact]
    public async Task Preflight_S256Advertised_NoWarning()
    {
        var logger = new CollectingLogger<OidcController>();
        InvokePreflight(
            MakeController(logger: logger),
            await PreflightDiscoAsync("\"code_challenge_methods_supported\": [\"S256\",\"plain\"], \"scopes_supported\": [\"openid\"]"),
            "openid");
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Preflight_NoMethodSetAdvertised_NoWarning()
    {
        var logger = new CollectingLogger<OidcController>();
        InvokePreflight(MakeController(logger: logger), await PreflightDiscoAsync(null), "openid profile");
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Preflight_RequestedScopeNotAdvertised_LogsWarning()
    {
        var logger = new CollectingLogger<OidcController>();
        InvokePreflight(
            MakeController(logger: logger),
            await PreflightDiscoAsync("\"code_challenge_methods_supported\": [\"S256\"], \"scopes_supported\": [\"openid\",\"profile\"]"),
            "openid profile email");
        Assert.True(logger.Any(LogLevel.Warning, "email"));
    }

    [Fact]
    public async Task Start_DiscoLacksS256_StillRedirects()
    {
        const string authority = "https://203.0.113.10";
        var discoveryJson = $$"""
            {
                "issuer": "{{authority}}",
                "authorization_endpoint": "{{authority}}/authorize",
                "token_endpoint": "{{authority}}/token",
                "jwks_uri": "{{authority}}/jwks",
                "code_challenge_methods_supported": ["plain"]
            }
            """;
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", Authority = authority, ClientId = "client", Enabled = true }
            ]
        });
        var controller = MakeController(httpHandler: new MockHttpMessageHandler(HttpStatusCode.OK, discoveryJson));

        var result = await controller.Start("keycloak");

        Assert.IsType<RedirectResult>(result);
    }

    // ── ValidateOrPinEndpoints (TOFU pinning of discovery endpoints) ───────────
    // Uses a real DiscoveryDocumentResponse from a mocked HTTP handler, not a hand-built stand-in.

    private static async Task<DiscoveryDocumentResponse> MakeDiscoAsync(
        string authority, string? userInfoEndpoint = null, string? authorizeEndpoint = null)
    {
        var userInfoLine = userInfoEndpoint != null
            ? $"""
                   ,"userinfo_endpoint": "{userInfoEndpoint}"
               """
            : string.Empty;
        var doc = $$"""
            {
                "issuer": "{{authority}}",
                "authorization_endpoint": "{{authorizeEndpoint ?? $"{authority}/authorize"}}",
                "token_endpoint": "{{authority}}/token",
                "jwks_uri": "{{authority}}/jwks"{{userInfoLine}}
            }
            """;
        using var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, doc));
        return await httpClient.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = authority,
            Policy = new DiscoveryPolicy { ValidateIssuerName = true, ValidateEndpoints = false }
        });
    }

    // ── ParseAdditionalParameters ──────────────────────────────────────────────

    private static readonly MethodInfo _parseAdditional =
        typeof(OidcController).GetMethod(
            "ParseAdditionalParameters",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private object? ParseAdditional(string? raw)
        => _parseAdditional.Invoke(MakeController(), [raw, "keycloak"]);

    [Fact]
    public void ParseAdditionalParameters_Null_ReturnsNull()
    {
        Assert.Null(ParseAdditional(null));
    }

    [Fact]
    public void ParseAdditionalParameters_EmptyString_ReturnsNull()
    {
        Assert.Null(ParseAdditional(""));
    }

    [Fact]
    public void ParseAdditionalParameters_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(ParseAdditional("   "));
    }

    [Fact]
    public void ParseAdditionalParameters_ValidPairs_ReturnsCorrectKeyValues()
    {
        var result = ParseAdditional("prompt=consent&ui_locales=en");

        Assert.NotNull(result);
        var dict = ((IEnumerable<KeyValuePair<string, string>>)result!).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("consent", dict["prompt"]);
        Assert.Equal("en", dict["ui_locales"]);
    }

    [Fact]
    public void ParseAdditionalParameters_UrlEncodedValues_AreDecoded()
    {
        var result = ParseAdditional("acr_values=urn%3Amace%3Aincommon");

        Assert.NotNull(result);
        Assert.Equal("urn:mace:incommon",
            ((IEnumerable<KeyValuePair<string, string>>)result!).First().Value);
    }

    [Fact]
    public void ParseAdditionalParameters_MalformedEntry_IsSkippedButValidOnesKept()
    {
        var result = ParseAdditional("prompt=consent&badentry&=novalue");

        Assert.NotNull(result);
        var pairs = ((IEnumerable<KeyValuePair<string, string>>)result!).ToList();
        Assert.Single(pairs);
        Assert.Equal("prompt", pairs[0].Key);
        Assert.Equal("consent", pairs[0].Value);
    }

    [Fact]
    public void ParseAdditionalParameters_AllEntriesMalformed_ReturnsNull()
    {
        Assert.Null(ParseAdditional("badentry&another&=x"));
    }

    [Fact]
    public void ParseAdditionalParameters_ExplicitEmptyValue_IsKept()
    {
        var result = ParseAdditional("prompt=");

        Assert.NotNull(result);
        var pairs = ((IEnumerable<KeyValuePair<string, string>>)result!).ToList();
        Assert.Single(pairs);
        Assert.Equal("prompt", pairs[0].Key);
        Assert.Equal(string.Empty, pairs[0].Value);
    }

    // ── BuildCsrfCookieName ────────────────────────────────────────────────────

    private static readonly MethodInfo _buildCsrfCookieName =
        typeof(OidcController).GetMethod(
            "BuildCsrfCookieName",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildCsrfCookieName_ReturnsPrefixedStateKey()
    {
        var result = (string)_buildCsrfCookieName.Invoke(null, ["abc123"])!;

        Assert.Equal("oidc_csrf.abc123", result);
    }

    // ── VerifyCsrfToken ────────────────────────────────────────────────────────

    private static readonly MethodInfo _verifyCsrfToken =
        typeof(OidcController).GetMethod(
            "VerifyCsrfToken",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void VerifyCsrfToken_ExactMatch_ReturnsTrue()
    {
        var result = (bool)_verifyCsrfToken.Invoke(null, ["same-token", "same-token"])!;

        Assert.True(result);
    }

    [Fact]
    public void VerifyCsrfToken_Mismatch_ReturnsFalse()
    {
        var result = (bool)_verifyCsrfToken.Invoke(null, ["cookie-token", "expected-token"])!;

        Assert.False(result);
    }

    [Fact]
    public void VerifyCsrfToken_NullCookie_ReturnsFalse()
    {
        var result = (bool)_verifyCsrfToken.Invoke(null, [null, "expected-token"])!;

        Assert.False(result);
    }

    [Fact]
    public void VerifyCsrfToken_EmptyCookie_ReturnsFalse()
    {
        var result = (bool)_verifyCsrfToken.Invoke(null, ["", "expected-token"])!;

        Assert.False(result);
    }

    [Fact]
    public void VerifyCsrfToken_DifferentLength_ReturnsFalse()
    {
        var result = (bool)_verifyCsrfToken.Invoke(null, ["short", "much-longer-expected-token"])!;

        Assert.False(result);
    }

    // ── BuildRedirectUri ───────────────────────────────────────────────────────

    private static readonly MethodInfo _buildRedirectUri =
        typeof(OidcController).GetMethod(
            "BuildRedirectUri",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void BuildRedirectUri_ServerBaseUrlSet_UsesServerBaseUrl()
    {
        var controller = MakeController();
        var provider = new OidcProviderConfig { ProviderId = "kc", ServerBaseUrl = "https://custom.server" };

        var result = (string)_buildRedirectUri.Invoke(controller, [provider])!;

        Assert.Equal("https://custom.server/sso/OIDC/Callback/kc", result);
    }

    [Fact]
    public void BuildRedirectUri_ServerBaseUrlNotSet_UsesSmartApiUrl()
    {
        var appHost = Substitute.For<IServerApplicationHost>();
        appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://auto.detected/");
        var controller = MakeController(appHost);
        var provider = new OidcProviderConfig { ProviderId = "kc", ServerBaseUrl = "" };

        var result = (string)_buildRedirectUri.Invoke(controller, [provider])!;

        Assert.Equal("https://auto.detected/sso/OIDC/Callback/kc", result);
    }

    // ── BuildCallbackHtml ──────────────────────────────────────────────────────

    private static readonly MethodInfo _buildCallbackHtml =
        typeof(OidcController).GetMethod(
            "BuildCallbackHtml",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildCallbackHtml_DerivesBasePathFromCallbackUrl_NotHardcodedRootRelative()
    {
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", "keycloak", "nonce123"])!;

        // Base path is derived client-side from the callback URL, not hardcoded root-relative.
        Assert.Contains(
            "window.location.pathname.replace(/\\/sso\\/OIDC\\/Callback\\/[^/]+\\/?$/i, '')",
            html);
        Assert.Contains("fetch(basePath + '/sso/OIDC/Auth/' + providerId", html);
        Assert.Contains("ManualAddress: window.location.origin + basePath", html);
        Assert.Contains("window.location.href = basePath + '/'", html);
    }

    [Fact]
    public void BuildCallbackHtml_TokenAndProviderId_AreJsonEncoded()
    {
        // A provider ID with a single quote must not break out of the JS string literal.
        const string maliciousProviderId = "kc'; alert(1); //";

        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", maliciousProviderId, "nonce123"])!;

        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(maliciousProviderId), html);
        Assert.DoesNotContain("const providerId = 'kc'", html);
    }

    [Fact]
    public void BuildCallbackHtml_IncludesNonceOnScriptAndStyleTags()
    {
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", "keycloak", "abc123=="])!;

        Assert.Contains("<script nonce=\"abc123==\">", html);
        Assert.Contains("<style nonce=\"abc123==\">", html);
    }

    [Fact]
    public void BuildCallbackHtml_StatusElementIdIsPreserved()
    {
        // The inline script depends on the 'status' id surviving markup redesigns.
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", "keycloak", "abc123=="])!;

        Assert.Contains("id=\"status\"", html);
    }

    [Fact]
    public void BuildCallbackHtml_UsesJellyfinWebLocalStorageKeysAndAppVersion()
    {
        // These mirror jellyfin-web internals (see the constants' doc comment in OidcController) -
        // pinned here so a Jellyfin bump that changes them fails a test instead of silently breaking login.
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", "keycloak", "abc123=="])!;

        Assert.Contains("\"_deviceId2\"", html);
        Assert.Contains("\"jellyfin_credentials\"", html);
        Assert.Contains("\"Jellyfin Web\"", html);
        Assert.Contains("\"12.0.0\"", html);
    }

    // ── BuildQuickConnectHtml ──────────────────────────────────────────────────

    private static readonly MethodInfo _buildQuickConnectHtml =
        typeof(OidcController).GetMethod(
            "BuildQuickConnectHtml",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildQuickConnectHtml_ContainsCodeEntryFormAndBasePathPrefixedAuthorizeUrl()
    {
        var html = (string)_buildQuickConnectHtml.Invoke(null, ["token123", "keycloak", "nonce123"])!;

        Assert.Contains("id=\"code\"", html);
        Assert.Contains(
            "window.location.pathname.replace(/\\/sso\\/OIDC\\/Callback\\/[^/]+\\/?$/i, '')",
            html);
        Assert.Contains(
            "fetch(basePath + '/sso/OIDC/QuickConnect/Authorize/' + encodeURIComponent(providerId)",
            html);
    }

    [Fact]
    public void BuildQuickConnectHtml_TokenAndProviderId_AreJsonEncoded()
    {
        const string maliciousProviderId = "kc'; alert(1); //";

        var html = (string)_buildQuickConnectHtml.Invoke(null, ["token123", maliciousProviderId, "nonce123"])!;

        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(maliciousProviderId), html);
        Assert.DoesNotContain("const providerId = 'kc'", html);
    }

    [Fact]
    public void BuildQuickConnectHtml_IncludesNonceOnScriptAndStyleTags()
    {
        var html = (string)_buildQuickConnectHtml.Invoke(null, ["token123", "keycloak", "abc123=="])!;

        Assert.Contains("<script nonce=\"abc123==\">", html);
        Assert.Contains("<style nonce=\"abc123==\">", html);
    }

    // ── Authenticate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Authenticate_ProviderDisabled_ReturnsNotFound()
    {
        // Provider existed when authorized but was disabled afterward.
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = false }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        var result = await controller.Authenticate("keycloak", new AuthenticateRequest { Token = token! });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Authenticate_ProviderRemoved_ReturnsNotFound()
    {
        // No provider configured at all (e.g. removed from the admin UI mid-flow).
        _fixture.SetConfiguration(new PluginConfiguration());
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        var result = await controller.Authenticate("keycloak", new AuthenticateRequest { Token = token! });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── QuickConnectAuthorize ──────────────────────────────────────────────────

    private static AuthorizedSession MakeQcSession(
        string username = "alice", string providerId = "keycloak", string? pictureUrl = null) => new()
    {
        ProviderId = providerId,
        Username = username,
        DisplayName = username,
        PictureUrl = pictureUrl,
        Roles = []
    };

    private const string OidcAuthProviderId = "Jellyfin.Plugin.OIDC.Auth.OidcAuthProvider";

    // Existing OIDC user, already registered to `providerId` - the "happy path" identity that
    // lets SyncUserAsync succeed without needing to stub user creation.
    private static (IUserManager UserManager, User User) MakeSyncableUser(string username, string providerId)
    {
        var user = new User(username, OidcAuthProviderId, "PasswordResetProviderId");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName(username).Returns(user);
        return (userManager, user);
    }

    private void ConfigureForSyncableUser(string username, string providerId) =>
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = username, ProviderId = providerId }],
            Providers = [new OidcProviderConfig { ProviderId = providerId, Enabled = true }]
        });

    [Fact]
    public async Task QuickConnectAuthorize_InvalidToken_ReturnsUnauthorized()
    {
        var controller = MakeController();

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = "does-not-exist", Code = "123456" });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_ProviderMismatch_ReturnsBadRequest()
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        // Session was authorized for "keycloak" but Authorize is called for "okta".
        var result = await controller.QuickConnectAuthorize(
            "okta", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_ProviderDisabled_ReturnsNotFound()
    {
        // Provider existed when authorized but was disabled afterward.
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = false }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_QuickConnectDisabled_ReturnsBadRequest()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = true }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession());
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(false);
        var controller = MakeController(stateManager: stateManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
        // The session must remain valid - the admin might enable Quick Connect and the user retries.
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_MissingCode_ReturnsBadRequest()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = true }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession());
        var controller = MakeController(stateManager: stateManager);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "   " });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_UserSyncFails_ReturnsForbid()
    {
        // User does not exist and auto-creation is disabled, so SyncUserAsync throws.
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "unknown-user"));
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("unknown-user").Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = false,
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = true }]
        });
        var controller = MakeController(stateManager: stateManager, userManager: userManager);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_Success_ReturnsOkAndInvalidatesSession()
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), "123456").Returns(Task.FromResult(true));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.IsType<OkObjectResult>(result);
        // The one-time session must be invalidated so the token can't be replayed.
        Assert.Null(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_CodeTrimmed_StillMatches()
    {
        // A code pasted with surrounding whitespace must still authorize.
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), "123456").Returns(Task.FromResult(true));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "  123456  " });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_AuthorizationRejected_ReturnsBadRequestAndKeepsSessionValid()
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>()).Returns(Task.FromResult(false));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "999999" });

        Assert.IsType<BadRequestObjectResult>(result);
        // A retry with the correct code should still be possible.
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_RetryAfterBadCode_DoesNotReProvisionTheUser()
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), "999999").Returns(Task.FromResult(false));
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), "123456").Returns(Task.FromResult(true));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var rejected = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "999999" });
        var accepted = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.IsType<BadRequestObjectResult>(rejected);
        Assert.IsType<OkObjectResult>(accepted);
        // SyncUserAsync resolves the user via GetUserByName exactly once - the retry reused the
        // cached SyncedUserId instead of re-provisioning + re-applying RBAC.
        userManager.Received(1).GetUserByName("alice");
    }

    [Fact]
    public async Task QuickConnectAuthorize_UnknownCode_ReturnsBadRequestAndKeepsSessionValid()
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns<bool>(_ => throw new ResourceNotFoundException());
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "000000" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_QuickConnectNotActive_ReturnsBadRequest()
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns<bool>(_ => throw new AuthenticationException());
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "000000" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_UserSyncThrowsUnexpected_Returns500AndKeepsSessionValid()
    {
        // Anything other than InvalidOperationException from the sync path (DB timeout, transient
        // Jellyfin API error) must land on the catch-all, not propagate unhandled.
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns<User?>(_ => throw new TimeoutException("db timeout"));
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = true }]
        });
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_AuthorizeThrowsUnexpected_Returns500AndKeepsSessionValid()
    {
        // Safety net for when the by-name exception filters go stale against a future core version.
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns<bool>(_ => throw new TimeoutException("quick connect backend timeout"));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "000000" });

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }
}
