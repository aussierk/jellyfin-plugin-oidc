using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Api;

[Xunit.Collection("OidcPlugin")]
public class LoginButtonControllerTests
{
    private readonly PluginTestFixture _fixture;

    public LoginButtonControllerTests(PluginTestFixture fixture) => _fixture = fixture;

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
        var result = new LoginButtonController().GetLoginButtonsScript();

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
        var result = new LoginButtonController().GetLoginButtonsScript();

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
        var result = new LoginButtonController().GetLoginButtonsScript();

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
            new LoginButtonController().GetLoginButtonsScript()).Content;

        // Assert
        Assert.Contains("enabled-p", content);
        Assert.DoesNotContain("disabled-p", content);
    }

    // ── GetBrandingSnippet ─────────────────────────────────────────────────────

    [Fact]
    public void GetBrandingSnippet_NoProviders_ReturnsEmptyHtml()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        // Act
        var html = GetBrandingHtml(new LoginButtonController().GetBrandingSnippet());

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
        var html = GetBrandingHtml(new LoginButtonController().GetBrandingSnippet());

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
        var html = GetBrandingHtml(new LoginButtonController().GetBrandingSnippet());

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
        var html = GetBrandingHtml(new LoginButtonController().GetBrandingSnippet());

        // Assert
        Assert.Contains("#1A2B3C", html);
    }
}
