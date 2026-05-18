using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC/Config")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ConfigController : ControllerBase
{
    private readonly RbacService _rbacService;
    private readonly IHttpClientFactory _httpClientFactory;

    public ConfigController(RbacService rbacService, IHttpClientFactory httpClientFactory)
    {
        _rbacService = rbacService;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("Libraries")]
    public ActionResult<Dictionary<string, string>> GetLibraries()
    {
        return Ok(_rbacService.GetAvailableLibraries());
    }

    [HttpGet("Status")]
    public ActionResult GetStatus()
    {
        var config = OidcPlugin.Instance?.Configuration;
        return Ok(new
        {
            PluginVersion = OidcPlugin.Instance?.Version?.ToString() ?? "unknown",
            ProviderCount = config?.Providers.Count ?? 0,
            RoleMappingCount = config?.RoleMappings.Count ?? 0,
            EnabledProviders = config?.Providers.Where(p => p.Enabled).Select(p => p.DisplayName).ToList()
                               ?? new List<string>()
        });
    }

    [HttpPost("TestProvider")]
    public async Task<ActionResult> TestProvider([FromBody] ProviderTestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Authority))
        {
            return Ok(new { Success = false, Error = "Authority URL is required" });
        }

        var httpClient = _httpClientFactory.CreateClient("OidcPlugin");
        var disco = await httpClient.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = request.Authority,
            Policy = new DiscoveryPolicy
            {
                ValidateIssuerName = true,
                ValidateEndpoints = false
            }
        }).ConfigureAwait(false);

        if (disco.IsError)
        {
            return Ok(new
            {
                Success = false,
                Error = disco.Error,
                ErrorType = disco.ErrorType.ToString()
            });
        }

        var requestedScopes = (request.Scopes ?? string.Empty)
            .Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        var supportedScopes = disco.ScopesSupported?.ToList() ?? new List<string>();
        var unsupportedScopes = supportedScopes.Count == 0
            ? new List<string>()
            : requestedScopes.Where(s => !supportedScopes.Contains(s)).ToList();

        return Ok(new
        {
            Success = true,
            Issuer = disco.Issuer,
            AuthorizationEndpoint = disco.AuthorizeEndpoint,
            TokenEndpoint = disco.TokenEndpoint,
            UserInfoEndpoint = disco.UserInfoEndpoint,
            JwksUri = disco.JwksUri,
            ScopesSupported = supportedScopes,
            UnsupportedRequestedScopes = unsupportedScopes
        });
    }
}

public class ProviderTestRequest
{
    public string Authority { get; set; } = string.Empty;
    public string? Scopes { get; set; }
}
