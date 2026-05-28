using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC")]
public class LoginButtonController : ControllerBase
{
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

        var sb = new StringBuilder();
        sb.AppendLine("(function() {");
        sb.AppendLine("  function addButtons() {");
        sb.AppendLine("    var form = document.querySelector('.manualLoginForm, #loginPage form, .loginPage form, [data-page=\"loginPage\"] form, form[action*=\"login\"], #loginPage .padded-left form');");
        sb.AppendLine("    if (!form || document.getElementById('oidc-sso-buttons')) return;");
        sb.AppendLine("    var container = document.createElement('div');");
        sb.AppendLine("    container.id = 'oidc-sso-buttons';");
        sb.AppendLine("    container.style.cssText = 'margin:1em 0;text-align:center;';");

        foreach (var p in providers)
        {
            var escapedName = p.DisplayName.Replace("'", "\\'");
            var escapedColor = p.ButtonColor.Replace("'", "\\'");
            sb.AppendLine($"    var btn_{p.ProviderId} = document.createElement('a');");
            sb.AppendLine($"    btn_{p.ProviderId}.href = '/sso/OIDC/Start/{p.ProviderId}';");
            sb.AppendLine($"    btn_{p.ProviderId}.textContent = 'Sign in with {escapedName}';");
            sb.AppendLine($"    btn_{p.ProviderId}.style.cssText = 'display:block;margin:0.5em auto;padding:0.7em 1.5em;background:{escapedColor};color:#fff;text-decoration:none;border-radius:4px;font-size:1em;max-width:300px;';");
            sb.AppendLine($"    container.appendChild(btn_{p.ProviderId});");
        }

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

        var sb = new System.Text.StringBuilder();
        sb.Append("<div style=\"margin:1em 0;text-align:center;\">");
        foreach (var p in providers)
        {
            var name = System.Net.WebUtility.HtmlEncode(p.DisplayName);
            var color = System.Net.WebUtility.HtmlEncode(p.ButtonColor);
            sb.Append($"<a href=\"/sso/OIDC/Start/{p.ProviderId}\" style=\"display:block;margin:0.5em auto;padding:0.7em 1.5em;background:{color};color:#fff;text-decoration:none;border-radius:4px;font-size:1em;max-width:300px;\">{name}</a>");
        }
        sb.Append("<div style=\"margin:1em 0;color:#888;\">— or sign in with password —</div>");
        sb.Append("</div>");

        return Ok(new { Html = sb.ToString(), Instructions = "Paste the Html value into Jellyfin Dashboard > General > Branding > Login Disclaimer and save." });
    }
}
