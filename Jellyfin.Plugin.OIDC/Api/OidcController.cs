using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Data.Queries;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.OIDC.Api;

[ApiController]
[Route("sso/OIDC")]
public class OidcController : ControllerBase
{
    // BuildCallbackHtml's inline script writes these jellyfin-web localStorage keys directly; there's
    // no public "adopt this token" API. Undocumented internals; verified against jellyfin-web 12.0
    // (this plugin's targetAbi). Re-check both keys and AppVersion on the next Jellyfin bump.
    private const string JellyfinWebDeviceIdStorageKey = "_deviceId2";
    private const string JellyfinWebCredentialsStorageKey = "jellyfin_credentials";
    private const string JellyfinWebAppName = "Jellyfin Web";
    private const string JellyfinWebAppVersion = "12.0.0";

    private readonly StateManager _stateManager;
    private readonly UserSyncService _userSyncService;
    private readonly UserProviderMapStore _mapStore;
    private readonly ISessionManager _sessionManager;
    private readonly IDeviceManager _deviceManager;
    private readonly IQuickConnect _quickConnect;
    private readonly OidcProtocolService _protocol;
    private readonly LoginFlowService _loginFlow;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<OidcController> _logger;

    public OidcController(
        StateManager stateManager,
        UserSyncService userSyncService,
        UserProviderMapStore mapStore,
        ISessionManager sessionManager,
        IDeviceManager deviceManager,
        IQuickConnect quickConnect,
        OidcProtocolService protocol,
        LoginFlowService loginFlow,
        IServerApplicationHost appHost,
        ILogger<OidcController> logger)
    {
        _stateManager = stateManager;
        _userSyncService = userSyncService;
        _mapStore = mapStore;
        _sessionManager = sessionManager;
        _deviceManager = deviceManager;
        _quickConnect = quickConnect;
        _protocol = protocol;
        _loginFlow = loginFlow;
        _appHost = appHost;
        _logger = logger;
    }

    [HttpGet("Start/{providerId}")]
    [RateLimit("oidc-start", maxRequests: 20, windowSeconds: 60)]
    public Task<ActionResult> Start(string providerId)
    {
        return BeginAuthorizeAsync(providerId, quickConnect: false);
    }

    /// Entry point for a native/mobile app: user authenticates here, then enters the code shown by their app.
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

        var (disco, failure) = await _protocol.ResolveAndPinDiscoveryAsync(provider, providerId).ConfigureAwait(false);
        if (failure == DiscoveryPinFailure.PinMismatch)
        {
            return StatusCode(502, "Identity provider endpoint mismatch detected. Re-run Test Connection in the plugin admin UI.");
        }

        if (disco == null)
        {
            return StatusCode(502, "Failed to contact identity provider");
        }

