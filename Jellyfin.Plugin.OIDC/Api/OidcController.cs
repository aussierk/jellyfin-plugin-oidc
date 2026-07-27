using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel;
using IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.QuickConnect;
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
    // Per-provider semaphores guard the TOFU endpoint pin read-modify-write against races.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pinLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly StateManager _stateManager;
    private readonly UserSyncService _userSyncService;
    private readonly ISessionManager _sessionManager;
    private readonly IQuickConnect _quickConnect;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<IPAddress, bool, HttpClient> _pinnedHttpClientFactory;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<OidcController> _logger;

    public OidcController(
        StateManager stateManager,
        UserSyncService userSyncService,
        ISessionManager sessionManager,
        IQuickConnect quickConnect,
        IHttpClientFactory httpClientFactory,
        Func<IPAddress, bool, HttpClient> pinnedHttpClientFactory,
        IServerApplicationHost appHost,
        ILogger<OidcController> logger)
    {
        _stateManager = stateManager;
        _userSyncService = userSyncService;
        _sessionManager = sessionManager;
        _quickConnect = quickConnect;
        _httpClientFactory = httpClientFactory;
        _pinnedHttpClientFactory = pinnedHttpClientFactory;
        _appHost = appHost;
        _logger = logger;
    }

    [HttpGet("Start/{providerId}")]
    [RateLimit("oidc-start", maxRequests: 20, windowSeconds: 60)]
    public Task<ActionResult> Start(string providerId)
    {
        return BeginAuthorizeAsync(providerId, quickConnect: false);
    }

    /// <summary>
    /// Entry point for logging in a native/mobile app via Jellyfin Quick Connect. The user opens
    /// this in a browser, authenticates at the IdP, then enters the code shown by their app.
    /// </summary>
    [HttpGet("QuickConnect/{providerId}")]
    [RateLimit("oidc-start", maxRequests: 20, windowSeconds: 60)]
    public Task<ActionResult> QuickConnectStart(string providerId)
    {
        return BeginAuthorizeAsync(providerId, quickConnect: true);
    }

    private async Task<ActionResult> BeginAuthorizeAsync(string providerId, bool quickConnect)
    {
        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return NotFound($"Provider '{providerId}' not found or disabled");
        }

        var blockPrivateNetworks = OidcPlugin.Instance?.Configuration?.BlockPrivateNetworkAuthorities ?? false;
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            provider.Authority,
            provider.AllowLoopbackAuthority,
            provider.AllowLinkLocalAuthority,
            blockPrivateNetworks).ConfigureAwait(false);
        if (blockReason != null)
        {
            _logger.LogError("OIDC discovery blocked for {Provider}: {Reason}", providerId, blockReason);
            return StatusCode(502, "Failed to contact identity provider");
        }

        var disco = await GetDiscoveryDocumentAsync(provider, pinnedAddress).ConfigureAwait(false);
        if (disco.IsError)
        {
            _logger.LogError("OIDC discovery failed for {Provider}: {Error}", providerId, disco.Error);
            return StatusCode(502, "Failed to contact identity provider");
        }

        var pinLock = _pinLocks.GetOrAdd(provider.ProviderId, _ => new SemaphoreSlim(1, 1));
        await pinLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ValidateOrPinEndpoints(provider, disco))
            {
                return StatusCode(502, "Identity provider endpoint mismatch detected. Re-run Test Connection in the plugin admin UI.");
            }
        }
        finally
        {
            pinLock.Release();
        }

        var codeVerifier = CryptoRandom.CreateUniqueId(64);
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var nonce = CryptoRandom.CreateUniqueId(32);
        var csrfToken = CryptoRandom.CreateUniqueId(32);
        var redirectUri = BuildRedirectUri(provider);

        var state = new OidcState
        {
            ProviderId = providerId,
            Nonce = nonce,
            CodeVerifier = codeVerifier,
            RedirectUri = redirectUri,
            CsrfToken = csrfToken,
            QuickConnect = quickConnect
        };

        var stateKey = _stateManager.StoreState(state);
        if (stateKey == null)
        {
            return StatusCode(503, "Server is busy. Please try again in a moment.");
        }

        Response.Cookies.Append(
            BuildCsrfCookieName(stateKey),
            csrfToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/sso/OIDC",
                MaxAge = StateManager.StateExpiry
            });

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
    [RateLimit("oidc-callback", maxRequests: 10, windowSeconds: 60)]
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

        var csrfCookieName = BuildCsrfCookieName(state);
        Request.Cookies.TryGetValue(csrfCookieName, out var csrfCookie);
        Response.Cookies.Delete(csrfCookieName, new CookieOptions { Path = "/sso/OIDC" });

        if (!VerifyCsrfToken(csrfCookie, oidcState.CsrfToken))
        {
            _logger.LogWarning("OIDC CSRF cookie missing or mismatched for provider {Provider}", providerId);
            return BadRequest("Could not verify this browser started the sign-in. Please retry from the same browser/tab.");
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

        var blockPrivateNetworks = OidcPlugin.Instance?.Configuration?.BlockPrivateNetworkAuthorities ?? false;
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            provider.Authority,
            provider.AllowLoopbackAuthority,
            provider.AllowLinkLocalAuthority,
            blockPrivateNetworks).ConfigureAwait(false);
        if (blockReason != null)
        {
            _logger.LogError("OIDC discovery blocked for {Provider}: {Reason}", providerId, blockReason);
            return StatusCode(502, "Failed to contact identity provider");
        }

        var disco = await GetDiscoveryDocumentAsync(provider, pinnedAddress).ConfigureAwait(false);
        if (disco.IsError)
        {
            _logger.LogError("OIDC discovery failed for {Provider}: {Error}", providerId, disco.Error);
            return StatusCode(502, "Failed to contact identity provider");
        }

        var pinLockCallback = _pinLocks.GetOrAdd(provider.ProviderId, _ => new SemaphoreSlim(1, 1));
        await pinLockCallback.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ValidateOrPinEndpoints(provider, disco))
            {
                return StatusCode(502, "Identity provider endpoint mismatch detected. Re-run Test Connection in the plugin admin UI.");
            }
        }
        finally
        {
            pinLockCallback.Release();
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
            _logger.LogError("Token exchange failed for {Provider}: {Error}", providerId, tokenResponse.Error);
            _logger.LogDebug("Token exchange error detail for {Provider}: {Description}", providerId, tokenResponse.ErrorDescription);
            return BadRequest("Token exchange failed. Check plugin logs for details.");
        }

        // Fetch JWKS signing keys and validate token signatures
        var keysResponse = await httpClient.GetJsonWebKeySetAsync(disco.JwksUri).ConfigureAwait(false);
        if (keysResponse.IsError)
        {
            _logger.LogError("JWKS fetch failed for {Provider}: {Error}", providerId, keysResponse.Error);
            return StatusCode(502, "Failed to fetch identity provider signing keys");
        }

        var keySet = new Microsoft.IdentityModel.Tokens.JsonWebKeySet(keysResponse.Raw);
        var signingKeys = keySet.GetSigningKeys();

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();

        var rawIdToken = tokenResponse.IdentityToken ?? tokenResponse.AccessToken;
        if (string.IsNullOrEmpty(rawIdToken))
        {
            return BadRequest("No token in IdP response");
        }

        JwtSecurityToken idToken;
        try
        {
            var idTokenValidation = new TokenValidationParameters
            {
                ValidIssuer = disco.Issuer,
                ValidAudience = provider.ClientId,
                IssuerSigningKeys = signingKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };
            handler.ValidateToken(rawIdToken, idTokenValidation, out var validated);
            idToken = (JwtSecurityToken)validated;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Token validation failed for provider {Provider}: {Message}", providerId, ex.Message);
            return BadRequest("Token validation failed");
        }

        var nonceClaim = idToken.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (string.IsNullOrEmpty(nonceClaim) || nonceClaim != oidcState.Nonce)
        {
            _logger.LogWarning("Nonce mismatch in OIDC callback for provider {Provider}", providerId);
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
        if (roles.Length == 0 && !string.IsNullOrEmpty(tokenResponse.AccessToken) && handler.CanReadToken(tokenResponse.AccessToken))
        {
            try
            {
                var accessTokenValidation = new TokenValidationParameters
                {
                    ValidIssuer = disco.Issuer,
                    IssuerSigningKeys = signingKeys,
                    ValidateIssuer = true,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
                handler.ValidateToken(tokenResponse.AccessToken, accessTokenValidation, out var validatedAccess);
                var accessToken = (JwtSecurityToken)validatedAccess;
                roles = ClaimParser.ExtractRoles(accessToken, provider.RoleClaim);
            }
            catch (SecurityTokenException ex)
            {
                if (provider.StrictAccessTokenValidation)
                {
                    _logger.LogWarning("Access token validation failed for provider {Provider}: {Message}", providerId, ex.Message);
                    return BadRequest("Access token validation failed");
                }

                _logger.LogWarning("Access token signature validation failed for {Provider}; roles from access token skipped", providerId);
            }
        }

        // Optionally extract the profile-image URL (standard OIDC "picture" claim). Like roles,
        // check the ID token first and fall back to the access token, since providers differ
        // in which token carries the claim.
        string? pictureUrl = null;
        if (provider.SyncProfileImage && !string.IsNullOrWhiteSpace(provider.PictureClaim))
        {
            pictureUrl = ClaimParser.ExtractClaim(idToken, provider.PictureClaim);
            if (string.IsNullOrEmpty(pictureUrl) && !string.IsNullOrEmpty(tokenResponse.AccessToken) && handler.CanReadToken(tokenResponse.AccessToken))
            {
                pictureUrl = ClaimParser.ExtractClaim(
                    handler.ReadJwtToken(tokenResponse.AccessToken), provider.PictureClaim);
            }

            // Many providers (e.g. Authentik) expose the picture only via the userinfo
            // endpoint, not in the tokens. Fall back to userinfo when it's in neither token.
            if (string.IsNullOrEmpty(pictureUrl)
                && !string.IsNullOrEmpty(disco.UserInfoEndpoint)
                && !string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                var userInfo = await httpClient.GetUserInfoAsync(new UserInfoRequest
                {
                    Address = disco.UserInfoEndpoint,
                    Token = tokenResponse.AccessToken
                }).ConfigureAwait(false);

                if (userInfo.IsError)
                {
                    _logger.LogWarning("OIDC userinfo request failed for {Provider}: {Error}", providerId, userInfo.Error);
                }
                else
                {
                    pictureUrl = userInfo.Claims
                        .FirstOrDefault(c => c.Type == provider.PictureClaim)?.Value;
                }
            }
        }

        _logger.LogInformation("OIDC auth successful: user={Username}, roles=[{Roles}], provider={Provider}",
            username, string.Join(", ", roles), providerId);

        var sessionToken = _stateManager.StoreAuthorizedSession(new AuthorizedSession
        {
            ProviderId = providerId,
            Username = username,
            DisplayName = displayName,
            PictureUrl = string.IsNullOrEmpty(pictureUrl) ? null : pictureUrl,
            Roles = roles
        });

        if (sessionToken == null)
        {
            return StatusCode(503, "Server is busy. Please try again in a moment.");
        }

        // A per-response nonce lets the CSP require it on the inline script instead of
        // 'unsafe-inline' — pure defense-in-depth, since the script's only dynamic content is
        // already JSON-encoded below.
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = $"default-src 'none'; script-src 'nonce-{nonce}'; connect-src 'self'; frame-ancestors 'none'";

        if (oidcState.QuickConnect)
        {
            return Content(BuildQuickConnectHtml(sessionToken, providerId, nonce), "text/html");
        }

        return Content(BuildCallbackHtml(sessionToken, providerId, nonce), "text/html");
    }

    [HttpPost("Auth/{providerId}")]
    [RateLimit("oidc-auth", maxRequests: 10, windowSeconds: 60)]
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
            var userId = await _userSyncService.SyncUserAsync(session.Username, session.DisplayName, session.ProviderId).ConfigureAwait(false);

            // Apply RBAC via UpdatePolicyAsync BEFORE AuthenticateDirect so that
            // Jellyfin's runtime user state is correct when the session token is minted.
            await _userSyncService.ApplyRolesAsync(userId, session.Roles, session.ProviderId).ConfigureAwait(false);
            await _userSyncService.ApplyProfileImageAsync(userId, session.PictureUrl, session.ProviderId).ConfigureAwait(false);

            var authRequest = new AuthenticationRequest
            {
                App = request.App ?? "Jellyfin Web",
                AppVersion = request.AppVersion ?? "0.0.0",
                DeviceId = request.DeviceId ?? Guid.NewGuid().ToString(),
                DeviceName = request.DeviceName ?? "OIDC",
                UserId = userId
            };

            var authResult = await _sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);

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

    /// <summary>
    /// Authorizes a pending Quick Connect request using the identity established by the OIDC login.
    /// The user is provisioned/synced (same as web login) and then the code shown by their native
    /// app is authorized on their behalf — no pre-existing Jellyfin session required.
    /// </summary>
    [HttpPost("QuickConnect/Authorize/{providerId}")]
    [RateLimit("oidc-auth", maxRequests: 10, windowSeconds: 60)]
    public async Task<ActionResult> QuickConnectAuthorize(
        string providerId,
        [FromBody] QuickConnectAuthorizeRequest request)
    {
        // Peek (not consume) so a mistyped code can be retried without redoing the OIDC login.
        var session = _stateManager.PeekAuthorizedSession(request.Token);
        if (session == null)
        {
            return Unauthorized("Invalid or expired session token");
        }

        if (!string.Equals(session.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Provider mismatch");
        }

        if (!_quickConnect.IsEnabled)
        {
            return BadRequest("Quick Connect is not enabled on this server. An administrator can enable it under Dashboard > General.");
        }

        var code = (request.Code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Missing Quick Connect code");
        }

        Guid userId;
        try
        {
            userId = await _userSyncService.SyncUserAsync(session.Username, session.DisplayName, session.ProviderId).ConfigureAwait(false);
            await _userSyncService.ApplyRolesAsync(userId, session.Roles, session.ProviderId).ConfigureAwait(false);
            await _userSyncService.ApplyProfileImageAsync(userId, session.PictureUrl, session.ProviderId).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("User sync failed during Quick Connect: {Message}", ex.Message);
            return Forbid();
        }

        try
        {
            var authorized = await _quickConnect.AuthorizeRequest(userId, code).ConfigureAwait(false);
            if (!authorized)
            {
                return BadRequest("Quick Connect authorization was rejected.");
            }
        }
        catch (Exception ex) when (ex.GetType().Name == "ResourceNotFoundException")
        {
            // Unknown / expired code — keep the session valid so the user can retry.
            return BadRequest("That code wasn't recognized. Check the code on your device and try again.");
        }
        catch (Exception ex) when (ex.GetType().Name == "AuthenticationException")
        {
            return BadRequest("Quick Connect is not active on this server.");
        }

        // Success — invalidate the one-time session so the token can't be replayed.
        _stateManager.InvalidateAuthorizedSession(request.Token);
        _logger.LogInformation(
            "Quick Connect authorized for user {Username} via provider {Provider}",
            session.Username, providerId);

        return Ok(new { success = true });
    }

    [HttpGet("Providers")]
    public ActionResult GetProviders()
    {
        var config = OidcPlugin.Instance?.Configuration;
        if (config == null)
        {
            return Ok(Array.Empty<object>());
        }

        // GetSmartApiUrl does not include a reverse-proxy base path, so append PathBase
        // (Jellyfin Dashboard > Networking > Base URL) explicitly.
        var baseUrl = _appHost.GetSmartApiUrl(Request).TrimEnd('/') + Request.PathBase;
        var providers = config.Providers
            .Where(p => p.Enabled)
            .Select(p => new
            {
                p.ProviderId,
                p.DisplayName,
                p.ButtonColor,
                p.ButtonIcon,
                StartUrl = $"{baseUrl}/sso/OIDC/Start/{p.ProviderId}"
            });

        return Ok(providers);
    }

    private OidcProviderConfig? GetProvider(string providerId)
    {
        return OidcPlugin.Instance?.Configuration.Providers
            .FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                                 && p.Enabled);
    }

    private bool ValidateOrPinEndpoints(OidcProviderConfig provider, DiscoveryDocumentResponse disco)
    {
        // Treat as unpinned only when all three endpoint fields are empty.
        // Admin-pre-filled pins (PinnedAuthority empty, but Issuer/Token/Jwks set) are respected.
        var unpinned = string.IsNullOrEmpty(provider.PinnedIssuer)
                       && string.IsNullOrEmpty(provider.PinnedTokenEndpoint)
                       && string.IsNullOrEmpty(provider.PinnedJwksUri);

        // Only treat the authority as having changed when there was a previous pinned authority.
        // This prevents overwriting admin-pre-set pins when PinnedAuthority hasn't been written yet.
        var authorityChanged = !string.IsNullOrEmpty(provider.PinnedAuthority)
                               && !string.Equals(provider.Authority, provider.PinnedAuthority, StringComparison.OrdinalIgnoreCase);

        if (unpinned || authorityChanged)
        {
            provider.PinnedAuthority = provider.Authority;
            provider.PinnedIssuer = disco.Issuer ?? string.Empty;
            provider.PinnedTokenEndpoint = disco.TokenEndpoint ?? string.Empty;
            provider.PinnedJwksUri = disco.JwksUri ?? string.Empty;
            OidcPlugin.Instance?.SaveConfiguration();
            _logger.LogInformation("Pinned discovery endpoints for provider {Provider}", provider.ProviderId);
            return true;
        }

        var issuerMatch = string.Equals(disco.Issuer, provider.PinnedIssuer, StringComparison.Ordinal);
        var tokenMatch = string.Equals(disco.TokenEndpoint, provider.PinnedTokenEndpoint, StringComparison.Ordinal);
        var jwksMatch = string.Equals(disco.JwksUri, provider.PinnedJwksUri, StringComparison.Ordinal);

        if (!issuerMatch || !tokenMatch || !jwksMatch)
        {
            _logger.LogError(
                "Discovery endpoint mismatch for {Provider} — expected issuer={Issuer} token={Token} jwks={Jwks}; got issuer={ActualIssuer} token={ActualToken} jwks={ActualJwks}. Pins retained — re-run Test Connection in the admin UI to update them.",
                provider.ProviderId,
                provider.PinnedIssuer, provider.PinnedTokenEndpoint, provider.PinnedJwksUri,
                disco.Issuer, disco.TokenEndpoint, disco.JwksUri);
            return false;
        }

        return true;
    }

    private async Task<DiscoveryDocumentResponse> GetDiscoveryDocumentAsync(OidcProviderConfig provider, IPAddress? pinnedAddress)
    {
        // Pin the connection to the exact address AuthorityGuard already validated, so a
        // DNS-rebinding attacker can't swap in a different (internal) address between the
        // guard check and this request. Falls back to the shared client only when resolution
        // failed naturally (malformed URL/DNS failure) — the fetch is left to fail on its own.
        using var httpClient = pinnedAddress != null
            ? _pinnedHttpClientFactory(pinnedAddress, true)
            : _httpClientFactory.CreateClient("OidcPlugin");
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

    private string BuildRedirectUri(OidcProviderConfig provider)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(provider.ServerBaseUrl)
            ? provider.ServerBaseUrl
            : _appHost.GetSmartApiUrl(Request);

        return $"{baseUrl.TrimEnd('/')}/sso/OIDC/Callback/{provider.ProviderId}";
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncoder.Encode(hash);
    }

    private static string BuildCsrfCookieName(string stateKey) => $"oidc_csrf.{stateKey}";

    private static bool VerifyCsrfToken(string? cookieValue, string expectedToken) =>
        !string.IsNullOrEmpty(cookieValue) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(cookieValue),
            Encoding.UTF8.GetBytes(expectedToken));

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

    private static string BuildCallbackHtml(string sessionToken, string providerId, string nonce)
    {
        // JSON-encode both values so single quotes, backslashes, or other JS metacharacters
        // in admin-configured provider IDs cannot break out of the string literal.
        var tokenJson = System.Text.Json.JsonSerializer.Serialize(sessionToken);
        var providerIdJson = System.Text.Json.JsonSerializer.Serialize(providerId);

        return $$"""
        <!DOCTYPE html>
        <html>
        <head><title>Authenticating...</title></head>
        <body>
        <h3>Completing authentication...</h3>
        <p id="status">Please wait...</p>
        <script nonce="{{nonce}}">
        (function() {
            const token = {{tokenJson}};
            const providerId = {{providerIdJson}};

            // Jellyfin may run under a base path (Networking > Base URL). Derive it from the URL
            // this callback was served at, so every link below keeps the prefix. Empty when unset.
            const basePath = window.location.pathname.replace(/\/sso\/OIDC\/Callback\/[^/]+\/?$/i, '');

            const deviceId = localStorage.getItem('_deviceId2') || crypto.randomUUID();
            localStorage.setItem('_deviceId2', deviceId);

            fetch(basePath + '/sso/OIDC/Auth/' + providerId, {
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
                        ManualAddress: window.location.origin + basePath,
                        AccessToken: auth.AccessToken,
                        UserId: auth.User.Id,
                        DateLastAccessed: Date.now()
                    }]
                };
                localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));

                document.getElementById('status').textContent = 'Success! Redirecting...';
                window.location.href = basePath + '/';
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

    private static string BuildQuickConnectHtml(string sessionToken, string providerId, string nonce)
    {
        // JSON-encode both values so single quotes, backslashes, or other JS metacharacters
        // in admin-configured provider IDs cannot break out of the string literal.
        var tokenJson = System.Text.Json.JsonSerializer.Serialize(sessionToken);
        var providerIdJson = System.Text.Json.JsonSerializer.Serialize(providerId);

        return $$"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Quick Connect</title>
        <style>
            body { font-family: system-ui, -apple-system, sans-serif; background: #101010; color: #eee;
                   display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
            .card { background: #1c1c1c; padding: 2em; border-radius: 8px; max-width: 360px; width: 90%;
                    box-shadow: 0 4px 24px rgba(0,0,0,.5); text-align: center; }
            h2 { margin-top: 0; }
            p { color: #aaa; line-height: 1.5; }
            input { width: 100%; box-sizing: border-box; font-size: 1.6em; letter-spacing: .3em;
                    text-align: center; padding: .5em; margin: .5em 0 1em; border-radius: 4px;
                    border: 1px solid #444; background: #111; color: #fff; }
            button { width: 100%; padding: .8em; font-size: 1em; border: 0; border-radius: 4px;
                     background: #00a4dc; color: #fff; cursor: pointer; }
            button:disabled { opacity: .6; cursor: default; }
            #msg { min-height: 1.4em; margin-top: 1em; }
            .ok { color: #4caf50; }
            .err { color: #f44336; }
        </style>
        </head>
        <body>
        <div class="card">
            <h2>Quick Connect</h2>
            <p>Enter the code shown on your device to finish signing in.</p>
            <input id="code" inputmode="numeric" pattern="[0-9]*" maxlength="6" autocomplete="off"
                   placeholder="000000" aria-label="Quick Connect code">
            <button id="submit">Authorize</button>
            <div id="msg"></div>
        </div>
        <script nonce="{{nonce}}">
        (function() {
            const token = {{tokenJson}};
            const providerId = {{providerIdJson}};
            // Preserve Jellyfin's base path (Networking > Base URL); empty when unset.
            const basePath = window.location.pathname.replace(/\/sso\/OIDC\/Callback\/[^/]+\/?$/i, '');
            const codeInput = document.getElementById('code');
            const button = document.getElementById('submit');
            const msg = document.getElementById('msg');

            codeInput.focus();

            function submit() {
                const code = (codeInput.value || '').trim();
                if (!code) { return; }
                button.disabled = true;
                msg.className = '';
                msg.textContent = 'Authorizing...';

                fetch(basePath + '/sso/OIDC/QuickConnect/Authorize/' + encodeURIComponent(providerId), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ Token: token, Code: code })
                })
                .then(function(r) {
                    return r.text().then(function(body) {
                        if (!r.ok) { throw new Error(body || ('Error ' + r.status)); }
                    });
                })
                .then(function() {
                    msg.className = 'ok';
                    msg.textContent = 'Approved! Return to your device — it should sign in shortly.';
                    codeInput.disabled = true;
                    button.disabled = true;
                })
                .catch(function(err) {
                    msg.className = 'err';
                    msg.textContent = err.message;
                    button.disabled = false;
                    codeInput.focus();
                    codeInput.select();
                });
            }

            button.addEventListener('click', submit);
            codeInput.addEventListener('keydown', function(e) { if (e.key === 'Enter') { submit(); } });
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

public class QuickConnectAuthorizeRequest
{
    public string Token { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
