using System.Linq;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Controller.QuickConnect;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC")]
public class LoginButtonController : ControllerBase
{
    private readonly IQuickConnect _quickConnect;

    public LoginButtonController(IQuickConnect quickConnect) => _quickConnect = quickConnect;

    // Jellyfin may run under a base path (Networking > Base URL); include it so generated
    // links keep working under a prefixed deployment. Empty PathBase yields root-relative links.
    private string BasePath => Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;

    private static System.Collections.Generic.List<Configuration.OidcProviderConfig> EnabledProviders()
        => OidcPlugin.CurrentConfig.EnabledProviders.ToList();

    private static bool HideManualLogin => OidcPlugin.CurrentConfig.HideManualLogin;

    private static string LoginTitle => OidcPlugin.CurrentConfig.LoginTitle;

    private static string LoginSubtitle => OidcPlugin.CurrentConfig.LoginSubtitle;

    [HttpGet("LoginButtonSnippet")]
    [RateLimit("oidc-providers", maxRequests: 60, windowSeconds: 60)]
    public ActionResult GetLoginButtonSnippet()
    {
        var providers = EnabledProviders();
        if (providers.Count == 0)
        {
            return Ok(new { Html = "", Css = "", Instructions = "No enabled providers configured." });
        }

        var (html, css) = LoginButtonSnippetBuilder.Build(
            providers, BasePath, HideManualLogin, LoginTitle, LoginSubtitle,
            includeQuickConnect: _quickConnect.IsEnabled);

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
