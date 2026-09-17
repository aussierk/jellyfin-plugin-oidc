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
/// <see cref="ResolvedIdentity"/>, falling back to userinfo per-field when the id_token lacks it.
/// userinfo and access-token inspection each run at most once. Extracted verbatim from
/// <c>OidcController.Callback</c>.
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

        // Every ladder below (identity claims, role, picture) may need these; Lazy runs each at most once.
        var userInfoOnce = new Lazy<Task<UserInfoResponse?>>(
            () => _protocol.FetchUserInfoAsync(disco, provider, providerId, tokenResponse.AccessToken));
        var accessTokenOnce = new Lazy<AccessTokenInspection>(
            () => OidcProtocolService.InspectAccessToken(tokenResponse.AccessToken, disco.Issuer, provider.ClientId, signingKeys));

        var username = await WithUserInfoFallbackAsync(
            ClaimParser.ExtractClaim(idToken, provider.UsernameClaim), provider.UsernameClaim, userInfoOnce).ConfigureAwait(false);
        if (string.IsNullOrEmpty(username))
        {
            username = subject;
        }

        if (string.IsNullOrEmpty(username))
        {
            return new IdentityResolution(null, ClaimsResolutionError.UsernameMissing);
        }

        var displayName = await WithUserInfoFallbackAsync(
            ClaimParser.ExtractClaim(idToken, provider.DisplayNameClaim), provider.DisplayNameClaim, userInfoOnce).ConfigureAwait(false);

        var emailClaimName = provider.EmailClaimOrDefault;
        var email = EmailWithEntraFallback(path => ClaimParser.ExtractFirstClaim(idToken, path), emailClaimName);
        if (string.IsNullOrEmpty(email))
        {
            var userInfo = await userInfoOnce.Value.ConfigureAwait(false);
            email = userInfo != null
                ? EmailWithEntraFallback(path => ClaimParser.ExtractFirstClaimFromJson(userInfo.Raw, path), emailClaimName)
                : string.Empty;
        }

        var emailVerified = await WithUserInfoFallbackAsync(
            ClaimParser.ExtractBool(idToken, provider.EmailVerifiedClaimOrDefault), provider.EmailVerifiedClaimOrDefault, userInfoOnce).ConfigureAwait(false);

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

            // Same trust model as the role ladder above: an access token's claims are only used
            // once its signature has validated, so an unverified token can't steer this URL.
            if (string.IsNullOrEmpty(pictureUrl) && accessTokenOnce.Value.Validated is { } validatedAccessToken)
            {
                pictureUrl = ClaimParser.ExtractClaim(validatedAccessToken, provider.PictureClaim);
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

    /// <c>value</c> if non-empty, else the same claim from userinfo.
    private static async Task<string> WithUserInfoFallbackAsync(
        string value, string claimPath, Lazy<Task<UserInfoResponse?>> userInfoOnce)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        var userInfo = await userInfoOnce.Value.ConfigureAwait(false);
        return userInfo != null ? ClaimParser.ExtractFirstClaimFromJson(userInfo.Raw, claimPath) : string.Empty;
    }

    /// Bool overload.
    private static async Task<bool> WithUserInfoFallbackAsync(
        bool value, string claimPath, Lazy<Task<UserInfoResponse?>> userInfoOnce)
    {
        if (value)
        {
            return true;
        }

        var userInfo = await userInfoOnce.Value.ConfigureAwait(false);
        return userInfo != null && ClaimParser.ExtractBoolFromJson(userInfo.Raw, claimPath);
    }

    /// Falls back to "emails" (Entra's array claim) when using the spec-default email claim name.
    private static string EmailWithEntraFallback(Func<string, string> extract, string emailClaimName)
    {
        var email = extract(emailClaimName);
        if (string.IsNullOrEmpty(email) && string.Equals(emailClaimName, "email", StringComparison.OrdinalIgnoreCase))
        {
            email = extract("emails");
        }

        return email;
    }
}
