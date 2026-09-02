using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.OIDC.Configuration;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Builds the SSO login-button snippet that goes into Jellyfin's Branding settings:
/// an HTML block for <b>Login Disclaimer</b> and a CSS block for <b>Custom CSS</b>.
/// Both are wrapped in marker comments so they can be spliced in/out of existing
/// branding content without disturbing anything else.
///
/// Web client only — native/TV apps don't render Login Disclaimer or Custom CSS and
/// use Quick Connect instead.
/// </summary>
public static class BrandingSnippetBuilder
{
    public const string HtmlStart = "<!-- oidc-sso-buttons:start -->";
    public const string HtmlEnd = "<!-- oidc-sso-buttons:end -->";
    public const string CssStart = "/* oidc-sso-buttons:start */";
    public const string CssEnd = "/* oidc-sso-buttons:end */";

    private const string DefaultButtonColor = "#4285F4";

    // #RGB / #RGBA / #RRGGBB / #RRGGBBAA, or a CSS named colour (letters/hyphens only).
    private static readonly Regex _safeCssColor = new(
        @"^(#[0-9a-fA-F]{3,8}|[a-zA-Z\-]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The provider's brand colour, only when the admin set a valid, non-default value.
    /// Null means "let the active theme colour the button".
    /// </summary>
    public static string? CustomBrandColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var trimmed = color.Trim();
        return _safeCssColor.IsMatch(trimmed)
               && !string.Equals(trimmed, DefaultButtonColor, System.StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    /// <summary>
    /// Builds the marker-fenced (Html, Css) pair for the given enabled providers.
    /// Returns empty strings when there are no providers.
    /// </summary>
    /// <param name="hideManualLogin">
    /// When true, the CSS hides the web login's username/password form and the Forgot
    /// Password button (Quick Connect stays) and the HTML adds a heading back.
    /// </param>
    /// <param name="loginTitle">Heading text used when <paramref name="hideManualLogin"/> is set.</param>
    /// <param name="loginSubtitle">
    /// Optional smaller line under the heading (e.g. TV / Quick Connect instructions), shown
    /// only when <paramref name="hideManualLogin"/> is set and this is non-blank.
    /// </param>
    public static (string Html, string Css) Build(
        IEnumerable<OidcProviderConfig> enabledProviders,
        string basePath,
        bool hideManualLogin = false,
        string? loginTitle = null,
        string? loginSubtitle = null)
    {
        var providers = enabledProviders?.ToList() ?? new List<OidcProviderConfig>();
        if (providers.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var prefix = basePath ?? string.Empty;

        var html = new StringBuilder();
        html.Append(HtmlStart).Append('\n');
        // readOnlyContent is Jellyfin's own class for the login button stack, so themes style
        // this block; the id is just our splice/CSS hook.
        html.Append("<div id=\"oidc-sso-buttons\" class=\"readOnlyContent\">\n");
        if (hideManualLogin)
        {
            // Use Jellyfin's own typography classes (.sectionTitle / .fieldDescription) so custom
            // themes style these automatically; our classes are just layout hooks.
            var title = System.Net.WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(loginTitle) ? "Please sign in" : loginTitle.Trim());
            html.Append("  <h1 class=\"sectionTitle oidc-sso-title\">").Append(title).Append("</h1>\n");

            if (!string.IsNullOrWhiteSpace(loginSubtitle))
            {
                var subtitle = System.Net.WebUtility.HtmlEncode(loginSubtitle.Trim());
                html.Append("  <div class=\"fieldDescription oidc-sso-subtitle\">").Append(subtitle).Append("</div>\n");
            }
        }

        foreach (var p in providers)
        {
            var name = System.Net.WebUtility.HtmlEncode(p.DisplayName);
            var providerAttr = System.Net.WebUtility.HtmlEncode(p.ProviderId);
            var href = $"{prefix}/sso/OIDC/Start/{System.Net.WebUtility.UrlEncode(p.ProviderId)}";
            // target="_self" is a best effort — Jellyfin's disclaimer sanitizer may still force
            // target="_blank" on all links, so the flow can open in a new tab.
            html.Append("  <a class=\"raised button-submit block emby-button oidc-sso-btn\" target=\"_self\" data-provider=\"")
                .Append(providerAttr)
                .Append("\" href=\"")
                .Append(href)
                .Append("\"><span>")
                .Append(name)
                .Append("</span></a>\n");
        }
        html.Append("</div>\n");
        html.Append(HtmlEnd);

        var css = new StringBuilder();
        css.Append(CssStart).Append('\n');
        // Jellyfin 10.11 login page: .readOnlyContent (after <form>) holds the Quick Connect /
        // Forgot Password buttons and .loginDisclaimerContainer. Make it a flex column and pull
        // the disclaimer to the top so the SSO buttons sit directly under "Sign In" (still
        // inside .readOnlyContent — CSS can't lift them out to above the <form>).
        css.Append(".readOnlyContent:has(#oidc-sso-buttons){display:flex;flex-direction:column}\n");
        css.Append(".readOnlyContent:has(#oidc-sso-buttons) .loginDisclaimerContainer{order:-1}\n");
        // Collapse the disclaimer wrapper/inner to a full-width block with no margin/padding of
        // its own, so the button sits as tight under "Sign In" as the native buttons do.
        css.Append(".loginDisclaimerContainer:has(#oidc-sso-buttons),\n");
        css.Append(".loginDisclaimerContainer:has(#oidc-sso-buttons) .loginDisclaimer{display:block;width:100%;margin:0;padding:0}\n");
        // Layout comes from .readOnlyContent (theme-styled); just fill the disclaimer and don't
        // add spacing of our own (the parent .readOnlyContent already has its margin).
        css.Append("#oidc-sso-buttons{width:100%;margin:0}\n");
        // Jellyfin upgrades the <a> to an emby-linkbutton and adds .button-link, which strips the
        // padding/flex box and collapses the button to a text-link height. Re-assert the native
        // submit-button box (full width, centred, real vertical padding) and white label. #id
        // beats those class rules without !important; radius/colour/hover still track the theme.
        css.Append("#oidc-sso-buttons a.oidc-sso-btn{display:flex;align-items:center;");
        css.Append("justify-content:center;width:100%;box-sizing:border-box;margin:0 0 .25em;");
        css.Append("padding:1.05em 1em;line-height:1;color:#fff;text-decoration:none;text-align:center}\n");
        foreach (var p in providers)
        {
            var brand = CustomBrandColor(p.ButtonColor);
            if (brand != null)
            {
                // background-image:none clears any theme gradient so the solid colour shows.
                css.Append("#oidc-sso-buttons a[data-provider=\"")
                    .Append(CssEscape(p.ProviderId))
                    .Append("\"]{background-color:")
                    .Append(brand)
                    .Append(";background-image:none}\n");
            }

            var icon = IconDataUri(p.ButtonIcon);
            if (icon != null)
            {
                css.Append("#oidc-sso-buttons a[data-provider=\"")
                    .Append(CssEscape(p.ProviderId))
                    .Append("\"]::before{content:\"\";display:inline-block;flex:0 0 auto;")
                    .Append("width:1.25em;height:1.25em;margin-right:.55em;")
                    .Append("background:center/contain no-repeat url(\"")
                    .Append(icon)
                    .Append("\")}\n");
            }
        }

        if (hideManualLogin)
        {
            // Keep Quick Connect (.btnQuick) visible; hide the password form and Forgot Password.
            css.Append("#loginPage .manualLoginForm{display:none}\n");
            css.Append("#loginPage .readOnlyContent .btnForgotPassword{display:none}\n");
            // Typography comes from .sectionTitle / .fieldDescription (theme-styled); only layout here.
            css.Append("#oidc-sso-buttons .oidc-sso-title{margin:.25em 0 .35em;text-align:center}\n");
            css.Append("#oidc-sso-buttons .oidc-sso-subtitle{margin:0 auto .9em;max-width:22em;text-align:center}\n");
        }

        css.Append(CssEnd);

        return (html.ToString(), css.ToString());
    }

    // Escapes a provider id for safe use inside a CSS attribute-selector string.
    // Provider ids are admin-set and normally [a-z0-9-]; this is defence in depth.
    private static string CssEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static readonly Regex _scriptOrHandler = new(
        @"<script[\s\S]*?</script\s*>|\son[a-zA-Z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Accepted image data-URI types: SVG plus the common raster formats.
    private static readonly Regex _imageDataUri = new(
        @"^data:image/(svg\+xml|png|jpe?g|gif|webp)[;,]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Cap the encoded icon so it can't bloat the Branding Custom CSS (~192 KB of image).
    private const int MaxIconDataUriLength = 262_144;

    /// <summary>
    /// Resolves the provider's <c>ButtonIcon</c> to a <c>data:</c> URI for a CSS
    /// <c>url("…")</c>, or null when there's no usable icon. Accepts a bundled key,
    /// raw <c>&lt;svg&gt;</c> markup, or a <c>data:image/(svg+xml|png|jpeg|gif|webp)</c> URI.
    /// </summary>
    private static string? IconDataUri(string? buttonIcon)
    {
        if (string.IsNullOrWhiteSpace(buttonIcon))
        {
            return null;
        }

        var v = buttonIcon.Trim();

        if (v.StartsWith("data:", System.StringComparison.OrdinalIgnoreCase))
        {
            // Known image type, size-bounded, and no scripts/handlers or CSS-breaking chars.
            return _imageDataUri.IsMatch(v)
                   && v.Length <= MaxIconDataUriLength
                   && !_scriptOrHandler.IsMatch(v)
                   && v.IndexOf('"') < 0
                   && v.IndexOf(')') < 0
                ? v
                : null;
        }

        // A pasted or uploaded .svg file often starts with an <?xml …?> prolog, a <!DOCTYPE>,
        // or comments — take everything from the first <svg tag onward.
        var svgStart = v.IndexOf("<svg", System.StringComparison.OrdinalIgnoreCase);
        if (svgStart >= 0)
        {
            var cleaned = _scriptOrHandler.Replace(v.Substring(svgStart), string.Empty);
            var bytes = System.Text.Encoding.UTF8.GetBytes(cleaned);
            return "data:image/svg+xml;base64," + System.Convert.ToBase64String(bytes);
        }

        return KnownProviderIcons.TryGet(v);
    }
}
