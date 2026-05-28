using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel;
using IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC")]
public class OidcController : ControllerBase
{
    private readonly StateManager _stateManager;
    private readonly UserSyncService _userSyncService;
    private readonly ISessionManager _sessionManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OidcController> _logger;

    public OidcController(
        StateManager stateManager,
        UserSyncService userSyncService,
        ISessionManager sessionManager,
        IHttpClientFactory httpClientFactory,
        ILogger<OidcController> logger)
    {
        _stateManager = stateManager;
        _userSyncService = userSyncService;
        _sessionManager = sessionManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("Start/{providerId}")]
    public async Task<ActionResult> Start(string providerId)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return NotFound($"Provider '{providerId}' not found or disabled");
        }

        var disco = await GetDiscoveryDocumentAsync(provider).ConfigureAwait(false);
        if (disco.IsError)
        {
            _logger.LogError("OIDC discovery failed for {Provider}: {Error}", providerId, disco.Error);
            return StatusCode(502, "Failed to contact identity provider");
        }

        var codeVerifier = CryptoRandom.CreateUniqueId(64);
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var nonce = CryptoRandom.CreateUniqueId(32);
        var redirectUri = BuildRedirectUri(providerId);

        var state = new OidcState
        {
            ProviderId = providerId,
            Nonce = nonce,
            CodeVerifier = codeVerifier,
            RedirectUri = redirectUri
        };

        var stateKey = _stateManager.StoreState(state);

        var authorizeUrl = new RequestUrl(disco.AuthorizeEndpoint!);
        var url = authorizeUrl.CreateAuthorizeUrl(
            clientId: provider.ClientId,
            responseType: OidcConstants.ResponseTypes.Code,
            scope: provider.Scopes,
            redirectUri: redirectUri,
            state: stateKey,
            nonce: nonce,
            codeChallenge: codeChallenge,
            codeChallengeMethod: OidcConstants.CodeChallengeMethods.Sha256,
            extra: ParseAdditionalParameters(provider.AdditionalParameters));

        return Redirect(url);
    }

    [HttpGet("Callback/{providerId}")]
    public async Task<ActionResult> Callback(string providerId, [FromQuery] string code, [FromQuery] string state)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            var error = HttpContext.Request.Query["error"].FirstOrDefault();
            var errorDesc = HttpContext.Request.Query["error_description"].FirstOrDefault();
            _logger.LogWarning("OIDC callback error: {Error} - {Description}", error, errorDesc);
            return BadRequest($"Authentication failed: {error ?? "missing code or state"}");
        }

        var oidcState = _stateManager.ConsumeState(state);
        if (oidcState == null)
        {
            return BadRequest("Invalid or expired authentication state. Please try again.");
        }

        if (!string.Equals(oidcState.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Provider mismatch");
        }

        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return NotFound($"Provider '{providerId}' not found");
        }

        var disco = await GetDiscoveryDocumentAsync(provider).ConfigureAwait(false);
        if (disco.IsError)
        {
            return StatusCode(502, "Failed to contact identity provider");
        }

        var httpClient = _httpClientFactory.CreateClient("OidcPlugin");
        var tokenResponse = await httpClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
        {
            Address = disco.TokenEndpoint,
            ClientId = provider.ClientId,
            ClientSecret = provider.ClientSecret,
            Code = code,
            RedirectUri = oidcState.RedirectUri,
            CodeVerifier = oidcState.CodeVerifier
        }).ConfigureAwait(false);

        if (tokenResponse.IsError)
        {
            _logger.LogError("Token exchange failed: {Error} {Description}",
                tokenResponse.Error, tokenResponse.ErrorDescription);
            return BadRequest("Token exchange failed. Check plugin logs for details.");
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(tokenResponse.IdentityToken ?? tokenResponse.AccessToken))
        {
            return BadRequest("Could not read identity token");
        }

        var idToken = handler.ReadJwtToken(tokenResponse.IdentityToken ?? tokenResponse.AccessToken);

        var nonceClaim = idToken.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (!string.IsNullOrEmpty(oidcState.Nonce) && nonceClaim != oidcState.Nonce)
        {
            _logger.LogWarning("Nonce mismatch in OIDC callback");
            return BadRequest("Token validation failed: nonce mismatch");
        }

        var username = ClaimParser.ExtractClaim(idToken, provider.UsernameClaim);
        if (string.IsNullOrEmpty(username))
        {
            username = ClaimParser.ExtractClaim(idToken, "sub");
        }

        if (string.IsNullOrEmpty(username))
        {
            return BadRequest("Could not determine username from token");
        }

        var displayName = ClaimParser.ExtractClaim(idToken, provider.DisplayNameClaim);

        // Extract roles from both ID token and access token
        var roles = ClaimParser.ExtractRoles(idToken, provider.RoleClaim);
        if (roles.Length == 0 && handler.CanReadToken(tokenResponse.AccessToken))
        {
            var accessToken = handler.ReadJwtToken(tokenResponse.AccessToken);
            roles = ClaimParser.ExtractRoles(accessToken, provider.RoleClaim);
        }

        _logger.LogInformation("OIDC auth successful: user={Username}, roles=[{Roles}], provider={Provider}",
            username, string.Join(", ", roles), providerId);

        var sessionToken = _stateManager.StoreAuthorizedSession(new AuthorizedSession
        {
            ProviderId = providerId,
            Username = username,
            DisplayName = displayName,
            Roles = roles
        });

        return Content(BuildCallbackHtml(sessionToken, providerId), "text/html");
    }

    [HttpPost("Auth/{providerId}")]
    public async Task<ActionResult> Authenticate(
        string providerId,
        [FromBody] AuthenticateRequest request)
    {
        var session = _stateManager.ConsumeAuthorizedSession(request.Token);
        if (session == null)
        {
            return Unauthorized("Invalid or expired session token");
        }

        if (!string.Equals(session.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Provider mismatch");
        }

        try
        {
            var userId = await _userSyncService.SyncUserAsync(session.Username).ConfigureAwait(false);

            var authRequest = new AuthenticationRequest
            {
                App = request.App ?? "Jellyfin Web",
                AppVersion = request.AppVersion ?? "0.0.0",
                DeviceId = request.DeviceId ?? Guid.NewGuid().ToString(),
                DeviceName = request.DeviceName ?? "OIDC",
                UserId = userId
            };

            var authResult = await _sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);

            // Apply RBAC after AuthenticateDirect so Jellyfin's session setup
            // cannot overwrite our permission changes.
            await _userSyncService.ApplyRolesAsync(userId, session.Roles).ConfigureAwait(false);

            return Ok(authResult);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("User sync failed: {Message}", ex.Message);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for user {Username}", session.Username);
            return StatusCode(500, "Authentication failed");
        }
    }

    [HttpGet("Providers")]
    public ActionResult GetProviders()
    {
        var config = OidcPlugin.Instance?.Configuration;
        if (config == null)
        {
            return Ok(Array.Empty<object>());
        }

        var providers = config.Providers
            .Where(p => p.Enabled)
            .Select(p => new
            {
                p.ProviderId,
                p.DisplayName,
                p.ButtonColor,
                p.ButtonIcon,
                StartUrl = $"{Request.Scheme}://{Request.Host}/sso/OIDC/Start/{p.ProviderId}"
            });

        return Ok(providers);
    }

    private OidcProviderConfig? GetProvider(string providerId)
    {
        return OidcPlugin.Instance?.Configuration.Providers
            .FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                                 && p.Enabled);
    }

    private async Task<DiscoveryDocumentResponse> GetDiscoveryDocumentAsync(OidcProviderConfig provider)
    {
        var httpClient = _httpClientFactory.CreateClient("OidcPlugin");
        return await httpClient.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = provider.Authority,
            Policy = new DiscoveryPolicy
            {
                ValidateIssuerName = true,
                ValidateEndpoints = false
            }
        }).ConfigureAwait(false);
    }

    private string BuildRedirectUri(string providerId)
    {
        return $"{Request.Scheme}://{Request.Host}/sso/OIDC/Callback/{providerId}";
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncoder.Encode(hash);
    }

    private static Parameters? ParseAdditionalParameters(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var pairs = raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(
                Uri.UnescapeDataString(p[0].Trim()),
                Uri.UnescapeDataString(p[1].Trim())));

        return new Parameters(pairs);
    }

    private static string BuildCallbackHtml(string sessionToken, string providerId)
    {
        return $$"""
        <!DOCTYPE html>
        <html>
        <head><title>Authenticating...</title></head>
        <body>
        <h3>Completing authentication...</h3>
        <p id="status">Please wait...</p>
        <script>
        (function() {
            const token = '{{sessionToken}}';
            const providerId = '{{providerId}}';

            const deviceId = localStorage.getItem('_deviceId2') || crypto.randomUUID();
            localStorage.setItem('_deviceId2', deviceId);

            fetch('/sso/OIDC/Auth/' + providerId, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    Token: token,
                    DeviceId: deviceId,
                    DeviceName: navigator.userAgent.substring(0, 50),
                    App: 'Jellyfin Web',
                    AppVersion: '10.11.0'
                })
            })
            .then(function(r) {
                if (!r.ok) throw new Error('Auth failed: ' + r.status);
                return r.json();
            })
            .then(function(auth) {
                var credentials = {
                    Servers: [{
                        Id: auth.ServerId,
                        ManualAddress: window.location.origin,
                        AccessToken: auth.AccessToken,
                        UserId: auth.User.Id,
                        DateLastAccessed: Date.now()
                    }]
                };
                localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));

                document.getElementById('status').textContent = 'Success! Redirecting...';
                window.location.href = '/';
            })
            .catch(function(err) {
                document.getElementById('status').textContent = 'Error: ' + err.message;
            });
        })();
        </script>
        </body>
        </html>
        """;
    }
}

public class AuthenticateRequest
{
    public string Token { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? App { get; set; }
    public string? AppVersion { get; set; }
}
