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
    public static (string Html, string Css) Build(IEnumerable<OidcProviderConfig> enabledProviders, string basePath)
    {
        var providers = enabledProviders?.ToList() ?? new List<OidcProviderConfig>();
        if (providers.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var prefix = basePath ?? string.Empty;

        var html = new StringBuilder();
        html.Append(HtmlStart).Append('\n');
        html.Append("<div id=\"oidc-sso-buttons\">\n");
        foreach (var p in providers)
        {
            var name = System.Net.WebUtility.HtmlEncode(p.DisplayName);
            var providerAttr = System.Net.WebUtility.HtmlEncode(p.ProviderId);
            var href = $"{prefix}/sso/OIDC/Start/{System.Net.WebUtility.UrlEncode(p.ProviderId)}";
            html.Append("  <a class=\"raised button-submit block emby-button oidc-sso-btn\" data-provider=\"")
                .Append(providerAttr)
                .Append("\" href=\"")
                .Append(href)
                .Append("\">")
                .Append(name)
                .Append("</a>\n");
        }
        html.Append("  <div class=\"oidc-sso-sep\">— or sign in with password —</div>\n");
        html.Append("</div>\n");
        html.Append(HtmlEnd);

        var css = new StringBuilder();
        css.Append(CssStart).Append('\n');
        // Pull the buttons above the login form so "— or sign in with password —" reads
        // correctly. The disclaimer's container varies between Jellyfin versions, so target the
        // likely candidates; :has scopes each rule to pages that actually carry our block.
        css.Append(".loginPage .padded-left:has(#oidc-sso-buttons),\n");
        css.Append(".disclaimerContainer:has(#oidc-sso-buttons){display:flex;flex-direction:column}\n");
        css.Append("#oidc-sso-buttons{order:-1;margin:1em 0}\n");
        // Full width + white label to match the native submit button; #id beats theme rules
        // without !important. Shape/height/radius/hover still come from the native classes.
        css.Append("#oidc-sso-buttons a.oidc-sso-btn{display:block;width:100%;margin:.5em 0;");
        css.Append("color:#fff;text-decoration:none;text-align:center}\n");
        foreach (var p in providers)
        {
            var brand = CustomBrandColor(p.ButtonColor);
            if (brand == null)
            {
                continue;
            }

            // background-image:none clears any theme gradient so the solid colour shows.
            css.Append("#oidc-sso-buttons a[data-provider=\"")
                .Append(CssEscape(p.ProviderId))
                .Append("\"]{background-color:")
                .Append(brand)
                .Append(";background-image:none}\n");
        }
        css.Append("#oidc-sso-buttons .oidc-sso-sep{margin:1em 0;opacity:.7;text-align:center}\n");
        css.Append(CssEnd);

        return (html.ToString(), css.ToString());
    }

    // Escapes a provider id for safe use inside a CSS attribute-selector string.
    // Provider ids are admin-set and normally [a-z0-9-]; this is defence in depth.
    private static string CssEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
