using System.Linq;
using System.Text;
using Jellyfin.Plugin.OIDC.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC")]
public class LoginButtonController : ControllerBase
{
    // Jellyfin may run under a base path (Networking > Base URL); include it so generated
    // links keep working under a prefixed deployment. Empty PathBase yields root-relative links.
    private string BasePath => Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;

    private static System.Collections.Generic.List<Configuration.OidcProviderConfig> EnabledProviders()
        => OidcPlugin.Instance?.Configuration?.Providers.Where(p => p.Enabled).ToList()
           ?? new System.Collections.Generic.List<Configuration.OidcProviderConfig>();

    [HttpGet("LoginButtons")]
    public ActionResult GetLoginButtonsScript()
    {
        var providers = EnabledProviders();
        if (providers.Count == 0)
        {
            return Content("", "application/javascript");
        }

        // Reuse the same markup/CSS the Branding snippet uses so both injection paths render
        // identically. The script inserts the container above the form itself, so the CSS's
        // reorder rules are simply a no-op here.
        var (_, css) = BrandingSnippetBuilder.Build(providers, basePath: string.Empty);

        var providerData = providers.Select(p => new { id = p.ProviderId, name = p.DisplayName });
        var providersJson = System.Text.Json.JsonSerializer.Serialize(providerData);
        var cssJson = System.Text.Json.JsonSerializer.Serialize(css);

        var sb = new StringBuilder();
        sb.AppendLine("(function() {");
        sb.AppendLine($"  var providers = {providersJson};");
        sb.AppendLine($"  var css = {cssJson};");
        // The web client is served under '<basePath>/web/', so derive the prefix from the
        // login page URL. Empty when no base path is configured.
        sb.AppendLine("  var _p = window.location.pathname.split('/web/');");
        sb.AppendLine("  var basePath = _p.length > 1 ? _p[0] : '';");
        sb.AppendLine("  function addButtons() {");
        sb.AppendLine("    var form = document.querySelector('.manualLoginForm, #loginPage form, .loginPage form, [data-page=\"loginPage\"] form, form[action*=\"login\"], #loginPage .padded-left form');");
        sb.AppendLine("    if (!form || document.getElementById('oidc-sso-buttons')) return;");
        sb.AppendLine("    if (css && !document.getElementById('oidc-sso-buttons-style')) {");
        sb.AppendLine("      var st = document.createElement('style');");
        sb.AppendLine("      st.id = 'oidc-sso-buttons-style';");
        sb.AppendLine("      st.textContent = css;");
        sb.AppendLine("      document.head.appendChild(st);");
        sb.AppendLine("    }");
        sb.AppendLine("    var container = document.createElement('div');");
        sb.AppendLine("    container.id = 'oidc-sso-buttons';");
        sb.AppendLine("    providers.forEach(function(p) {");
        sb.AppendLine("      var btn = document.createElement('a');");
        sb.AppendLine("      btn.href = basePath + '/sso/OIDC/Start/' + encodeURIComponent(p.id);");
        sb.AppendLine("      btn.textContent = p.name;");
        sb.AppendLine("      btn.className = 'raised button-submit block emby-button oidc-sso-btn';");
        sb.AppendLine("      btn.setAttribute('data-provider', p.id);");
        sb.AppendLine("      container.appendChild(btn);");
        sb.AppendLine("      var qc = document.createElement('a');");
        sb.AppendLine("      qc.href = basePath + '/sso/OIDC/QuickConnect/' + encodeURIComponent(p.id);");
        sb.AppendLine("      qc.textContent = 'Sign in a device with ' + p.name + ' (Quick Connect)';");
        sb.AppendLine("      qc.className = 'oidc-sso-qc';");
        sb.AppendLine("      qc.style.cssText = 'display:block;margin:0.2em auto 0.8em;text-decoration:none;font-size:0.8em;opacity:0.7;';");
        sb.AppendLine("      container.appendChild(qc);");
        sb.AppendLine("    });");
        sb.AppendLine("    var sep = document.createElement('div');");
        sb.AppendLine("    sep.className = 'oidc-sso-sep';");
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
        var providers = EnabledProviders();
        if (providers.Count == 0)
        {
            return Ok(new { Html = "", Css = "", Instructions = "No enabled providers configured." });
        }

        var (html, css) = BrandingSnippetBuilder.Build(providers, BasePath);

        return Ok(new
        {
            Html = html,
            Css = css,
            Instructions =
                "Enable a provider and click Save on the plugin's General tab to add this automatically, "
                + "or paste Html into Dashboard > General > Branding > Login Disclaimer and Css into Custom CSS."
        });
    }
}
