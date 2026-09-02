using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class LoginButtonSnippetBuilderTests
{
    private static OidcProviderConfig Provider(string id = "p1", string name = "Test", string color = "#4285F4", string icon = "")
        => new() { ProviderId = id, DisplayName = name, ButtonColor = color, ButtonIcon = icon, Enabled = true };

    [Fact]
    public void Build_NoProviders_ReturnsEmptyPair()
    {
        var (html, css) = LoginButtonSnippetBuilder.Build([], string.Empty);

        Assert.Equal(string.Empty, html);
        Assert.Equal(string.Empty, css);
    }

    [Fact]
    public void Build_Html_IsMarkerFencedWithNativeClassesAndNoInlineStyle()
    {
        var (html, _) = LoginButtonSnippetBuilder.Build([Provider(id: "authentik", name: "Authentik")], string.Empty);

        Assert.StartsWith(LoginButtonSnippetBuilder.HtmlStart, html);
        Assert.EndsWith(LoginButtonSnippetBuilder.HtmlEnd, html);
        Assert.Contains("<div id=\"oidc-sso-buttons\" class=\"readOnlyContent\">", html);
        Assert.Contains("class=\"raised button-submit block emby-button oidc-sso-btn\"", html);
        Assert.Contains("data-provider=\"authentik\"", html);
        Assert.Contains("target=\"_self\"", html);
        Assert.Contains("<span>Authentik</span></a>", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void Build_Css_IsMarkerFencedAndReordersAboveFormWithWhiteText()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider()], string.Empty);

        Assert.StartsWith(LoginButtonSnippetBuilder.CssStart, css);
        Assert.EndsWith(LoginButtonSnippetBuilder.CssEnd, css);
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
        var (html, _) = LoginButtonSnippetBuilder.Build([Provider(id: "keycloak")], "/jellyfin");

        Assert.Contains("href=\"/jellyfin/sso/OIDC/Start/keycloak\"", html);
    }

    [Fact]
    public void Build_NoBasePath_HrefIsRootRelative()
    {
        var (html, _) = LoginButtonSnippetBuilder.Build([Provider(id: "keycloak")], string.Empty);

        Assert.Contains("href=\"/sso/OIDC/Start/keycloak\"", html);
    }

    [Theory]
    [InlineData("#4285F4")]            // config default
    [InlineData("javascript:alert(1)")] // unsafe
    [InlineData("")]                   // unset
    public void Build_NonCustomColor_EmitsNoPerProviderRule(string color)
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(color: color)], string.Empty);

        Assert.DoesNotContain("background-color", css);
        Assert.DoesNotContain("[data-provider=", css);
    }

    [Fact]
    public void Build_CustomColor_EmitsScopedRuleThatClearsGradient()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(color: "#1A2B3C")], string.Empty);

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

        var (html, _) = LoginButtonSnippetBuilder.Build([provider], string.Empty);

        Assert.DoesNotContain("<b>Hi</b>", html);
        Assert.Contains("&lt;b&gt;Hi&lt;/b&gt;", html);
        Assert.Contains("/sso/OIDC/Start/space+id", html);
    }

    [Fact]
    public void Build_MultipleProviders_OneCustomColour_EmitsExactlyOneRule()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build(
            [Provider(id: "a", color: "#4285F4"), Provider(id: "b", color: "#112233")],
            string.Empty);

        Assert.DoesNotContain("data-provider=\"a\"]{background-color", css);
        Assert.Contains("a[data-provider=\"b\"]{background-color:#112233;background-image:none}", css);
    }

    // ── Button Icon ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_NoIcon_EmitsNoBeforeRule()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider()], string.Empty);

        Assert.DoesNotContain("::before", css);
    }

    [Fact]
    public void Build_KnownIconKey_EmitsBeforeRuleWithDataUri()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(icon: "authentik")], string.Empty);

        Assert.Contains("#oidc-sso-buttons a[data-provider=\"p1\"]::before{", css);
        Assert.Contains("url(\"data:image/svg+xml;base64,", css);
    }

    [Fact]
    public void Build_RawSvgIcon_IsSanitisedAndBase64Encoded()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build(
            [Provider(icon: "<svg onload=\"alert(1)\"><script>alert(2)</script><path d=\"M0 0h1v1z\"/></svg>")],
            string.Empty);

        Assert.Contains("::before{", css);
        Assert.Contains("url(\"data:image/svg+xml;base64,", css);
        Assert.DoesNotContain("onload", css);
        Assert.DoesNotContain("<script", css);
    }

    [Fact]
    public void Build_UploadedSvgFileWithXmlProlog_IsAccepted()
    {
        // What FileReader.readAsText hands back for a real .svg download.
        var file = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- Generator -->\n"
                   + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M0 0h1v1z\"/></svg>\n";

        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(icon: file)], string.Empty);

        Assert.Contains("#oidc-sso-buttons a[data-provider=\"p1\"]::before{", css);
        Assert.Contains("url(\"data:image/svg+xml;base64,", css);
        // The XML prolog is dropped before encoding.
        var b64 = System.Text.RegularExpressions.Regex.Match(css, @"base64,([A-Za-z0-9+/=]+)").Groups[1].Value;
        var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(b64));
        Assert.StartsWith("<svg", decoded);
    }

    [Fact]
    public void Build_UnclosedSvgTag_IsStillSanitisedAndEncoded()
    {
        // No closing </svg> at all - the sanitizer doesn't require well-formed markup, only
        // that dangerous content gets stripped regardless of whether the tag is ever closed.
        var (_, css) = LoginButtonSnippetBuilder.Build(
            [Provider(icon: "<svg onload=\"alert(1)\"><path d=\"M0 0h1v1z\"")],
            string.Empty);

        Assert.Contains("::before{", css);
        Assert.Contains("url(\"data:image/svg+xml;base64,", css);
        Assert.DoesNotContain("onload", css);
    }

    [Fact]
    public void Build_UnclosedScriptTag_IsStrippedToEndOfString()
    {
        // Regression test: <script[\s\S]*?</script\s*> alone requires a literal closing tag to
        // exist anywhere in the string. A truncated <script> with no </script> at all used to
        // survive stripping unchanged and get base64-encoded straight into the "sanitised" icon.
        var (_, css) = LoginButtonSnippetBuilder.Build(
            [Provider(icon: "<svg><script>alert(document.cookie)")],
            string.Empty);

        Assert.Contains("::before{", css);
        var b64 = System.Text.RegularExpressions.Regex.Match(css, @"base64,([A-Za-z0-9+/=]+)").Groups[1].Value;
        var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(b64));
        Assert.DoesNotContain("<script", decoded, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", decoded);
    }

    [Fact]
    public void Build_UnknownIconKey_EmitsNoBeforeRule()
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(icon: "not-a-real-icon")], string.Empty);

        Assert.DoesNotContain("::before", css);
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8Xw8AAoMBgDTD2qgAAAAASUVORK5CYII=")]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACwAAAAAAQABAAACAkQBADs=")]
    public void Build_RasterDataUriIcon_IsAccepted(string dataUri)
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(icon: dataUri)], string.Empty);

        Assert.Contains("#oidc-sso-buttons a[data-provider=\"p1\"]::before{", css);
        Assert.Contains("url(\"" + dataUri + "\")", css);
    }

    [Theory]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("data:application/octet-stream;base64,AAAA")]
    public void Build_NonImageDataUri_EmitsNoBeforeRule(string dataUri)
    {
        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(icon: dataUri)], string.Empty);

        Assert.DoesNotContain("::before", css);
    }

    [Fact]
    public void Build_OversizeIconDataUri_EmitsNoBeforeRule()
    {
        var huge = "data:image/png;base64," + new string('A', 300_000);

        var (_, css) = LoginButtonSnippetBuilder.Build([Provider(icon: huge)], string.Empty);

        Assert.DoesNotContain("::before", css);
    }

    // ── Quick Connect links ──────────────────────────────────────────────────

    [Fact]
    public void Build_IncludeQuickConnect_AddsPerProviderLinkAndCssRule()
    {
        var (html, css) = LoginButtonSnippetBuilder.Build(
            [Provider(id: "keycloak", name: "Keycloak"), Provider(id: "authentik", name: "Authentik")],
            string.Empty, includeQuickConnect: true);

        Assert.Contains("href=\"/sso/OIDC/QuickConnect/keycloak\"", html);
        Assert.Contains("href=\"/sso/OIDC/QuickConnect/authentik\"", html);
        Assert.Contains("class=\"fieldDescription oidc-sso-qc-link\"", html);
        Assert.Contains("Sign in a device with Keycloak (Quick Connect)</a>", html);
        Assert.DoesNotContain("style=", html);
        // Rule appears exactly once, in the static block - not repeated per provider.
        Assert.Contains("#oidc-sso-buttons a.oidc-sso-qc-link{", css);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(css, "oidc-sso-qc-link\\{"));
    }

    [Fact]
    public void Build_QuickConnect_DefaultsOff()
    {
        var (html, css) = LoginButtonSnippetBuilder.Build([Provider(id: "keycloak")], string.Empty);

        Assert.DoesNotContain("oidc-sso-qc-link", html);
        Assert.DoesNotContain("QuickConnect", html);
        Assert.DoesNotContain("oidc-sso-qc-link", css);
    }

    [Fact]
    public void Build_IncludeQuickConnect_LinkTextIsHtmlEncoded()
    {
        var provider = new OidcProviderConfig { ProviderId = "p1", DisplayName = "<b>IdP</b>", Enabled = true };

        var (html, _) = LoginButtonSnippetBuilder.Build([provider], string.Empty, includeQuickConnect: true);

        Assert.DoesNotContain("<b>IdP</b>", html);
        Assert.Contains("Sign in a device with &lt;b&gt;IdP&lt;/b&gt; (Quick Connect)", html);
    }

    // ── Hide manual login ────────────────────────────────────────────────────

    [Fact]
    public void Build_HideManualLogin_HidesFormAndForgotButKeepsQuickConnect_AndAddsHeading()
    {
        var (html, css) = LoginButtonSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true, loginTitle: "Log in here");

        // Heading uses Jellyfin's own .sectionTitle so custom themes style it.
        Assert.Contains("<h1 class=\"sectionTitle oidc-sso-title\">Log in here</h1>", html);
        Assert.Contains("#loginPage .manualLoginForm{display:none}", css);
        Assert.Contains(".btnForgotPassword{display:none}", css);
        Assert.DoesNotContain("btnQuick", css);
        Assert.Contains("#oidc-sso-buttons .oidc-sso-title{", css);
        // No hardcoded font sizing - typography is left to the theme.
        Assert.DoesNotContain("font-size", css);
    }

    [Fact]
    public void Build_HideManualLogin_WithSubtitle_AddsFieldDescriptionLine()
    {
        var (html, css) = LoginButtonSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true,
            loginTitle: "Sign in", loginSubtitle: "On a TV, use Quick Connect");

        Assert.Contains("<div class=\"fieldDescription oidc-sso-subtitle\">On a TV, use Quick Connect</div>", html);
        Assert.Contains("#oidc-sso-buttons .oidc-sso-subtitle{", css);
    }

    [Fact]
    public void Build_HideManualLogin_BlankSubtitle_OmitsTheLine()
    {
        var (html, _) = LoginButtonSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true, loginSubtitle: "   ");

        Assert.DoesNotContain("oidc-sso-subtitle", html);
    }

    [Fact]
    public void Build_Subtitle_IgnoredWhenNotHidingManualLogin()
    {
        var (html, _) = LoginButtonSnippetBuilder.Build(
            [Provider()], string.Empty, loginSubtitle: "should not appear");

        Assert.DoesNotContain("oidc-sso-subtitle", html);
        Assert.DoesNotContain("should not appear", html);
    }

    [Fact]
    public void Build_HideManualLogin_TitleIsHtmlEncoded()
    {
        var (html, _) = LoginButtonSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true, loginTitle: "<script>x</script>");

        Assert.DoesNotContain("<script>x</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Build_HideManualLogin_DefaultsHeadingToPleaseSignIn()
    {
        var (html, _) = LoginButtonSnippetBuilder.Build(
            [Provider()], string.Empty, hideManualLogin: true);

        Assert.Contains("<h1 class=\"sectionTitle oidc-sso-title\">Please sign in</h1>", html);
    }

    [Fact]
    public void Build_NoHideManualLogin_HasNoHeadingOrHideRules()
    {
        var (html, css) = LoginButtonSnippetBuilder.Build([Provider()], string.Empty);

        Assert.DoesNotContain("oidc-sso-title", html);
        Assert.DoesNotContain("manualLoginForm", css);
        Assert.DoesNotContain("btnForgotPassword", css);
    }

    // ── jellyfin-web coupling surface ─────────────────────────────────────────
    //
    // Every jellyfin-web class / id the spliced snippet leans on to inherit theme styling or
    // hide native login chrome. jellyfin-web can rename or drop any of these in a release with
    // no compiler error and no visible break until someone loads the login page - so this list
    // is the single audit point. Verified against jellyfin-web 10.11.x (this plugin's targetAbi).
    // On a Jellyfin bump: re-check each hook against jellyfin-web's login page markup/styles and
    // update here in lockstep with LoginButtonSnippetBuilder.
    [Theory]
    [InlineData("readOnlyContent")]           // native login-button stack - themes style it for free
    [InlineData("loginDisclaimerContainer")]  // wrapper we reorder above the form
    [InlineData("loginDisclaimer")]           // inner disclaimer box we widen
    [InlineData("raised")]                    // native <button>/<a> chrome on each SSO button
    [InlineData("button-submit")]
    [InlineData("emby-button")]
    [InlineData("sectionTitle")]              // native heading class for the injected title
    [InlineData("fieldDescription")]          // native muted-text class for subtitle + QC link
    [InlineData("loginPage")]                 // #loginPage scope for the hide-native-chrome rules
    [InlineData("manualLoginForm")]           // hidden when the admin opts to hide manual login
    [InlineData("btnForgotPassword")]         // hidden alongside it
    public void Build_DependsOnKnownJellyfinWebHook(string hook)
    {
        // Exercise every branch so optional hooks (title / QC link / hide-manual-login) are emitted.
        var (html, css) = LoginButtonSnippetBuilder.Build(
            [Provider()],
            "/jf",
            hideManualLogin: true,
            loginTitle: "Sign in",
            loginSubtitle: "Use your account",
            includeQuickConnect: true);

        Assert.Contains(hook, html + css);
    }
}
