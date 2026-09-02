using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class BrandingSnippetBuilderTests
{
    private static OidcProviderConfig Provider(string id = "p1", string name = "Test", string color = "#4285F4", string icon = "")
        => new() { ProviderId = id, DisplayName = name, ButtonColor = color, ButtonIcon = icon, Enabled = true };

    [Fact]
    public void Build_NoProviders_ReturnsEmptyPair()
    {
        var (html, css) = BrandingSnippetBuilder.Build([], string.Empty);

        Assert.Equal(string.Empty, html);
        Assert.Equal(string.Empty, css);
    }

    [Fact]
    public void Build_Html_IsMarkerFencedWithNativeClassesAndNoInlineStyle()
    {
        var (html, _) = BrandingSnippetBuilder.Build([Provider(id: "authentik", name: "Authentik")], string.Empty);

        Assert.StartsWith(BrandingSnippetBuilder.HtmlStart, html);
        Assert.EndsWith(BrandingSnippetBuilder.HtmlEnd, html);
        Assert.Contains("<div id=\"oidc-sso-buttons\">", html);
        Assert.Contains("class=\"raised button-submit block emby-button oidc-sso-btn\"", html);
        Assert.Contains("data-provider=\"authentik\"", html);
        Assert.Contains("target=\"_self\"", html);
        Assert.Contains("<span>Authentik</span></a>", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void Build_Css_IsMarkerFencedAndReordersAboveFormWithWhiteText()
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider()], string.Empty);

        Assert.StartsWith(BrandingSnippetBuilder.CssStart, css);
        Assert.EndsWith(BrandingSnippetBuilder.CssEnd, css);
        Assert.Contains(".readOnlyContent:has(#oidc-sso-buttons){display:flex;flex-direction:column}", css);
        Assert.Contains(".readOnlyContent:has(#oidc-sso-buttons) .loginDisclaimerContainer{order:-1}", css);
        Assert.Contains(".loginDisclaimerContainer:has(#oidc-sso-buttons) .loginDisclaimer{display:block;width:100%", css);
        Assert.Contains("#oidc-sso-buttons a.oidc-sso-btn{", css);
        Assert.Contains("width:100%", css);
        Assert.Contains("color:#fff", css);
        Assert.DoesNotContain("oidc-sso-sep", css);
    }

    [Fact]
    public void Build_BasePath_PrefixesHref()
    {
        var (html, _) = BrandingSnippetBuilder.Build([Provider(id: "keycloak")], "/jellyfin");

        Assert.Contains("href=\"/jellyfin/sso/OIDC/Start/keycloak\"", html);
    }

    [Fact]
    public void Build_NoBasePath_HrefIsRootRelative()
    {
        var (html, _) = BrandingSnippetBuilder.Build([Provider(id: "keycloak")], string.Empty);

        Assert.Contains("href=\"/sso/OIDC/Start/keycloak\"", html);
    }

    [Theory]
    [InlineData("#4285F4")]            // config default
    [InlineData("javascript:alert(1)")] // unsafe
    [InlineData("")]                   // unset
    public void Build_NonCustomColor_EmitsNoPerProviderRule(string color)
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider(color: color)], string.Empty);

        Assert.DoesNotContain("background-color", css);
        Assert.DoesNotContain("[data-provider=", css);
    }

    [Fact]
    public void Build_CustomColor_EmitsScopedRuleThatClearsGradient()
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider(color: "#1A2B3C")], string.Empty);

        Assert.Contains("#oidc-sso-buttons a[data-provider=\"p1\"]{background-color:#1A2B3C;background-image:none}", css);
    }

    [Fact]
    public void Build_DisplayName_IsHtmlEncoded_ProviderId_IsUrlEncoded()
    {
        var provider = new OidcProviderConfig
        {
            ProviderId = "space id",
            DisplayName = "<b>Hi</b>",
            Enabled = true
        };

        var (html, _) = BrandingSnippetBuilder.Build([provider], string.Empty);

        Assert.DoesNotContain("<b>Hi</b>", html);
        Assert.Contains("&lt;b&gt;Hi&lt;/b&gt;", html);
        Assert.Contains("/sso/OIDC/Start/space+id", html);
    }

    [Fact]
    public void Build_MultipleProviders_OneCustomColour_EmitsExactlyOneRule()
    {
        var (_, css) = BrandingSnippetBuilder.Build(
            [Provider(id: "a", color: "#4285F4"), Provider(id: "b", color: "#112233")],
            string.Empty);

        Assert.DoesNotContain("data-provider=\"a\"]{background-color", css);
        Assert.Contains("a[data-provider=\"b\"]{background-color:#112233;background-image:none}", css);
    }

    // ── Button Icon ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_NoIcon_EmitsNoBeforeRule()
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider()], string.Empty);

        Assert.DoesNotContain("::before", css);
    }

    [Fact]
    public void Build_KnownIconKey_EmitsBeforeRuleWithDataUri()
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider(icon: "authentik")], string.Empty);

        Assert.Contains("#oidc-sso-buttons a[data-provider=\"p1\"]::before{", css);
        Assert.Contains("url(\"data:image/svg+xml;base64,", css);
    }

    [Fact]
    public void Build_RawSvgIcon_IsSanitisedAndBase64Encoded()
    {
        var (_, css) = BrandingSnippetBuilder.Build(
            [Provider(icon: "<svg onload=\"alert(1)\"><script>alert(2)</script><path d=\"M0 0h1v1z\"/></svg>")],
            string.Empty);

        Assert.Contains("::before{", css);
        Assert.Contains("url(\"data:image/svg+xml;base64,", css);
        Assert.DoesNotContain("onload", css);
        Assert.DoesNotContain("<script", css);
    }

    [Fact]
    public void Build_UnknownIconKey_EmitsNoBeforeRule()
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider(icon: "not-a-real-icon")], string.Empty);

        Assert.DoesNotContain("::before", css);
    }

    // ── Hide manual login ────────────────────────────────────────────────────

    [Fact]
    public void Build_HideManualLogin_HidesFormAndForgotButKeepsQuickConnect_AndAddsHeading()
    {
        var (html, css) = BrandingSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true, loginTitle: "Log in here");

        Assert.Contains("<div class=\"oidc-sso-title\">Log in here</div>", html);
        Assert.Contains("#loginPage .manualLoginForm{display:none}", css);
        Assert.Contains(".btnForgotPassword{display:none}", css);
        Assert.DoesNotContain("btnQuick", css);
        Assert.Contains("#oidc-sso-buttons .oidc-sso-title{", css);
    }

    [Fact]
    public void Build_HideManualLogin_TitleIsHtmlEncoded()
    {
        var (html, _) = BrandingSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true, loginTitle: "<script>x</script>");

        Assert.DoesNotContain("<script>x</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Build_HideManualLogin_DefaultsHeadingToPleaseSignIn()
    {
        var (html, _) = BrandingSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true);

        Assert.Contains("<div class=\"oidc-sso-title\">Please sign in</div>", html);
    }

    [Fact]
    public void Build_NoHideManualLogin_HasNoHeadingOrHideRules()
    {
        var (html, css) = BrandingSnippetBuilder.Build([Provider()], string.Empty);

        Assert.DoesNotContain("oidc-sso-title", html);
        Assert.DoesNotContain("manualLoginForm", css);
        Assert.DoesNotContain("btnForgotPassword", css);
    }
}
