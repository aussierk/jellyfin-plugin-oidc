using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>Outcome of <see cref="OidcProtocolService.ResolveAndPinDiscoveryAsync"/>.</summary>
public enum DiscoveryPinFailure
{
    None,
    Blocked,
    DiscoveryFailed,
    PinMismatch
}

/// <summary>Result of <see cref="OidcProtocolService.InspectAccessToken"/>: the parsed token (when it
/// is a JWT at all), the cryptographically-validated token (when it also passed), and a failure reason.</summary>
public readonly record struct AccessTokenInspection(JwtSecurityToken? Raw, JwtSecurityToken? Validated, string? Error);

/// <summary>
/// Every outbound OIDC-protocol call and the token cryptography that goes with it: SSRF-guarded
/// discovery + TOFU endpoint pinning, guarded per-endpoint clients, JWKS, token exchange, userinfo,
/// and signed-JWT validation. Extracted from <see cref="Api.OidcController"/> so the callback flow,
/// the authorize-start flow, back-channel logout, and the admin Test Connection button all share
/// one implementation. Registered as a singleton - its only mutable state is the per-provider pin
/// lock table, and it must never cache token or userinfo responses.
/// </summary>
public sealed class OidcProtocolService
{
    // Guards the TOFU endpoint pin read-modify-write per provider.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pinLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly GuardedHttpClientFactory _guardedHttp;
    private readonly ILogger<OidcProtocolService> _logger;

    public OidcProtocolService(GuardedHttpClientFactory guardedHttp, ILogger<OidcProtocolService> logger)
    {
        _guardedHttp = guardedHttp;
        _logger = logger;
    }

    /// <summary>
    /// SSRF-guarded discovery fetch with no pinning - for the admin Test Connection path, which
    /// establishes trust rather than enforcing it. Exactly one of the returned members is set:
    /// <c>BlockReason</c> when the guard refused the address, otherwise <c>Disco</c> (whose
    /// <see cref="DiscoveryDocumentResponse.IsError"/> the caller still checks).
    /// </summary>
    public async Task<(DiscoveryDocumentResponse? Disco, string? BlockReason)> FetchDiscoveryAsync(
        string authority, bool allowLoopback, bool allowLinkLocal)
    {
        var guarded = await _guardedHttp.CreateAsync(authority, allowLoopback, allowLinkLocal).ConfigureAwait(false);
        if (guarded.Blocked)
        {
            return (null, guarded.BlockReason);
        }

        using var http = guarded.Client!;
        var disco = await GetDiscoveryAsync(http, authority).ConfigureAwait(false);

        return (disco, null);
    }

