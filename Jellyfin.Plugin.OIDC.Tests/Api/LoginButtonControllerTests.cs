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

    private static string BrandingField(ActionResult result, string name)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (string)ok.Value!.GetType().GetProperty(name)!.GetValue(ok.Value)!;
    }

    private static string GetBrandingHtml(ActionResult result) => BrandingField(result, "Html");

    private static string GetBrandingCss(ActionResult result) => BrandingField(result, "Css");

    // ── GetLoginButtonsScript ──────────────────────────────────────────────────

    [Fact]
    public void GetLoginButtonsScript_NoProviders_ReturnsEmptyContent()
    {
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        var result = MakeController().GetLoginButtonsScript();

        Assert.Equal("", Assert.IsType<ContentResult>(result).Content);
    }

    [Fact]
    public void GetLoginButtonsScript_OneProvider_ContainsProviderId()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "my-provider", DisplayName = "My IdP", Enabled = true }]
        });

        var result = MakeController().GetLoginButtonsScript();

        Assert.Contains("my-provider", Assert.IsType<ContentResult>(result).Content);
    }

    [Fact]
    public void GetLoginButtonsScript_CustomColor_InInjectedCss()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#FF5733", Enabled = true }]
        });

        var content = Assert.IsType<ContentResult>(MakeController().GetLoginButtonsScript()).Content;

        // The script carries the builder's CSS (JSON-encoded) which colours the button by
        // [data-provider]; no inline colour on the element.
        Assert.Contains("#FF5733", content);
        Assert.Contains("data-provider", content);
    }

    [Fact]
    public void GetLoginButtonsScript_DisabledProvider_NotIncluded()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "enabled-p", DisplayName = "Enabled", Enabled = true },
                new OidcProviderConfig { ProviderId = "disabled-p", DisplayName = "Disabled", Enabled = false }
            ]
        });

        var content = Assert.IsType<ContentResult>(MakeController().GetLoginButtonsScript()).Content;

        Assert.Contains("enabled-p", content);
        Assert.DoesNotContain("disabled-p", content);
    }

    [Fact]
    public void GetLoginButtonsScript_ContainsBasePathDerivation()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var content = Assert.IsType<ContentResult>(MakeController().GetLoginButtonsScript()).Content;

        Assert.Contains("window.location.pathname.split('/web/')", content);
        Assert.Contains("basePath + '/sso/OIDC/Start/'", content);
    }

    [Fact]
    public void GetLoginButtonsScript_UsesNativeButtonClasses()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var content = Assert.IsType<ContentResult>(MakeController().GetLoginButtonsScript()).Content;

        Assert.Contains("raised button-submit block emby-button oidc-sso-btn", content);
        Assert.DoesNotContain("border-radius", content);
        Assert.DoesNotContain("padding:0.7em", content);
    }

    [Fact]
    public void GetLoginButtonsScript_DefaultColor_NoColourRule()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#4285F4", Enabled = true }]
        });

        var content = Assert.IsType<ContentResult>(MakeController().GetLoginButtonsScript()).Content;

        // Default colour ⇒ builder emits no per-provider background rule.
        Assert.DoesNotContain("#4285F4", content);
        Assert.DoesNotContain("background-color", content);
    }

    [Fact]
    public void GetLoginButtonsScript_ContainsQuickConnectLink()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var content = Assert.IsType<ContentResult>(MakeController().GetLoginButtonsScript()).Content;

        Assert.Contains("basePath + '/sso/OIDC/QuickConnect/'", content);
        Assert.Contains("Quick Connect", content);
    }

    // ── GetBrandingSnippet ─────────────────────────────────────────────────────

    [Fact]
    public void GetBrandingSnippet_NoProviders_ReturnsEmptyHtmlAndCss()
    {
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        var result = MakeController().GetBrandingSnippet();

        Assert.Equal("", GetBrandingHtml(result));
        Assert.Equal("", GetBrandingCss(result));
    }

    [Fact]
    public void GetBrandingSnippet_HtmlAndCss_AreMarkerFenced()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var result = MakeController().GetBrandingSnippet();

        var html = GetBrandingHtml(result);
        Assert.StartsWith("<!-- oidc-sso-buttons:start -->", html);
        Assert.EndsWith("<!-- oidc-sso-buttons:end -->", html);

        var css = GetBrandingCss(result);
        Assert.StartsWith("/* oidc-sso-buttons:start */", css);
        Assert.EndsWith("/* oidc-sso-buttons:end */", css);
    }

    [Fact]
    public void GetBrandingSnippet_DisplayName_IsHtmlEncoded()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "<script>alert('xss')</script>", Enabled = true }
            ]
        });

        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GetBrandingSnippet_Html_HasNativeClassesAndNoInlineStyle()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        Assert.Contains("id=\"oidc-sso-buttons\"", html);
        Assert.Contains("class=\"raised button-submit block emby-button oidc-sso-btn\"", html);
        Assert.Contains("data-provider=\"p1\"", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void GetBrandingSnippet_Css_ReordersAboveFormAndSetsWhiteText()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var css = GetBrandingCss(MakeController().GetBrandingSnippet());

        Assert.Contains("order:-1", css);
        Assert.Contains("color:#fff", css);
    }

    [Fact]
    public void GetBrandingSnippet_BadButtonColor_NoColourRule()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "javascript:alert(1)", Enabled = true }
            ]
        });

        var css = GetBrandingCss(MakeController().GetBrandingSnippet());

        Assert.DoesNotContain("javascript:alert(1)", css);
        Assert.DoesNotContain("background-color", css);
    }

    [Fact]
    public void GetBrandingSnippet_DefaultColor_NoColourRule()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#4285F4", Enabled = true }
            ]
        });

        var css = GetBrandingCss(MakeController().GetBrandingSnippet());

        Assert.DoesNotContain("background-color", css);
        Assert.DoesNotContain("#4285F4", css);
    }

    [Fact]
    public void GetBrandingSnippet_CustomColor_ScopedRuleClearsGradient()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#1A2B3C", Enabled = true }
            ]
        });

        var css = GetBrandingCss(MakeController().GetBrandingSnippet());

        Assert.Contains("a[data-provider=\"p1\"]{background-color:#1A2B3C;background-image:none}", css);
    }

    // ── Base URL / PathBase handling ──────────────────────────────────────────

    [Fact]
    public void GetBrandingSnippet_PathBaseSet_HrefIncludesPathBase()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }]
        });
        var controller = MakeController();
        controller.HttpContext.Request.PathBase = new PathString("/jellyfin");

        var html = GetBrandingHtml(controller.GetBrandingSnippet());

        Assert.Contains("href=\"/jellyfin/sso/OIDC/Start/keycloak\"", html);
    }

    [Fact]
    public void GetBrandingSnippet_NoPathBase_HrefIsRootRelative()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }]
        });

        var html = GetBrandingHtml(MakeController().GetBrandingSnippet());

        Assert.Contains("href=\"/sso/OIDC/Start/keycloak\"", html);
    }
}