        LogDiscoveryPreflightWarnings(disco, provider, providerId);

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
            extra: ParseAdditionalParameters(provider.AdditionalParameters, providerId));

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

        var outcome = await _loginFlow.CompleteCallbackAsync(provider, providerId, oidcState, code).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.FailureStatusCode == 400
                ? BadRequest(outcome.FailureMessage)
                : StatusCode(outcome.FailureStatusCode, outcome.FailureMessage);
        }

        // Lets the CSP authorize the inline script/styles below without 'unsafe-inline'.
        var cspNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Response.Headers["X-Frame-Options"] = "DENY";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // The page body carries the one-time session token - never cache it anywhere.
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Content-Security-Policy"] =
            $"default-src 'none'; script-src 'nonce-{cspNonce}'; style-src 'nonce-{cspNonce}'; connect-src 'self'; frame-ancestors 'none'";

        return Content(
            outcome.QuickConnect
                ? BuildQuickConnectHtml(outcome.SessionToken!, providerId, cspNonce)
                : BuildCallbackHtml(outcome.SessionToken!, providerId, cspNonce),
            "text/html");
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

        if (GetProvider(providerId) == null)
        {
            return NotFound($"Provider '{providerId}' not found or disabled");
        }

        try
        {
            var userId = await _userSyncService.SyncUserAsync(
                session.Username, session.DisplayName, session.Subject, session.Email, session.EmailVerified, session.ProviderId).ConfigureAwait(false);

            // RBAC must apply before AuthenticateDirect mints the session token.
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

            TrackMintedSession(session, authRequest.DeviceId!, authResult.SessionInfo?.Id, userId);
            _logger.LogInformation(
                "OIDC audit: decision=login provider={Provider} subject={Subject} user={User} device={Device}",
                session.ProviderId, ClaimParser.RedactSubject(session.Subject), session.Username, authRequest.DeviceId);

            return Ok(authResult);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=sync-{Reason}",
                session.ProviderId, ClaimParser.RedactSubject(session.Subject), session.Username, ex.Message);
            return Forbid();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Authentication failed for user {Username}", session.Username);
            return StatusCode(500, "Authentication failed");
        }
    }

    /// Provisions/syncs the user from the OIDC login, then authorizes their device's Quick Connect code.
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

        if (GetProvider(providerId) == null)
        {
            return NotFound($"Provider '{providerId}' not found or disabled");
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

        // Provision + apply RBAC once per session; a mistyped-code retry peeks the same object and skips it.
        Guid userId;
        if (session.SyncedUserId is { } alreadySynced)
        {
            userId = alreadySynced;
        }
        else
        {
            try
            {
                userId = await _userSyncService.SyncUserAsync(
                    session.Username, session.DisplayName, session.Subject, session.Email, session.EmailVerified, session.ProviderId).ConfigureAwait(false);
                await _userSyncService.ApplyRolesAsync(userId, session.Roles, session.ProviderId).ConfigureAwait(false);
                await _userSyncService.ApplyProfileImageAsync(userId, session.PictureUrl, session.ProviderId).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("User sync failed during Quick Connect: {Message}", ex.Message);
                return Forbid();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Quick Connect user sync failed for {Username}", session.Username);
                return StatusCode(500, "Quick Connect authorization failed");
            }

            session.SyncedUserId = userId;
        }

        try
        {
            var authorized = await _quickConnect.AuthorizeRequest(userId, code).ConfigureAwait(false);
            if (!authorized)
            {
                return BadRequest("Quick Connect authorization was rejected.");
            }
        }
        catch (MediaBrowser.Common.Extensions.ResourceNotFoundException)
        {
            // Unknown / expired code - keep the session valid so the user can retry.
            return BadRequest("That code wasn't recognized. Check the code on your device and try again.");
        }
        catch (MediaBrowser.Controller.Authentication.AuthenticationException)
        {
            return BadRequest("Quick Connect is not active on this server.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Safety net for anything Quick Connect throws that the two filters above don't name.
            _logger.LogError(ex, "Quick Connect authorization failed for user {Username}", session.Username);
            return StatusCode(500, "Quick Connect authorization failed");
        }

        // Success - invalidate the one-time session so the token can't be replayed.
        _stateManager.InvalidateAuthorizedSession(request.Token);
        _logger.LogInformation(
            "OIDC audit: decision=login provider={Provider} subject={Subject} user={User} channel=quickconnect",
            providerId, ClaimParser.RedactSubject(session.Subject), session.Username);

        return Ok(new { success = true });
    }

    /// OIDC Back-Channel Logout 1.0: a valid <c>logout_token</c> revokes by <c>sid</c> (one device) or <c>sub</c> (all of the user's).
    [HttpPost("BackchannelLogout/{providerId}")]
    [Consumes("application/x-www-form-urlencoded")]
    [RateLimit("oidc-callback", maxRequests: 10, windowSeconds: 60)]
    public async Task<ActionResult> BackchannelLogout(string providerId, [FromForm(Name = "logout_token")] string? logoutToken)
    {
        if (string.IsNullOrWhiteSpace(logoutToken))
        {
            return BadRequest(new { error = "invalid_request", error_description = "logout_token missing" });
        }

        var provider = GetProvider(providerId);
        if (provider == null)
        {
            return BadRequest(new { error = "invalid_request", error_description = "unknown provider" });
        }

        var (disco, _) = await _protocol.ResolveAndPinDiscoveryAsync(provider, providerId).ConfigureAwait(false);
        if (disco == null)
        {
            return StatusCode(502, new { error = "server_error" });
        }

        var signingKeys = await _protocol.ResolveSigningKeysAsync(disco, provider, providerId).ConfigureAwait(false);
        if (signingKeys == null)
        {
            return StatusCode(502, new { error = "server_error" });
        }

        var token = OidcProtocolService.ValidateSignedJwt(logoutToken, disco.Issuer, provider.ClientId, signingKeys, out _, out var logoutTokenError);
        if (token == null)
        {
            _logger.LogWarning("OIDC back-channel logout token invalid for {Provider}: {Message}", providerId, logoutTokenError);
            return BadRequest(new { error = "invalid_request", error_description = "logout_token validation failed" });
        }

        var claimError = ValidateLogoutTokenClaims(token);
        if (claimError != null)
        {
            return BadRequest(new { error = "invalid_request", error_description = claimError });
        }

        var sub = ClaimParser.ExtractClaim(token, "sub");
        var sid = ClaimParser.ExtractClaim(token, "sid");

        // Optional (some IdPs omit it); when present, blocks replay for the token's full validity window.
        var jti = ClaimParser.ExtractClaim(token, "jti");
        if (!string.IsNullOrEmpty(jti))
        {
            var forgetAfter = new DateTimeOffset(DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc))
                + TimeSpan.FromMinutes(5);
            if (!_stateManager.RegisterJti(jti, forgetAfter))
            {
                return BadRequest(new { error = "invalid_request", error_description = "replayed logout_token" });
            }
        }

        var issuer = disco.Issuer ?? provider.Authority;

        // FindTracked self-enforces sid-over-sub precedence - a sid-scoped logout never widens to
        // every session sharing the subject, even though both are passed through unmodified here.
        var tracked = _stateManager.FindTracked(issuer, sub, sid);
        var (revokedDevices, deviceRevocationErrored) = await RevokeTrackedDevicesAsync(tracked).ConfigureAwait(false);

        // A tracked-but-deviceless session (e.g. the device row was already removed) must NOT fall
        // through to the mapped-user fallback below - widening to every session for the user would
        // violate the sid-scoping guarantee above.
        var fallback = tracked.Count == 0
            ? await RevokeViaMappedUserAsync(providerId, sub, sid, token, provider).ConfigureAwait(false)
            : LogoutFallbackOutcome.NotAttempted;

        var revoked = revokedDevices > 0 || fallback == LogoutFallbackOutcome.Revoked;
        var errored = deviceRevocationErrored || fallback == LogoutFallbackOutcome.Errored;

        if (!revoked && errored)
        {
            // We matched a session but every revocation attempt errored. Free the jti so the IdP's
            // retry of this same logout_token isn't rejected as a replay, and signal a retry.
            _stateManager.UnregisterJti(jti);
            _logger.LogError(
                "OIDC back-channel logout for {Provider} matched a session but revoked nothing (sub={Subject} sid={Sid}); "
                + "returning 503 so the IdP retries.",
                providerId, ClaimParser.RedactSubject(sub), sid);
            return StatusCode(503, new { error = "temporarily_unavailable" });
        }

        if (!revoked && !errored && fallback == LogoutFallbackOutcome.NoMatch)
        {
            _logger.LogWarning(
                "OIDC back-channel logout for {Provider} matched no session (sub={Subject} sid={Sid}); nothing "
                + "was revoked. State may have been lost across a restart, or the account predates subject-keyed "
                + "identity and hasn't logged in since - the IdP still gets a 200 per spec.",
                providerId, ClaimParser.RedactSubject(sub), sid);
        }

        _logger.LogInformation(
            "OIDC audit: decision=backchannel-logout provider={Provider} subject={Subject} sid={Sid} revoked={Revoked}",
            providerId, ClaimParser.RedactSubject(sub), sid,
            fallback == LogoutFallbackOutcome.Revoked ? "all" : revokedDevices.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return Ok();
    }

    /// Deletes every device behind each tracked session, untracking it either way; returns how many
    /// devices were revoked and whether any deletion attempt errored.
    private async Task<(int RevokedDevices, bool Errored)> RevokeTrackedDevicesAsync(IReadOnlyList<TrackedSession> tracked)
    {
        var revokedDevices = 0;
        var errored = false;
        foreach (var t in tracked)
        {
            try
            {
                var devices = _deviceManager.GetDevices(new DeviceQuery { UserId = t.UserId, DeviceId = t.DeviceId });
                foreach (var device in devices.Items)
                {
                    await _deviceManager.DeleteDevice(device).ConfigureAwait(false);
                    revokedDevices++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errored = true;
                _logger.LogWarning(ex, "OIDC back-channel logout: failed to delete device {Device}", t.DeviceId);
            }

            _stateManager.UntrackBySessionId(t.SessionId);
        }

        return (revokedDevices, errored);
    }

    private enum LogoutFallbackOutcome { NotAttempted, Revoked, NoMatch, Errored }

    /// Only reached when nothing was tracked in memory (Quick Connect session, or state lost on
    /// restart): resolves the logout token to a mapped user and revokes every one of their sessions.
    private async Task<LogoutFallbackOutcome> RevokeViaMappedUserAsync(
        string providerId, string sub, string? sid, JwtSecurityToken token, OidcProviderConfig provider)
    {
        var username = ClaimParser.ExtractClaim(token, provider.UsernameClaim);
        var entry = _mapStore.ResolveForLogout(providerId, sub, sid, username);
        if (entry == null)
        {
            return LogoutFallbackOutcome.NoMatch;
        }

        // A fully-legacy row stores no UserId - fall back to the live session.
        var userId = Guid.TryParse(entry.UserId, out var parsed)
            ? parsed
            : _sessionManager.Sessions
                .FirstOrDefault(s => string.Equals(s.UserName, entry.Username, StringComparison.OrdinalIgnoreCase))
                ?.UserId ?? Guid.Empty;
        if (userId == Guid.Empty)
        {
            return LogoutFallbackOutcome.NoMatch;
        }

        if (!string.IsNullOrEmpty(sid))
        {
            // UserProviderMapStore rows carry no DeviceId, and ISessionManager.RevokeUserTokens has
            // no sid/device-scoped overload - so this sid-scoped logout_token, having matched no
            // in-memory TrackedSession (restart, or a session older than this plugin version), is
            // about to revoke every session for the user rather than just the one the IdP named.
            _logger.LogWarning(
                "OIDC back-channel logout for {Provider} (sid={Sid}) matched no in-memory session; "
                + "falling back to revoking every session for user {UserId}, wider than the sid-scoped "
                + "request. See SECURITY.md for this known limitation.",
                providerId, sid, userId);
        }

        try
        {
            await _sessionManager.RevokeUserTokens(userId, string.Empty).ConfigureAwait(false);
            return LogoutFallbackOutcome.Revoked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OIDC back-channel logout: failed to revoke tokens for user {UserId}", userId);
            return LogoutFallbackOutcome.Errored;
        }
    }

    [HttpGet("Providers")]
    [RateLimit("oidc-providers", maxRequests: 60, windowSeconds: 60)]
    public ActionResult GetProviders()
    {
        var config = OidcPlugin.CurrentConfig;

        // GetSmartApiUrl omits a reverse-proxy base path (Networking > Base URL), so append it.
        var baseUrl = _appHost.GetSmartApiUrl(Request).TrimEnd('/') + Request.PathBase;
        var providers = config.EnabledProviders
            .Select(p => new
            {
                p.ProviderId,
                p.DisplayName,
                // Anonymous endpoint - sanitize the same way the login-button snippet does.
                ButtonColor = ProviderButtonAssets.CustomBrandColor(p.ButtonColor),
                ButtonIcon = ProviderButtonAssets.IconDataUri(p.ButtonIcon),
                StartUrl = CombineRoute(baseUrl, $"sso/OIDC/Start/{p.ProviderId}")
            });

        return Ok(providers);
    }

    private static OidcProviderConfig? GetProvider(string providerId)
        => OidcPlugin.CurrentConfig.FindProvider(providerId) is { Enabled: true } p ? p : null;

    /// Correlates a new session with its OIDC identity so a later back-channel logout can target it.
    private void TrackMintedSession(AuthorizedSession session, string deviceId, string? sessionId, Guid userId)
    {
        if (string.IsNullOrEmpty(session.Subject) || string.IsNullOrEmpty(session.Issuer))
        {
            return;
        }

        _stateManager.TrackSession(new TrackedSession
        {
            ProviderId = session.ProviderId,
            Issuer = session.Issuer,
            Subject = session.Subject,
            Sid = session.Sid,
            UserId = userId,
            DeviceId = deviceId,
            SessionId = sessionId ?? deviceId
        });

        // Persist the sid on the map row so a post-restart sid-only back-channel logout still resolves
        // the user. Debounced in the store, so sid rotation every login doesn't mean a write every login.
        if (!string.IsNullOrEmpty(session.Sid) && !string.IsNullOrEmpty(session.Subject))
        {
            _mapStore.SetLogoutSid(session.ProviderId, session.Subject, session.Sid);
        }
    }

    /// OIDC Back-Channel Logout 1.0 §2.4 claim rules (no nonce, correct event, sub or sid present).
    private static string? ValidateLogoutTokenClaims(JwtSecurityToken token)
    {
        if (token.Claims.Any(c => c.Type == "nonce"))
        {
            return "nonce prohibited in logout_token";
        }

        var events = ClaimParser.ExtractClaim(token, "events");
        if (!events.Contains("http://schemas.openid.net/event/backchannel-logout", StringComparison.Ordinal))
        {
            return "missing back-channel logout event";
        }

        var sub = ClaimParser.ExtractClaim(token, "sub");
        var sid = ClaimParser.ExtractClaim(token, "sid");
        if (string.IsNullOrEmpty(sub) && string.IsNullOrEmpty(sid))
        {
            return "sub or sid required";
        }

        return null;
    }

    private string BuildRedirectUri(OidcProviderConfig provider)
    {
        var serverBaseUrl = OidcPlugin.CurrentConfig.ServerBaseUrl;
        var overrideOk = !string.IsNullOrWhiteSpace(serverBaseUrl)
            && Uri.TryCreate(serverBaseUrl, UriKind.Absolute, out _);
        if (!overrideOk && !string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            _logger.LogWarning(
                "OIDC: ServerBaseUrl '{ServerBaseUrl}' is not an absolute URL; "
                + "falling back to the auto-detected server address for redirect_uri.",
                serverBaseUrl);
        }

        var baseUrl = overrideOk ? serverBaseUrl : _appHost.GetSmartApiUrl(Request);

        return CombineRoute(baseUrl, $"sso/OIDC/Callback/{provider.ProviderId}");
    }

    /// <summary>
    /// Joins a server base URL (any trailing slash, optionally carrying a reverse-proxy base
    /// path) with a plugin-relative route via <see cref="Uri"/> composition rather than string
    /// concatenation, so a missing or doubled '/' can't slip into a redirect URI. The only
    /// normalization is lower-casing the host and dropping a default port; every IdP applies
    /// the same rules when matching a registered redirect URI, so the result still matches.
    /// </summary>
    private static string CombineRoute(string baseUrl, string relativeRoute)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, relativeRoute).AbsoluteUri;
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncoder.Encode(hash);
    }

    /// <summary>
    /// Best-effort sanity check on the discovery document before the authorize redirect: warns
    /// (never blocks) when the IdP advertises a PKCE method set without <c>S256</c>, or omits a
    /// requested scope from <c>scopes_supported</c>. An incomplete discovery document is common
    /// and must not lock out a working setup, so this only logs. Mirrors the scope check in
    /// <see cref="ConfigController.TestProvider"/>.
    /// </summary>
    private void LogDiscoveryPreflightWarnings(DiscoveryDocumentResponse disco, OidcProviderConfig provider, string providerId)
    {
        var pkceMethods = disco.CodeChallengeMethodsSupported?.ToList();
        if (pkceMethods is { Count: > 0 }
            && !pkceMethods.Contains(OidcConstants.CodeChallengeMethods.Sha256, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "OIDC preflight: provider {Provider} advertises code_challenge_methods_supported [{Methods}] without \"S256\"; "
                + "the plugin only sends S256, so the authorization request may be rejected.",
                providerId, string.Join(", ", pkceMethods));
        }

        var missing = OidcProtocolService.MissingScopes(disco, provider.Scopes);
        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "OIDC preflight: provider {Provider} requests scope(s) not in scopes_supported: {Missing}",
                providerId, string.Join(", ", missing));
        }
    }

    private static string BuildCsrfCookieName(string stateKey) => $"oidc_csrf.{stateKey}";

    private static bool VerifyCsrfToken(string? cookieValue, string expectedToken) =>
        !string.IsNullOrEmpty(cookieValue) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(cookieValue),
            Encoding.UTF8.GetBytes(expectedToken));

    private Parameters? ParseAdditionalParameters(string? raw, string providerId)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Admin free-text in query-string form. ParseQueryString owns the '&' / '=' / percent
        // decoding; it files a token with no '=' under a null key and an empty key ("=x") under
        // "", so we can still warn about a likely typo. Unlike a bare Uri.UnescapeDataString this
        // also decodes '+' to space and trims surrounding whitespace - both the conventional
        // reading of a query string.
        var parsed = System.Web.HttpUtility.ParseQueryString(raw);

        var kept = new List<KeyValuePair<string, string>>();
        var dropped = new List<string>();

        foreach (var key in parsed.AllKeys)
        {
            var values = parsed.GetValues(key) ?? [];
            if (string.IsNullOrWhiteSpace(key))
            {
                dropped.AddRange(values);
                continue;
            }

            foreach (var value in values)
            {
                kept.Add(new KeyValuePair<string, string>(key.Trim(), (value ?? string.Empty).Trim()));
            }
        }

        if (dropped.Count > 0)
        {
            _logger.LogWarning(
                "OIDC provider {Provider}: ignored malformed Additional Parameters (expected '&'-separated key=value): {Dropped}",
                providerId, string.Join(", ", dropped));
        }

        return kept.Count > 0 ? new Parameters(kept) : null;
    }

    /// <summary>
    /// The interactive-login landing page: its inline script trades the one-time
    /// <paramref name="sessionToken"/> for a Jellyfin session and writes jellyfin-web's
    /// localStorage credentials. Markup lives in <c>Configuration/CallbackPage.html</c>; the
    /// script's inputs are handed over as a JSON data island rather than spliced into the code.
    /// </summary>
    private static string BuildCallbackHtml(string sessionToken, string providerId, string cspNonce)
        => EmbeddedPage.Render("CallbackPage.html", new Dictionary<string, string>
        {
            ["__CSP_NONCE__"] = cspNonce,
            ["__DATA_JSON__"] = SerializePageData(new
            {
                token = sessionToken,
                providerId,
                deviceIdKey = JellyfinWebDeviceIdStorageKey,
                credentialsKey = JellyfinWebCredentialsStorageKey,
                appName = JellyfinWebAppName,
                appVersion = JellyfinWebAppVersion
            })
        });

    /// <summary>
    /// The Quick Connect landing page: prompts for the device's code and POSTs it with the
    /// one-time <paramref name="sessionToken"/>. Markup lives in
    /// <c>Configuration/QuickConnectPage.html</c>; inputs travel as a JSON data island.
    /// </summary>
    private static string BuildQuickConnectHtml(string sessionToken, string providerId, string cspNonce)
        => EmbeddedPage.Render("QuickConnectPage.html", new Dictionary<string, string>
        {
            ["__CSP_NONCE__"] = cspNonce,
            ["__DATA_JSON__"] = SerializePageData(new
            {
                token = sessionToken,
                providerId
            })
        });

    /// <summary>
    /// Serializes the value dropped into a page's <c>&lt;script type="application/json"&gt;</c>
    /// data island. The default <see cref="System.Text.Json"/> encoder escapes <c>&lt;</c>,
    /// <c>&gt;</c> and <c>&amp;</c> to <c>\uXXXX</c>, so no field value - a provider ID included -
    /// can spell <c>&lt;/script&gt;</c> and break out of the element.
    /// </summary>
    private static string SerializePageData<T>(T data)
        => System.Text.Json.JsonSerializer.Serialize(data);
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