    /// SSRF-guarded discovery fetch + TOFU pin check shared by every entry point that needs it.
    public async Task<(DiscoveryDocumentResponse? Disco, DiscoveryPinFailure Failure)> ResolveAndPinDiscoveryAsync(
        OidcProviderConfig provider, string providerId)
    {
        var guarded = await _guardedHttp.CreateAsync(provider.Authority, provider).ConfigureAwait(false);
        if (guarded.Blocked)
        {
            _logger.LogError("OIDC discovery blocked for {Provider}: {Reason}", providerId, guarded.BlockReason);
            return (null, DiscoveryPinFailure.Blocked);
        }

        using var http = guarded.Client!;
        var disco = await GetDiscoveryAsync(http, provider.Authority).ConfigureAwait(false);
        if (disco.IsError)
        {
            _logger.LogError("OIDC discovery failed for {Provider}: {Error}", providerId, disco.Error);
            return (null, DiscoveryPinFailure.DiscoveryFailed);
        }

        var pinLock = _pinLocks.GetOrAdd(provider.ProviderId, _ => new SemaphoreSlim(1, 1));
        await pinLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ValidateOrPinEndpoints(provider, disco))
            {
                return (null, DiscoveryPinFailure.PinMismatch);
            }
        }
        finally
        {
            pinLock.Release();
        }

        return (disco, DiscoveryPinFailure.None);
    }

    // Fresh DiscoveryPolicy per call: GetDiscoveryDocumentAsync writes the parsed authority back onto it.
    private static Task<DiscoveryDocumentResponse> GetDiscoveryAsync(HttpClient http, string authority)
        => http.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = authority,
            Policy = new DiscoveryPolicy
            {
                ValidateIssuerName = true,
                ValidateEndpoints = false
            }
        });

    /// <summary>
    /// Requested scopes the discovery document's <c>scopes_supported</c> does not list. Empty
    /// when the IdP advertises no scope list - an incomplete discovery document must not read
    /// as "everything missing". Shared by the authorize-start preflight and the admin Test
    /// Connection button so both flag the same thing.
    /// </summary>
    public static IReadOnlyList<string> MissingScopes(DiscoveryDocumentResponse disco, string? requestedScopes)
    {
        var supported = disco.ScopesSupported?.ToList();
        if (supported is not { Count: > 0 })
        {
            return Array.Empty<string>();
        }

        return (requestedScopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !supported.Contains(s, StringComparer.Ordinal))
            .ToList();
    }

    public bool ValidateOrPinEndpoints(OidcProviderConfig provider, DiscoveryDocumentResponse disco)
    {
        var unpinned = string.IsNullOrEmpty(provider.PinnedIssuer)
                       && string.IsNullOrEmpty(provider.PinnedTokenEndpoint)
                       && string.IsNullOrEmpty(provider.PinnedJwksUri);

        // Only "changed" if a previous pin exists, so an admin-pre-filled pin isn't overwritten.
        var authorityChanged = !string.IsNullOrEmpty(provider.PinnedAuthority)
                               && !string.Equals(provider.Authority, provider.PinnedAuthority, StringComparison.OrdinalIgnoreCase);

        if (unpinned || authorityChanged)
        {
            provider.PinnedAuthority = provider.Authority;
            provider.PinnedIssuer = disco.Issuer ?? string.Empty;
            provider.PinnedTokenEndpoint = disco.TokenEndpoint ?? string.Empty;
            provider.PinnedJwksUri = disco.JwksUri ?? string.Empty;
            provider.PinnedUserInfoEndpoint = disco.UserInfoEndpoint ?? string.Empty;
            provider.PinnedAuthorizeEndpoint = disco.AuthorizeEndpoint ?? string.Empty;
            OidcPlugin.Instance?.PersistConfiguration();
            _logger.LogInformation("Pinned discovery endpoints for provider {Provider}", provider.ProviderId);
            return true;
        }
        // Back-fill fields added after a provider was already pinned, instead of failing closed.
        var backfilled = false;
        if (string.IsNullOrEmpty(provider.PinnedUserInfoEndpoint) && !string.IsNullOrEmpty(disco.UserInfoEndpoint))
        {
            provider.PinnedUserInfoEndpoint = disco.UserInfoEndpoint;
            backfilled = true;
        }

        if (string.IsNullOrEmpty(provider.PinnedAuthorizeEndpoint) && !string.IsNullOrEmpty(disco.AuthorizeEndpoint))
        {
            provider.PinnedAuthorizeEndpoint = disco.AuthorizeEndpoint;
            backfilled = true;
        }

        if (backfilled)
        {
            OidcPlugin.Instance?.PersistConfiguration();
            _logger.LogInformation("Back-filled newly pinned discovery endpoint(s) for provider {Provider}", provider.ProviderId);
        }

        var issuerMatch = string.Equals(disco.Issuer, provider.PinnedIssuer, StringComparison.Ordinal);
        var tokenMatch = string.Equals(disco.TokenEndpoint, provider.PinnedTokenEndpoint, StringComparison.Ordinal);
        var jwksMatch = string.Equals(disco.JwksUri, provider.PinnedJwksUri, StringComparison.Ordinal);
        var userInfoMatch = string.Equals(disco.UserInfoEndpoint ?? string.Empty, provider.PinnedUserInfoEndpoint, StringComparison.Ordinal);
        var authorizeMatch = string.Equals(disco.AuthorizeEndpoint ?? string.Empty, provider.PinnedAuthorizeEndpoint, StringComparison.Ordinal);

        if (!issuerMatch || !tokenMatch || !jwksMatch || !userInfoMatch || !authorizeMatch)
        {
            _logger.LogError(
                "Discovery endpoint mismatch for {Provider} - expected issuer={Issuer} token={Token} jwks={Jwks} userinfo={UserInfo} authorize={Authorize}; got issuer={ActualIssuer} token={ActualToken} jwks={ActualJwks} userinfo={ActualUserInfo} authorize={ActualAuthorize}. Pins retained - re-run Test Connection in the admin UI to update them.",
                provider.ProviderId,
                provider.PinnedIssuer, provider.PinnedTokenEndpoint, provider.PinnedJwksUri, provider.PinnedUserInfoEndpoint, provider.PinnedAuthorizeEndpoint,
                disco.Issuer, disco.TokenEndpoint, disco.JwksUri, disco.UserInfoEndpoint, disco.AuthorizeEndpoint);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs an endpoint URL from the discovery document (token / JWKS / userinfo) through
    /// <see cref="GuardedHttpClientFactory"/>, tagging the block log with <paramref name="purpose"/>.
    /// Returns a client pinned to the validated address (redirects off), or null with the reason logged.
    /// </summary>
    public async Task<HttpClient?> CreateGuardedEndpointClientAsync(
        string? endpointUrl, OidcProviderConfig provider, string providerId, string purpose)
    {
        var guarded = await _guardedHttp.CreateAsync(endpointUrl, provider).ConfigureAwait(false);
        if (guarded.Blocked)
        {
            _logger.LogError("OIDC {Purpose} endpoint blocked for {Provider}: {Reason}", purpose, providerId, guarded.BlockReason);
            return null;
        }

        return guarded.Client;
    }

    /// <summary>
    /// Exchanges an authorization code for tokens at the (guarded) token endpoint. Returns the raw
    /// <see cref="TokenResponse"/> - including its <see cref="TokenResponse.IsError"/> - so the
    /// caller maps protocol errors; returns <c>null</c> only when the guard blocked the endpoint.
    /// </summary>
    public async Task<TokenResponse?> ExchangeCodeAsync(
        DiscoveryDocumentResponse disco, OidcProviderConfig provider, string providerId,
        string code, string redirectUri, string codeVerifier)
    {
        using var tokenClient = await CreateGuardedEndpointClientAsync(
            disco.TokenEndpoint, provider, providerId, "token").ConfigureAwait(false);
        if (tokenClient == null)
        {
            return null;
        }

        return await tokenClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
        {
            Address = disco.TokenEndpoint,
            ClientId = provider.ClientId,
            ClientSecret = ClientSecretResolver.Resolve(provider, _logger),
            Code = code,
            RedirectUri = redirectUri,
            CodeVerifier = codeVerifier
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches and parses the provider's JWKS, through the same SSRF guard as discovery.
    /// Returns null and logs on a block or fetch error; each caller maps that to its own 502.
    /// </summary>
    public async Task<IList<SecurityKey>?> ResolveSigningKeysAsync(
        DiscoveryDocumentResponse disco, OidcProviderConfig provider, string providerId)
    {
        using var http = await CreateGuardedEndpointClientAsync(disco.JwksUri, provider, providerId, "JWKS").ConfigureAwait(false);
        if (http == null)
        {
            return null;
        }

        var keysResponse = await http.GetJsonWebKeySetAsync(disco.JwksUri).ConfigureAwait(false);
        if (keysResponse.IsError)
        {
            _logger.LogError("JWKS fetch failed for {Provider}: {Error}", providerId, keysResponse.Error);
            return null;
        }

        return new JsonWebKeySet(keysResponse.Raw).GetSigningKeys();
    }

    /// <summary>
    /// Calls the provider's userinfo endpoint with <paramref name="accessToken"/>. Never throws:
    /// a missing endpoint, missing token, guard block, IdP error, or transport exception all yield
    /// <c>null</c> with a warning logged - userinfo is a fallback source, not required for login.
    /// </summary>
    public async Task<UserInfoResponse?> FetchUserInfoAsync(
        DiscoveryDocumentResponse disco, OidcProviderConfig provider, string providerId, string? accessToken)
    {
        if (string.IsNullOrEmpty(disco.UserInfoEndpoint) || string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        try
        {
            using var userInfoClient = await CreateGuardedEndpointClientAsync(
                disco.UserInfoEndpoint, provider, providerId, "userinfo").ConfigureAwait(false);
            if (userInfoClient == null)
            {
                return null;
            }

            var resp = await userInfoClient.GetUserInfoAsync(new UserInfoRequest
            {
                Address = disco.UserInfoEndpoint,
                Token = accessToken
            }).ConfigureAwait(false);

            if (resp.IsError)
            {
                _logger.LogWarning("OIDC userinfo request failed for {Provider}: {Error}", providerId, resp.Error);
                return null;
            }

            return resp;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "OIDC userinfo request threw for {Provider}", providerId);
            return null;
        }
    }

    /// <summary>
    /// Parses and validates a provider's access token for the role-claim fallback: signature +
    /// issuer + lifetime always, audience only when the token itself carries the client id
    /// (Keycloak, RFC 9068 JWT access tokens) so a resource-audienced token (Entra Graph, custom
    /// APIs) still validates. A token that passes but names a different authorized party
    /// (azp / client_id / cid / appid) is rejected - its claims must not feed this login.
    /// </summary>
    public static AccessTokenInspection InspectAccessToken(
        string? rawJwt, string? issuer, string clientId, IEnumerable<SecurityKey> signingKeys)
    {
        if (string.IsNullOrEmpty(rawJwt))
        {
            return default;
        }

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        if (!handler.CanReadToken(rawJwt))
        {
            return new AccessTokenInspection(null, null, "not a well-formed JWT");
        }

        // Parse once, for the audience decision below; then threaded through as preParsed.
        var parsed = handler.ReadJwtToken(rawJwt);
        var audience = parsed.Audiences.Contains(clientId, StringComparer.Ordinal) ? clientId : null;

        var validated = ValidateSignedJwt(rawJwt, issuer, audience, signingKeys, out var raw, out var error, parsed);

        return validated != null && !AuthorizedPartyMatches(validated, clientId)
            ? new AccessTokenInspection(raw, null, "access token authorized-party claim identifies a different client")
            : new AccessTokenInspection(raw, validated, error);
    }

    /// <summary>
    /// True when the access token carries no authorized-party claim, or every one it does carry
    /// matches the configured client. A mismatch means the token was issued to a different client,
    /// so its role claims must not be trusted. Covers <c>azp</c> (OIDC), <c>client_id</c>
    /// (RFC 9068), <c>cid</c> (Okta) and <c>appid</c> (Entra v1).
    /// </summary>
    public static bool AuthorizedPartyMatches(JwtSecurityToken token, string clientId)
    {
        foreach (var claimType in new[] { "azp", "client_id", "cid", "appid" })
        {
            var value = token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
            if (!string.IsNullOrEmpty(value) && !string.Equals(value, clientId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Cryptographically validates a signed JWT against <paramref name="signingKeys"/> -
    /// signature, issuer, lifetime, and (only when <paramref name="audience"/> is non-null)
    /// audience. Shared by the id_token, access-token, and logout_token paths. <paramref name="rawToken"/>
    /// is the parsed (but not cryptographically checked) token whenever <paramref name="rawJwt"/>
    /// is at least well-formed - set even when validation fails - so a caller needing raw claims
    /// (a non-critical fallback claim) doesn't need a second parse. A caller that has already
    /// parsed the token can pass it as <paramref name="preParsed"/> to skip the re-parse.
    /// Returns the validated token, or null with a reason in <paramref name="error"/>.
    /// </summary>
    public static JwtSecurityToken? ValidateSignedJwt(
        string rawJwt,
        string? issuer,
        string? audience,
        IEnumerable<SecurityKey> signingKeys,
        out JwtSecurityToken? rawToken,
        out string? error,
        JwtSecurityToken? preParsed = null)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();

        rawToken = preParsed;
        if (rawToken == null)
        {
            if (!handler.CanReadToken(rawJwt))
            {
                error = "not a well-formed JWT";
                return null;
            }

            rawToken = handler.ReadJwtToken(rawJwt);
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKeys = signingKeys,
            ValidateIssuer = true,
            ValidateAudience = audience != null,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        try
        {
            handler.ValidateToken(rawJwt, parameters, out var validated);
            error = null;
            return (JwtSecurityToken)validated;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }
}
