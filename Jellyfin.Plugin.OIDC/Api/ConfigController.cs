using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC/Config")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ConfigController : ControllerBase
{
    private const string DiscoveryFailedMessage =
        "Unable to retrieve a discovery document from the given Authority URL. Check the URL and try again; see the server log for details.";

    private readonly RbacService _rbacService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<IPAddress, bool, HttpClient> _pinnedHttpClientFactory;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(
        RbacService rbacService,
        IHttpClientFactory httpClientFactory,
        Func<IPAddress, bool, HttpClient> pinnedHttpClientFactory,
        ILogger<ConfigController> logger)
    {
        _rbacService = rbacService;
        _httpClientFactory = httpClientFactory;
        _pinnedHttpClientFactory = pinnedHttpClientFactory;
        _logger = logger;
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

        var blockPrivateNetworks = OidcPlugin.Instance?.Configuration?.BlockPrivateNetworkAuthorities ?? false;
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            request.Authority,
            request.AllowLoopbackAuthority,
            request.AllowLinkLocalAuthority,
            blockPrivateNetworks).ConfigureAwait(false);
        if (blockReason != null)
        {
            _logger.LogWarning("TestProvider blocked Authority {Authority}: {Reason}", request.Authority, blockReason);
            return Ok(new { Success = false, Error = blockReason });
        }

        // Pin to the exact address the guard just validated — see GetDiscoveryDocumentAsync in
        // OidcController for why (DNS-rebinding TOCTOU).
        using var httpClient = pinnedAddress != null
            ? _pinnedHttpClientFactory(pinnedAddress, true)
            : _httpClientFactory.CreateClient("OidcPlugin");
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
            _logger.LogError(
                "TestProvider discovery failed for {Authority}: {ErrorType} - {Error}",
                request.Authority,
                disco.ErrorType,
                disco.Error);
            return Ok(new
            {
                Success = false,
                Error = DiscoveryFailedMessage
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
    public bool AllowLoopbackAuthority { get; set; }
    public bool AllowLinkLocalAuthority { get; set; }
}
