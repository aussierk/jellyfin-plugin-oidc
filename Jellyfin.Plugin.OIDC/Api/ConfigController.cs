using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Globalization;
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
        "Unable to retrieve a discovery document from the given Issuer URL. Check the URL and try again; see the server log for details.";

    private readonly RbacService _rbacService;
    private readonly ILocalizationManager _localization;
    private readonly OidcProtocolService _protocol;
    private readonly UserProviderMapStore _mapStore;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(
        RbacService rbacService,
        ILocalizationManager localization,
        OidcProtocolService protocol,
        UserProviderMapStore mapStore,
        ILogger<ConfigController> logger)
    {
        _rbacService = rbacService;
        _localization = localization;
        _protocol = protocol;
        _mapStore = mapStore;
        _logger = logger;
    }

    [HttpGet("Libraries")]
    public ActionResult<Dictionary<string, string>> GetLibraries()
    {
        return Ok(_rbacService.GetAvailableLibraries());
    }

    /// The parental ratings this server knows (from its configured metadata country), for the
    /// role-mapping "Max Parental Rating" dropdown. Name is what the mapping stores; Score/SubScore
    /// are informational (ordering + display).
    [HttpGet("Ratings")]
    public ActionResult GetRatings()
    {
        var ratings = _localization.GetParentalRatings()
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(r => new
            {
                r.Name,
                Score = r.RatingScore?.Score,
                SubScore = r.RatingScore?.SubScore
            })
            .OrderBy(r => r.Score ?? int.MaxValue)
            .ThenBy(r => r.SubScore ?? int.MaxValue)
            .ToList();

        return Ok(ratings);
    }

    [HttpGet("Status")]
    public ActionResult GetStatus()
    {
        var config = OidcPlugin.CurrentConfig;
        return Ok(new
        {
            PluginVersion = OidcPlugin.Instance?.Version?.ToString() ?? "unknown",
            ProviderCount = config.Providers.Count,
            RoleMappingCount = config.RoleMappings.Count,
            IdentityMapCount = _mapStore.Count,
            EnabledProviders = config.Providers.Where(p => p.Enabled).Select(p => p.DisplayName).ToList()
        });
    }

    [HttpPost("TestProvider")]
    public async Task<ActionResult> TestProvider([FromBody] ProviderTestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Authority))
        {
            return Ok(new { Success = false, Error = "Issuer URL is required" });
        }

        var (disco, blockReason) = await _protocol.FetchDiscoveryAsync(
            request.Authority, request.AllowLoopbackAuthority, request.AllowLinkLocalAuthority).ConfigureAwait(false);
        if (blockReason != null)
        {
            _logger.LogWarning("TestProvider blocked Authority {Authority}: {Reason}", request.Authority, blockReason);
            return Ok(new { Success = false, Error = blockReason });
        }

        if (disco!.IsError)
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

        return Ok(new
        {
            Success = true,
            Issuer = disco.Issuer,
            AuthorizationEndpoint = disco.AuthorizeEndpoint,
            TokenEndpoint = disco.TokenEndpoint,
            UserInfoEndpoint = disco.UserInfoEndpoint,
            JwksUri = disco.JwksUri,
            ScopesSupported = disco.ScopesSupported?.ToList() ?? new List<string>(),
            UnsupportedRequestedScopes = OidcProtocolService.MissingScopes(disco, request.Scopes)
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
