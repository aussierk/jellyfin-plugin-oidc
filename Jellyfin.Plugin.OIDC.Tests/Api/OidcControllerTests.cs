using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using IdentityModel;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using System.Net;
using System.Net.Http;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        IQuickConnect? quickConnect = null)
    {
        stateManager ??= new StateManager(NullLogger<StateManager>.Instance);
        userManager ??= Substitute.For<IUserManager>();
        var libraryManager = Substitute.For<ILibraryManager>();
        var rbacService = new RbacService(userManager, libraryManager, NullLogger<RbacService>.Instance);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        // Not exercised by these tests — every session here has a null PictureUrl, so
        // ApplyProfileImageAsync returns before touching any HTTP client.
        HttpClient ProfileImagePinnedClientFactory(IPAddress address, bool allowAutoRedirect) => httpClientFactory.CreateClient("OidcPluginImage");

        var profileImageService = new ProfileImageService(
            httpClientFactory,
            ProfileImagePinnedClientFactory,
            userManager,
            Substitute.For<IServerConfigurationManager>(),
            Substitute.For<IProviderManager>(),
            NullLogger<ProfileImageService>.Instance);
        var userSyncService = new UserSyncService(userManager, rbacService, profileImageService, NullLogger<UserSyncService>.Instance);
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

        // Not exercised by the tests below (no Start/Callback coverage yet), but required to
        // construct the controller — points at the same substitute factory's client so it stays
        // consistent with the "OidcPlugin" mock if a test starts exercising the discovery path.
        HttpClient PinnedClientFactory(IPAddress address, bool allowAutoRedirect) => httpClientFactory.CreateClient("OidcPlugin");

        var controller = new OidcController(
            stateManager, userSyncService, sessionManager, quickConnect,
            httpClientFactory, PinnedClientFactory, appHost, NullLogger<OidcController>.Instance);

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
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        // Act
        var result = MakeController().GetProviders();

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("[]", System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void GetProviders_OneEnabledProvider_StartUrlContainsSmartApiUrl()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }
            ]
        });
        var appHost = Substitute.For<IServerApplicationHost>();
        appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://jellyfin.local");

        // Act
        var result = MakeController(appHost).GetProviders();

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains(
            "https://jellyfin.local/sso/OIDC/Start/keycloak",
            System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void GetProviders_PathBaseSet_StartUrlIncludesPathBase()
    {
        // Arrange — Jellyfin running under a reverse-proxy base path (Networking > Base URL).
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

        // Act
        var result = controller.GetProviders();

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains(
            "https://jellyfin.local/jellyfin/sso/OIDC/Start/keycloak",
            System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void GetProviders_DisabledProvider_NotIncluded()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "active", DisplayName = "Active", Enabled = true },
                new OidcProviderConfig { ProviderId = "inactive", DisplayName = "Inactive", Enabled = false }
            ]
        });

        // Act
        var result = MakeController().GetProviders();

        // Assert
        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Contains("active", json);
        Assert.DoesNotContain("inactive", json);
    }

    // ── Private helpers — deliberate exceptions to "test behavior not implementation"
    // These methods contain security-critical PKCE and routing logic that is only
    // reachable through the Start/Callback endpoints, which require live IdP HTTP calls.
    // Testing them directly is preferable to leaving them untested.

    // ── CreateCodeChallenge ────────────────────────────────────────────────────

    [Fact]
    public void CreateCodeChallenge_OutputIsBase64UrlSha256OfVerifier()
    {
        // Arrange
        var method = typeof(OidcController).GetMethod(
            "CreateCodeChallenge",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        const string verifier = "my-test-code-verifier";

        // Act
        var result = (string)method.Invoke(null, [verifier])!;

        // Assert
        using var sha256 = SHA256.Create();
        var expected = Base64UrlEncoder.Encode(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        Assert.Equal(expected, result);
    }

    // ── ParseAdditionalParameters ──────────────────────────────────────────────

    private static readonly MethodInfo _parseAdditional =
        typeof(OidcController).GetMethod(
            "ParseAdditionalParameters",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void ParseAdditionalParameters_Null_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(_parseAdditional.Invoke(null, [null]));
    }

    [Fact]
    public void ParseAdditionalParameters_EmptyString_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(_parseAdditional.Invoke(null, [""]));
    }

    [Fact]
    public void ParseAdditionalParameters_WhitespaceOnly_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(_parseAdditional.Invoke(null, ["   "]));
    }

    [Fact]
    public void ParseAdditionalParameters_ValidPairs_ReturnsCorrectKeyValues()
    {
        // Act
        var result = _parseAdditional.Invoke(null, ["prompt=consent&ui_locales=en"]);

        // Assert
        Assert.NotNull(result);
        var dict = ((IEnumerable<KeyValuePair<string, string>>)result!).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("consent", dict["prompt"]);
        Assert.Equal("en", dict["ui_locales"]);
    }

    [Fact]
    public void ParseAdditionalParameters_UrlEncodedValues_AreDecoded()
    {
        // Act
        var result = _parseAdditional.Invoke(null, ["acr_values=urn%3Amace%3Aincommon"]);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("urn:mace:incommon",
            ((IEnumerable<KeyValuePair<string, string>>)result!).First().Value);
    }

    [Fact]
    public void ParseAdditionalParameters_MalformedEntry_IsSkipped()
    {
        // Act
        var result = _parseAdditional.Invoke(null, ["prompt=consent&badentry"]);

        // Assert
        Assert.NotNull(result);
        var pairs = ((IEnumerable<KeyValuePair<string, string>>)result!).ToList();
        Assert.Single(pairs);
        Assert.Equal("prompt", pairs[0].Key);
        Assert.Equal("consent", pairs[0].Value);
    }

    // ── BuildCsrfCookieName ────────────────────────────────────────────────────

    private static readonly MethodInfo _buildCsrfCookieName =
        typeof(OidcController).GetMethod(
            "BuildCsrfCookieName",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildCsrfCookieName_ReturnsPrefixedStateKey()
    {
        // Act
        var result = (string)_buildCsrfCookieName.Invoke(null, ["abc123"])!;

        // Assert
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
        // Act
        var result = (bool)_verifyCsrfToken.Invoke(null, ["same-token", "same-token"])!;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyCsrfToken_Mismatch_ReturnsFalse()
    {
        // Act
        var result = (bool)_verifyCsrfToken.Invoke(null, ["cookie-token", "expected-token"])!;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyCsrfToken_NullCookie_ReturnsFalse()
    {
        // Act
        var result = (bool)_verifyCsrfToken.Invoke(null, [null, "expected-token"])!;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyCsrfToken_EmptyCookie_ReturnsFalse()
    {
        // Act
        var result = (bool)_verifyCsrfToken.Invoke(null, ["", "expected-token"])!;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyCsrfToken_DifferentLength_ReturnsFalse()
    {
        // Act
        var result = (bool)_verifyCsrfToken.Invoke(null, ["short", "much-longer-expected-token"])!;

        // Assert
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
        // Arrange
        var controller = MakeController();
        var provider = new OidcProviderConfig { ProviderId = "kc", ServerBaseUrl = "https://custom.server" };

        // Act
        var result = (string)_buildRedirectUri.Invoke(controller, [provider])!;

        // Assert
        Assert.Equal("https://custom.server/sso/OIDC/Callback/kc", result);
    }

    [Fact]
    public void BuildRedirectUri_ServerBaseUrlNotSet_UsesSmartApiUrl()
    {
        // Arrange
        var appHost = Substitute.For<IServerApplicationHost>();
        appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://auto.detected/");
        var controller = MakeController(appHost);
        var provider = new OidcProviderConfig { ProviderId = "kc", ServerBaseUrl = "" };

        // Act
        var result = (string)_buildRedirectUri.Invoke(controller, [provider])!;

        // Assert
        Assert.Equal("https://auto.detected/sso/OIDC/Callback/kc", result);
    }

    // ── BuildCallbackHtml ──────────────────────────────────────────────────────
    // Private static — reachable only via a full Callback round-trip that requires live IdP
    // HTTP calls, so it's tested directly (same rationale as the helpers above).

    private static readonly MethodInfo _buildCallbackHtml =
        typeof(OidcController).GetMethod(
            "BuildCallbackHtml",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildCallbackHtml_DerivesBasePathFromCallbackUrl_NotHardcodedRootRelative()
    {
        // Act
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", "keycloak", "nonce123"])!;

        // Assert — base path is derived client-side from the callback URL...
        Assert.Contains(
            "window.location.pathname.replace(/\\/sso\\/OIDC\\/Callback\\/[^/]+\\/?$/i, '')",
            html);
        // ... and used to prefix the auth fetch, the stored server address, and the redirect —
        // none of these may be hardcoded root-relative (the base-URL bug this fix addresses).
        Assert.Contains("fetch(basePath + '/sso/OIDC/Auth/' + providerId", html);
        Assert.Contains("ManualAddress: window.location.origin + basePath", html);
        Assert.Contains("window.location.href = basePath + '/'", html);
    }

    [Fact]
    public void BuildCallbackHtml_TokenAndProviderId_AreJsonEncoded()
    {
        // Arrange — a provider ID with a single quote must not break out of the JS string literal.
        const string maliciousProviderId = "kc'; alert(1); //";

        // Act
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", maliciousProviderId, "nonce123"])!;

        // Assert
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(maliciousProviderId), html);
        Assert.DoesNotContain("const providerId = 'kc'", html);
    }

    [Fact]
    public void BuildCallbackHtml_IncludesNonceOnScriptTag()
    {
        // Act
        var html = (string)_buildCallbackHtml.Invoke(null, ["token123", "keycloak", "abc123=="])!;

        // Assert
        Assert.Contains("<script nonce=\"abc123==\">", html);
    }

    // ── BuildQuickConnectHtml ──────────────────────────────────────────────────

    private static readonly MethodInfo _buildQuickConnectHtml =
        typeof(OidcController).GetMethod(
            "BuildQuickConnectHtml",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void BuildQuickConnectHtml_ContainsCodeEntryFormAndBasePathPrefixedAuthorizeUrl()
    {
        // Act
        var html = (string)_buildQuickConnectHtml.Invoke(null, ["token123", "keycloak", "nonce123"])!;

        // Assert
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
        // Arrange
        const string maliciousProviderId = "kc'; alert(1); //";

        // Act
        var html = (string)_buildQuickConnectHtml.Invoke(null, ["token123", maliciousProviderId, "nonce123"])!;

        // Assert
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(maliciousProviderId), html);
        Assert.DoesNotContain("const providerId = 'kc'", html);
    }

    [Fact]
    public void BuildQuickConnectHtml_IncludesNonceOnScriptTag()
    {
        // Act
        var html = (string)_buildQuickConnectHtml.Invoke(null, ["token123", "keycloak", "abc123=="])!;

        // Assert
        Assert.Contains("<script nonce=\"abc123==\">", html);
    }

    // ── Authenticate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Authenticate_ProviderDisabled_ReturnsNotFound()
    {
        // Arrange — provider existed when the session was authorized but was disabled afterward;
        // the session token (5-minute TTL) must not still let the login through.
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = false }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        // Act
        var result = await controller.Authenticate("keycloak", new AuthenticateRequest { Token = token! });

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Authenticate_ProviderRemoved_ReturnsNotFound()
    {
        // Arrange — no provider configured at all (e.g. removed from the admin UI mid-flow).
        _fixture.SetConfiguration(new PluginConfiguration());
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        // Act
        var result = await controller.Authenticate("keycloak", new AuthenticateRequest { Token = token! });

        // Assert
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

    // Existing OIDC user, already registered to `providerId` — the "happy path" identity that
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
        // Arrange
        var controller = MakeController();

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = "does-not-exist", Code = "123456" });

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_ProviderMismatch_ReturnsBadRequest()
    {
        // Arrange
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        // Act — session was authorized for "keycloak" but Authorize is called for "okta"
        var result = await controller.QuickConnectAuthorize(
            "okta", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_ProviderDisabled_ReturnsNotFound()
    {
        // Arrange — provider existed when the session was authorized but was disabled afterward.
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = false }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var controller = MakeController(stateManager: stateManager);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_QuickConnectDisabled_ReturnsBadRequest()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = true }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession());
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(false);
        var controller = MakeController(stateManager: stateManager, quickConnect: quickConnect);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        // The session must remain valid — the admin might enable Quick Connect and the user retries.
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_MissingCode_ReturnsBadRequest()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", Enabled = true }]
        });
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession());
        var controller = MakeController(stateManager: stateManager);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "   " });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_UserSyncFails_ReturnsForbid()
    {
        // Arrange — user does not exist and auto-creation is disabled, so SyncUserAsync throws.
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

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_Success_ReturnsOkAndInvalidatesSession()
    {
        // Arrange
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), "123456").Returns(Task.FromResult(true));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "123456" });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        // The one-time session must be invalidated so the token can't be replayed.
        Assert.Null(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_CodeTrimmed_StillMatches()
    {
        // Arrange — a code pasted with surrounding whitespace must still authorize.
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), "123456").Returns(Task.FromResult(true));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "  123456  " });

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task QuickConnectAuthorize_AuthorizationRejected_ReturnsBadRequestAndKeepsSessionValid()
    {
        // Arrange
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>()).Returns(Task.FromResult(false));
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "999999" });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        // A retry with the correct code should still be possible.
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    // Named to match the catch-by-type-name pattern in QuickConnectAuthorize, which matches on
    // ex.GetType().Name rather than a hard assembly reference (Jellyfin's QuickConnect service
    // throws its own exception types that vary across versions).
    private sealed class ResourceNotFoundException : Exception;

    private sealed class AuthenticationException : Exception;

    [Fact]
    public async Task QuickConnectAuthorize_UnknownCode_ReturnsBadRequestAndKeepsSessionValid()
    {
        // Arrange
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns<bool>(_ => throw new ResourceNotFoundException());
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "000000" });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(stateManager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public async Task QuickConnectAuthorize_QuickConnectNotActive_ReturnsBadRequest()
    {
        // Arrange
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var token = stateManager.StoreAuthorizedSession(MakeQcSession(username: "alice", providerId: "keycloak"));
        var (userManager, _) = MakeSyncableUser("alice", "keycloak");
        ConfigureForSyncableUser("alice", "keycloak");
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(true);
        quickConnect.AuthorizeRequest(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns<bool>(_ => throw new AuthenticationException());
        var controller = MakeController(stateManager: stateManager, userManager: userManager, quickConnect: quickConnect);

        // Act
        var result = await controller.QuickConnectAuthorize(
            "keycloak", new QuickConnectAuthorizeRequest { Token = token!, Code = "000000" });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
