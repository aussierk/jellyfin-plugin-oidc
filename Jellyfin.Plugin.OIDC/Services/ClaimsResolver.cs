using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>Inputs the callback flow hands <see cref="ClaimsResolver.ResolveAsync"/>.</summary>
public sealed record ClaimsResolutionContext(
    JwtSecurityToken IdToken,
    TokenResponse TokenResponse,
    DiscoveryDocumentResponse Disco,
    OidcProviderConfig Provider,
    string ProviderId,
    IList<SecurityKey> SigningKeys);

/// <summary>The identity a successful login carries into <see cref="StateManager.StoreAuthorizedSession"/>.</summary>
public sealed record ResolvedIdentity(
    string Subject,
    string? Sid,
    string Username,
    string DisplayName,
    string Email,
    bool EmailVerified,
    string[] Roles,
    string? PictureUrl);

/// <summary>Why <see cref="ClaimsResolver.ResolveAsync"/> stopped without a <see cref="ResolvedIdentity"/>.</summary>
public enum ClaimsResolutionError
{
    None,
    UsernameMissing,
    AccessTokenValidationFailed
}

/// <summary>Result of <see cref="ClaimsResolver.ResolveAsync"/>: exactly one member is set.</summary>
public readonly record struct IdentityResolution(ResolvedIdentity? Identity, ClaimsResolutionError Error);

/// <summary>
/// Turns a validated id_token (plus the token response and discovery document) into a
/// <see cref="ResolvedIdentity"/>: the six identity claims, then the 3-tier role fallback
/// (id_token → validated access token → userinfo) and the 3-tier picture fallback
/// (id_token → raw access token → userinfo). userinfo and access-token inspection each run at
/// most once across both ladders. Extracted verbatim from <c>OidcController.Callback</c>.
/// </summary>
public sealed class ClaimsResolver
{
    private readonly OidcProtocolService _protocol;
    private readonly ILogger<ClaimsResolver> _logger;

    public ClaimsResolver(OidcProtocolService protocol, ILogger<ClaimsResolver> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<IdentityResolution> ResolveAsync(ClaimsResolutionContext ctx)
    {
        var (idToken, tokenResponse, disco, provider, providerId, signingKeys) =
            (ctx.IdToken, ctx.TokenResponse, ctx.Disco, ctx.Provider, ctx.ProviderId, ctx.SigningKeys);

        var subject = ClaimParser.ExtractClaim(idToken, "sub");
        var sid = ClaimParser.ExtractClaim(idToken, "sid");

        var username = ClaimParser.ExtractClaim(idToken, provider.UsernameClaim);
        if (string.IsNullOrEmpty(username))
        {
            username = subject;
        }

        if (string.IsNullOrEmpty(username))
        {
            return new IdentityResolution(null, ClaimsResolutionError.UsernameMissing);
        }

        var displayName = ClaimParser.ExtractClaim(idToken, provider.DisplayNameClaim);

        var emailClaimName = provider.EmailClaimOrDefault;
        var email = ClaimParser.ExtractFirstClaim(idToken, emailClaimName);
        if (string.IsNullOrEmpty(email) && !string.Equals(emailClaimName, "emails", StringComparison.OrdinalIgnoreCase))
        {
            // Entra external identities carry the address in an "emails" array rather than "email".
            email = ClaimParser.ExtractFirstClaim(idToken, "emails");
        }

        var emailVerified = ClaimParser.ExtractBool(idToken, provider.EmailVerifiedClaimOrDefault);

        // Both fallbacks (role, picture) may need these; Lazy runs each at most once.
        var userInfoOnce = new Lazy<Task<UserInfoResponse?>>(
            () => _protocol.FetchUserInfoAsync(disco, provider, providerId, tokenResponse.AccessToken));
        var accessTokenOnce = new Lazy<AccessTokenInspection>(
            () => OidcProtocolService.InspectAccessToken(tokenResponse.AccessToken, disco.Issuer, provider.ClientId, signingKeys));

        var roles = ClaimParser.ExtractRoles(idToken, provider.RoleClaim);
        if (roles.Length == 0)
        {
            var accessToken = accessTokenOnce.Value;

            // An opaque access token (Google, default Authelia) is simply not a JWT: no roles, not a failure.
            if (accessToken.Raw != null)
            {
                if (accessToken.Validated != null)
                {
                    roles = ClaimParser.ExtractRoles(accessToken.Validated, provider.RoleClaim);
                }
                else if (provider.StrictAccessTokenValidation)
                {
                    _logger.LogWarning("Access token validation failed for provider {Provider}: {Message}", providerId, accessToken.Error);
                    return new IdentityResolution(null, ClaimsResolutionError.AccessTokenValidationFailed);
                }
                else
                {
                    _logger.LogWarning("Access token signature validation failed for {Provider}; roles from access token skipped", providerId);
                }
            }
        }

        // Some IdPs (Entra ID, some Okta setups) only expose roles via userinfo.
        if (roles.Length == 0)
        {
            var userInfo = await userInfoOnce.Value.ConfigureAwait(false);
            if (userInfo != null)
            {
                roles = ClaimParser.ExtractRolesFromJson(userInfo.Raw, provider.RoleClaim);
                if (roles.Length > 0)
                {
                    _logger.LogDebug("OIDC roles for {Provider} resolved from the userinfo endpoint", providerId);
                }
            }
        }

        string? pictureUrl = null;
        if (provider.SyncProfileImage && !string.IsNullOrWhiteSpace(provider.PictureClaim))
        {
            pictureUrl = ClaimParser.ExtractClaim(idToken, provider.PictureClaim);
            if (string.IsNullOrEmpty(pictureUrl) && accessTokenOnce.Value.Raw is { } rawAccessToken)
            {
                pictureUrl = ClaimParser.ExtractClaim(rawAccessToken, provider.PictureClaim);
            }

            // Some providers (e.g. Authentik) only expose the picture via userinfo.
            if (string.IsNullOrEmpty(pictureUrl))
            {
                var userInfo = await userInfoOnce.Value.ConfigureAwait(false);
                pictureUrl = userInfo?.Claims
                    .FirstOrDefault(c => c.Type == provider.PictureClaim)?.Value;
            }
        }

        return new IdentityResolution(
            new ResolvedIdentity(subject, sid, username, displayName, email, emailVerified, roles, pictureUrl),
            ClaimsResolutionError.None);
    }
}
