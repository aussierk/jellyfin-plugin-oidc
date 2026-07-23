using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Api;

[Xunit.Collection("OidcPlugin")]
public class LoginButtonControllerTests
{
    private readonly PluginTestFixture _fixture;

    public LoginButtonControllerTests(PluginTestFixture fixture) => _fixture = fixture;

    private static LoginButtonController MakeController() => new()
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private static string GetBrandingHtml(ActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (string)ok.Value!.GetType().GetProperty("Html")!.GetValue(ok.Value)!;
    }

    // ── GetLoginButtonsScript ──────────────────────────────────────────────────

    [Fact]
    public void GetLoginButtonsScript_NoProviders_ReturnsEmptyContent()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        // Act
        var result = MakeController().GetLoginButtonsScript();

        // Assert
        Assert.Equal("", Assert.IsType<ContentResult>(result).Content);
    }

    [Fact]
    public void GetLoginButtonsScript_OneProvider_ContainsProviderId()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "my-provider", DisplayName = "My IdP", Enabled = true }
            ]
        });

        // Act
        var result = MakeController().GetLoginButtonsScript();

        // Assert
        Assert.Contains("my-provider", Assert.IsType<ContentResult>(result).Content);
    }

    [Fact]
    public void GetLoginButtonsScript_OneProvider_ContainsButtonColor()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#FF5733", Enabled = true }
            ]
        });

        // Act
        var result = MakeController().GetLoginButtonsScript();

        // Assert
        Assert.Contains("#FF5733", Assert.IsType<ContentResult>(result).Content);
    }

    [Fact]
    public void GetLoginButtonsScript_DisabledProvider_NotIncluded()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "enabled-p", DisplayName = "Enabled", Enabled = true },
                new OidcProviderConfig { ProviderId = "disabled-p", DisplayName = "Disabled", Enabled = false }
            ]
        });

        // Act
        var content = Assert.IsType<ContentResult>(
            MakeController().GetLoginButtonsScript()).Content;

        // Assert
        Assert.Contains("enabled-p", content);
        Assert.DoesNotContain("disabled-p", content);
    }

    [Fact]
    public void GetLoginButtonsScript_ContainsBasePathDerivation()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }
            ]
        });

        // Act
        var content = Assert.IsType<ContentResult>(
            MakeController().GetLoginButtonsScript()).Content;

        // Assert — base path is derived client-side (base URL fix) and used to prefix the
        // generated link, not hardcoded root-relative.
        Assert.Contains("window.location.pathname.split('/web/')", content);
        Assert.Contains("basePath + '/sso/OIDC/Start/'", content);
    }

    [Fact]
    public void GetLoginButtonsScript_ContainsQuickConnectLink()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }
            ]
        });

        // Act
        var content = Assert.IsType<ContentResult>(
            MakeController().GetLoginButtonsScript()).Content;

        // Assert — a Quick Connect link is injected alongside the normal login button, for
        // signing in native/mobile apps that can't render the web button. Base-path-aware,
        // like the main button.
        Assert.Contains("basePath + '/sso/OIDC/QuickConnect/'", content);
        Assert.Contains("Quick Connect", content);
    }

    // ── GetBrandingSnippet ─────────────────────────────────────────────────────

    [Fact]
    public void GetBrandingSnippet_NoProviders_ReturnsEmptyHtml()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        // Act
        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        // Assert
        Assert.Equal("", html);
    }

    [Fact]
    public void GetBrandingSnippet_DisplayName_IsHtmlEncoded()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    ProviderId = "p1",
                    DisplayName = "<script>alert('xss')</script>",
                    Enabled = true
                }
            ]
        });

        // Act
        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        // Assert
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GetBrandingSnippet_BadButtonColor_FallsBackToDefault()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    ProviderId = "p1",
                    DisplayName = "Test",
                    ButtonColor = "javascript:alert(1)",
                    Enabled = true
                }
            ]
        });

        // Act
        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        // Assert
        Assert.DoesNotContain("javascript:alert(1)", html);
        Assert.Contains("#4285F4", html);
    }

    [Fact]
    public void GetBrandingSnippet_ValidColor_UsedAsIs()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#1A2B3C", Enabled = true }
            ]
        });

        // Act
        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        // Assert
        Assert.Contains("#1A2B3C", html);
    }

    // ── Base URL / PathBase handling ──────────────────────────────────────────

    [Fact]
    public void GetBrandingSnippet_PathBaseSet_HrefIncludesPathBase()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }
            ]
        });
        var controller = MakeController();
        controller.HttpContext.Request.PathBase = new PathString("/jellyfin");

        // Act
        var html = GetBrandingHtml(controller.GetBrandingSnippet());

        // Assert
        Assert.Contains("href=\"/jellyfin/sso/OIDC/Start/keycloak\"", html);
    }

    [Fact]
    public void GetBrandingSnippet_NoPathBase_HrefIsRootRelative()
    {
        // Arrange — no base URL configured; the fix must be a no-op in this (default) case.
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }
            ]
        });

        // Act
        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        // Assert
        Assert.Contains("href=\"/sso/OIDC/Start/keycloak\"", html);
    }
}
