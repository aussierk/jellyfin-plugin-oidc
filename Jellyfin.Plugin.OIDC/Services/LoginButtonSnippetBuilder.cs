using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.OIDC.Configuration;

namespace Jellyfin.Plugin.OIDC.Services;

/// 
/// Builds the SSO login-button snippet spliced into Jellyfin's Branding settings
/// (Login Disclaimer HTML + Custom CSS). Web client only. Per-provider button colour and
/// icon are sanitized by <see cref="ProviderButtonAssets"/>.
/// 
public static class LoginButtonSnippetBuilder
{
    // Also declared in Configuration/src/branding.js; LoginButtonSnippetMarkerSyncTests fails the build if they drift.
    public const string HtmlStart = "<!-- oidc-sso-buttons:start -->";
    public const string HtmlEnd = "<!-- oidc-sso-buttons:end -->";
    public const string CssStart = "/* oidc-sso-buttons:start */";
    public const string CssEnd = "/* oidc-sso-buttons:end */";

    /// Builds the marker-fenced (Html, Css) pair for the given enabled providers; empty when
    /// there are none. When <paramref name="includeQuickConnect"/> is set, each provider also
    /// gets a small "Sign in a device … (Quick Connect)" link - the caller should pass this
    /// only when Quick Connect is actually enabled on the server, or the link dead-ends.
    public static (string Html, string Css) Build(
        IEnumerable<OidcProviderConfig> enabledProviders,
        string basePath,
        bool hideManualLogin = false,
        string? loginTitle = null,
        string? loginSubtitle = null,
        bool includeQuickConnect = false)
    {
        var providers = enabledProviders?.ToList() ?? new List<OidcProviderConfig>();
        if (providers.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var prefix = basePath ?? string.Empty;

        var html = new StringBuilder();
        html.Append(HtmlStart).Append('\n');
        // readOnlyContent is Jellyfin's own login-button-stack class, so themes style it for free.
        html.Append("<div id=\"oidc-sso-buttons\" class=\"readOnlyContent\">\n");
        if (hideManualLogin)
        {
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
            // target="_self" is best-effort - Jellyfin's sanitizer may still force target="_blank".
            html.Append("  <a class=\"raised button-submit block emby-button oidc-sso-btn\" target=\"_self\" data-provider=\"")
                .Append(providerAttr)
                .Append("\" href=\"")
                .Append(href)
                .Append("\"><span>")
                .Append(name)
                .Append("</span></a>\n");

            if (includeQuickConnect)
            {
                // Browser-driven login for a device showing a Quick Connect code (native/TV apps
                // can't render this page). .fieldDescription is Jellyfin's muted-text class.
                var qcHref = $"{prefix}/sso/OIDC/QuickConnect/{System.Net.WebUtility.UrlEncode(p.ProviderId)}";
                html.Append("  <a class=\"fieldDescription oidc-sso-qc-link\" target=\"_self\" href=\"")
                    .Append(qcHref)
                    .Append("\">Sign in a device with ")
                    .Append(name)
                    .Append(" (Quick Connect)</a>\n");
            }
        }
        html.Append("</div>\n");
        html.Append(HtmlEnd);

        var css = new StringBuilder();
        css.Append(CssStart).Append('\n');
        // Pull the disclaimer to the top of .readOnlyContent so buttons sit under "Sign In".
        css.Append(".readOnlyContent:has(#oidc-sso-buttons){display:flex;flex-direction:column}\n");
        css.Append(".readOnlyContent:has(#oidc-sso-buttons) .loginDisclaimerContainer{order:-1}\n");
        css.Append(".loginDisclaimerContainer:has(#oidc-sso-buttons),\n");
        css.Append(".loginDisclaimerContainer:has(#oidc-sso-buttons) .loginDisclaimer{display:block;width:100%;margin:0;padding:0}\n");
        css.Append("#oidc-sso-buttons{width:100%;margin:0}\n");
        // Jellyfin's .button-link (auto-applied to <a>) collapses these to text-link height; re-assert the box.
        css.Append("#oidc-sso-buttons a.oidc-sso-btn{display:flex;align-items:center;");
        css.Append("justify-content:center;width:100%;box-sizing:border-box;margin:0 0 .25em;");
        css.Append("padding:1.05em 1em;line-height:1;color:#fff;text-decoration:none;text-align:center}\n");
        if (includeQuickConnect)
        {
            // Layout only; #id a.class out-specifies Jellyfin's .button-link, colour stays with .fieldDescription.
            css.Append("#oidc-sso-buttons a.oidc-sso-qc-link{display:block;margin:0 0 .85em;text-align:center;text-decoration:underline}\n");
        }
        foreach (var p in providers)
        {
            var brand = ProviderButtonAssets.CustomBrandColor(p.ButtonColor);
            if (brand != null)
            {
                css.Append("#oidc-sso-buttons a[data-provider=\"")
                    .Append(CssEscape(p.ProviderId))
                    .Append("\"]{background-color:")
                    .Append(brand)
                    .Append(";background-image:none}\n");
            }

            var icon = ProviderButtonAssets.IconDataUri(p.ButtonIcon);
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
            css.Append("#loginPage .manualLoginForm{display:none}\n");
            css.Append("#loginPage .readOnlyContent .btnForgotPassword{display:none}\n");
            css.Append("#oidc-sso-buttons .oidc-sso-title{margin:.25em 0 .35em;text-align:center}\n");
            css.Append("#oidc-sso-buttons .oidc-sso-subtitle{margin:0 auto .9em;max-width:22em;text-align:center}\n");
        }

        css.Append(CssEnd);

        return (html.ToString(), css.ToString());
    }

    private static string CssEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
