using Jellyfin.Plugin.OIDC.Api;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller.QuickConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Api;

[Xunit.Collection("OidcPlugin")]
public class LoginButtonControllerTests
{
    private readonly PluginTestFixture _fixture;

    public LoginButtonControllerTests(PluginTestFixture fixture) => _fixture = fixture;

    private static LoginButtonController MakeController(bool quickConnectEnabled = false)
    {
        var quickConnect = Substitute.For<IQuickConnect>();
        quickConnect.IsEnabled.Returns(quickConnectEnabled);
        return new LoginButtonController(quickConnect)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static string SnippetField(ActionResult result, string name)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (string)ok.Value!.GetType().GetProperty(name)!.GetValue(ok.Value)!;
    }

    private static string SnippetHtml(ActionResult result) => SnippetField(result, "Html");

    private static string SnippetCss(ActionResult result) => SnippetField(result, "Css");

    // ── GetLoginButtonSnippet ──────────────────────────────────────────────────

    [Fact]
    public void GetLoginButtonSnippet_NoProviders_ReturnsEmptyHtmlAndCss()
    {
        _fixture.SetConfiguration(new PluginConfiguration { Providers = [] });

        var result = MakeController().GetLoginButtonSnippet();

        Assert.Equal("", SnippetHtml(result));
        Assert.Equal("", SnippetCss(result));
    }

    [Fact]
    public void GetLoginButtonSnippet_HtmlAndCss_AreMarkerFenced()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var result = MakeController().GetLoginButtonSnippet();

        var html = SnippetHtml(result);
        Assert.StartsWith("<!-- oidc-sso-buttons:start -->", html);
        Assert.EndsWith("<!-- oidc-sso-buttons:end -->", html);

        var css = SnippetCss(result);
        Assert.StartsWith("/* oidc-sso-buttons:start */", css);
        Assert.EndsWith("/* oidc-sso-buttons:end */", css);
    }

    [Fact]
    public void GetLoginButtonSnippet_QuickConnectEnabled_IncludesPerProviderQcLink()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }]
        });

        var result = MakeController(quickConnectEnabled: true).GetLoginButtonSnippet();

        var html = SnippetHtml(result);
        Assert.Contains("href=\"/sso/OIDC/QuickConnect/keycloak\"", html);
        Assert.Contains("class=\"fieldDescription oidc-sso-qc-link\"", html);
        Assert.Contains("Sign in a device with Keycloak (Quick Connect)", html);
        Assert.DoesNotContain("style=", html);
        Assert.Contains("#oidc-sso-buttons a.oidc-sso-qc-link{", SnippetCss(result));
    }

    [Fact]
    public void GetLoginButtonSnippet_QuickConnectDisabled_OmitsQcLink()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }]
        });

        var result = MakeController().GetLoginButtonSnippet(); // quickConnectEnabled: false

        Assert.DoesNotContain("oidc-sso-qc-link", SnippetHtml(result));
        Assert.DoesNotContain("oidc-sso-qc-link", SnippetCss(result));
    }

    [Fact]
    public void GetLoginButtonSnippet_DisplayName_IsHtmlEncoded()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "<script>alert('xss')</script>", Enabled = true }
            ]
        });

        var html = SnippetHtml(MakeController().GetLoginButtonSnippet());

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GetLoginButtonSnippet_Html_HasNativeClassesAndNoInlineStyle()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var html = SnippetHtml(MakeController().GetLoginButtonSnippet());

        Assert.Contains("id=\"oidc-sso-buttons\"", html);
        Assert.Contains("class=\"raised button-submit block emby-button oidc-sso-btn\"", html);
        Assert.Contains("data-provider=\"p1\"", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void GetLoginButtonSnippet_Css_ReordersAboveFormAndSetsWhiteText()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", Enabled = true }]
        });

        var css = SnippetCss(MakeController().GetLoginButtonSnippet());

        Assert.Contains("order:-1", css);
        Assert.Contains("color:#fff", css);
    }

    [Fact]
    public void GetLoginButtonSnippet_BadButtonColor_NoColourRule()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "javascript:alert(1)", Enabled = true }
            ]
        });

        var css = SnippetCss(MakeController().GetLoginButtonSnippet());

        Assert.DoesNotContain("javascript:alert(1)", css);
        Assert.DoesNotContain("background-color", css);
    }

    [Fact]
    public void GetLoginButtonSnippet_DefaultColor_NoColourRule()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#4285F4", Enabled = true }
            ]
        });

        var css = SnippetCss(MakeController().GetLoginButtonSnippet());

        Assert.DoesNotContain("background-color", css);
        Assert.DoesNotContain("#4285F4", css);
    }

    [Fact]
    public void GetLoginButtonSnippet_CustomColor_ScopedRuleClearsGradient()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "p1", DisplayName = "Test", ButtonColor = "#1A2B3C", Enabled = true }
            ]
        });

        var css = SnippetCss(MakeController().GetLoginButtonSnippet());

        Assert.Contains("a[data-provider=\"p1\"]{background-color:#1A2B3C;background-image:none}", css);
    }

    // ── Base URL / PathBase handling ──────────────────────────────────────────

    [Fact]
    public void GetLoginButtonSnippet_PathBaseSet_HrefIncludesPathBase()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }]
        });
        var controller = MakeController();
        controller.HttpContext.Request.PathBase = new PathString("/jellyfin");

        var html = SnippetHtml(controller.GetLoginButtonSnippet());

        Assert.Contains("href=\"/jellyfin/sso/OIDC/Start/keycloak\"", html);
    }

    [Fact]
    public void GetLoginButtonSnippet_NoPathBase_HrefIsRootRelative()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "Keycloak", Enabled = true }]
        });

        var html = SnippetHtml(MakeController().GetLoginButtonSnippet());

        Assert.Contains("href=\"/sso/OIDC/Start/keycloak\"", html);
    }
}
