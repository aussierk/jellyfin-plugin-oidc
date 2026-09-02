using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class BrandingSnippetBuilderTests
{
    private static OidcProviderConfig Provider(string id = "p1", string name = "Test", string color = "#4285F4")
        => new() { ProviderId = id, DisplayName = name, ButtonColor = color, Enabled = true };

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
        Assert.Contains(">Authentik</a>", html);
        Assert.Contains("oidc-sso-sep", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void Build_Css_IsMarkerFencedAndReordersAboveFormWithWhiteText()
    {
        var (_, css) = BrandingSnippetBuilder.Build([Provider()], string.Empty);

        Assert.StartsWith(BrandingSnippetBuilder.CssStart, css);
        Assert.EndsWith(BrandingSnippetBuilder.CssEnd, css);
        Assert.Contains(":has(#oidc-sso-buttons)", css);
        Assert.Contains("#oidc-sso-buttons{order:-1", css);
        Assert.Contains("#oidc-sso-buttons a.oidc-sso-btn{", css);
        Assert.Contains("color:#fff", css);
        Assert.Contains("#oidc-sso-buttons .oidc-sso-sep{", css);
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
}
