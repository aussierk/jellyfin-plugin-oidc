using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using IdentityModel;
using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
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

    private OidcController MakeController(IServerApplicationHost? appHost = null)
    {
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var userManager = Substitute.For<IUserManager>();
        var libraryManager = Substitute.For<ILibraryManager>();
        var rbacService = new RbacService(userManager, libraryManager, NullLogger<RbacService>.Instance);
        var userSyncService = new UserSyncService(userManager, rbacService, NullLogger<UserSyncService>.Instance);
        var sessionManager = Substitute.For<ISessionManager>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        if (appHost == null)
        {
            appHost = Substitute.For<IServerApplicationHost>();
            appHost.GetSmartApiUrl(Arg.Any<HttpRequest>()).Returns("https://jellyfin.test");
        }

        var controller = new OidcController(
            stateManager, userSyncService, sessionManager,
            httpClientFactory, appHost, NullLogger<OidcController>.Instance);

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
}
