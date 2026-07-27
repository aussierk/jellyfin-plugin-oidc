using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC")]
public class LoginButtonController : ControllerBase
{
    // Allows only #RGB, #RGBA, #RRGGBB, #RRGGBBAA, and CSS named colors (letters/hyphens only).
    private static readonly Regex _safeCssColor = new(
        @"^(#[0-9a-fA-F]{3,8}|[a-zA-Z\-]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string SanitizeColor(string? color, string fallback = "#4285F4")
        => !string.IsNullOrWhiteSpace(color) && _safeCssColor.IsMatch(color.Trim())
            ? color.Trim()
            : fallback;

    [HttpGet("LoginButtons")]
    public ActionResult GetLoginButtonsScript()
    {
        var config = OidcPlugin.Instance?.Configuration;
        if (config == null)
        {
            return Content("", "application/javascript");
        }

        var providers = config.Providers.Where(p => p.Enabled).ToList();
        if (providers.Count == 0)
        {
            return Content("", "application/javascript");
        }

        var providerData = providers.Select(p => new
        {
            id = p.ProviderId,
            name = p.DisplayName,
            color = SanitizeColor(p.ButtonColor)
        });
        var providersJson = JsonSerializer.Serialize(providerData);

        var sb = new StringBuilder();
        sb.AppendLine("(function() {");
        sb.AppendLine($"  var providers = {providersJson};");
        // Jellyfin may run under a base path (Networking > Base URL). The web client is served
        // under '<basePath>/web/', so derive the prefix from the login page URL. Empty when unset.
        sb.AppendLine("  var _p = window.location.pathname.split('/web/');");
        sb.AppendLine("  var basePath = _p.length > 1 ? _p[0] : '';");
        sb.AppendLine("  function addButtons() {");
        sb.AppendLine("    var form = document.querySelector('.manualLoginForm, #loginPage form, .loginPage form, [data-page=\"loginPage\"] form, form[action*=\"login\"], #loginPage .padded-left form');");
        sb.AppendLine("    if (!form || document.getElementById('oidc-sso-buttons')) return;");
        sb.AppendLine("    var container = document.createElement('div');");
        sb.AppendLine("    container.id = 'oidc-sso-buttons';");
        sb.AppendLine("    container.style.cssText = 'margin:1em 0;text-align:center;';");
        sb.AppendLine("    providers.forEach(function(p) {");
        sb.AppendLine("      var btn = document.createElement('a');");
        sb.AppendLine("      btn.href = basePath + '/sso/OIDC/Start/' + encodeURIComponent(p.id);");
        sb.AppendLine("      btn.textContent = 'Sign in with ' + p.name;");
        sb.AppendLine("      btn.style.cssText = 'display:block;margin:0.5em auto;padding:0.7em 1.5em;background:' + p.color + ';color:#fff;text-decoration:none;border-radius:4px;font-size:1em;max-width:300px;';");
        sb.AppendLine("      container.appendChild(btn);");
        sb.AppendLine("      var qc = document.createElement('a');");
        sb.AppendLine("      qc.href = basePath + '/sso/OIDC/QuickConnect/' + encodeURIComponent(p.id);");
        sb.AppendLine("      qc.textContent = 'Sign in a device with ' + p.name + ' (Quick Connect)';");
        sb.AppendLine("      qc.style.cssText = 'display:block;margin:0.2em auto 0.8em;color:#888;text-decoration:none;font-size:0.8em;max-width:300px;';");
        sb.AppendLine("      container.appendChild(qc);");
        sb.AppendLine("    });");
        sb.AppendLine("    var sep = document.createElement('div');");
        sb.AppendLine("    sep.style.cssText = 'margin:1em 0;text-align:center;color:#888;';");
        sb.AppendLine("    sep.textContent = '— or sign in with password —';");
        sb.AppendLine("    container.appendChild(sep);");
        sb.AppendLine("    form.parentNode.insertBefore(container, form);");
        sb.AppendLine("  }");
        sb.AppendLine("  var observer = new MutationObserver(addButtons);");
        sb.AppendLine("  observer.observe(document.body, { childList: true, subtree: true });");
        sb.AppendLine("  addButtons();");
        sb.AppendLine("})();");

        return Content(sb.ToString(), "application/javascript");
    }

    [HttpGet("BrandingSnippet")]
    public ActionResult GetBrandingSnippet()
    {
        var config = OidcPlugin.Instance?.Configuration;
        var providers = config?.Providers.Where(p => p.Enabled).ToList()
                        ?? new System.Collections.Generic.List<Configuration.OidcProviderConfig>();

        if (providers.Count == 0)
        {
            return Ok(new { Html = "", Instructions = "No enabled providers configured." });
        }

        // Jellyfin may run under a base path (Networking > Base URL); include it so pasted
        // links keep working under a prefixed deployment. Empty PathBase yields root-relative links.
        var basePath = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append("<div style=\"margin:1em 0;text-align:center;\">");
        foreach (var p in providers)
        {
            var name = System.Net.WebUtility.HtmlEncode(p.DisplayName);
            var color = System.Net.WebUtility.HtmlEncode(SanitizeColor(p.ButtonColor));
            var encodedId = System.Net.WebUtility.UrlEncode(p.ProviderId);
            sb.Append($"<a href=\"{basePath}/sso/OIDC/Start/{encodedId}\" style=\"display:block;margin:0.5em auto;padding:0.7em 1.5em;background:{color};color:#fff;text-decoration:none;border-radius:4px;font-size:1em;max-width:300px;\">{name}</a>");
        }
        sb.Append("<div style=\"margin:1em 0;color:#888;\">— or sign in with password —</div>");
        sb.Append("</div>");

        return Ok(new { Html = sb.ToString(), Instructions = "Paste the Html value into Jellyfin Dashboard > General > Branding > Login Disclaimer and save." });
    }
}
