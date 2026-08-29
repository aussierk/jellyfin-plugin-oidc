using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Result of <see cref="LoginFlowService.CompleteCallbackAsync"/>. On success carries the one-time
/// session token (+ whether Quick Connect was requested); on failure carries the HTTP status and
/// the exact message the controller returns.
/// </summary>
public readonly record struct CallbackOutcome(
    string? SessionToken, bool QuickConnect, int FailureStatusCode, string? FailureMessage)
{
    public static CallbackOutcome Success(string token, bool quickConnect) => new(token, quickConnect, 0, null);

    public static CallbackOutcome Failure(int status, string message) => new(null, false, status, message);

    public bool IsFailure => FailureStatusCode != 0;
}

/// <summary>
/// Orchestrates everything the OIDC callback does after the controller has validated the request
/// (state, CSRF, provider): discovery + TOFU pin, code→token exchange, JWKS, id_token validation,
/// nonce check, identity/role/picture resolution, the server-wide admission gate, and minting the
/// one-time authorized session. Extracted from <c>OidcController.Callback</c> so the flow is
/// testable end-to-end without an HTTP context.
/// </summary>
public sealed class LoginFlowService
{
    private readonly OidcProtocolService _protocol;
    private readonly ClaimsResolver _claims;
    private readonly StateManager _stateManager;
    private readonly ILogger<LoginFlowService> _logger;

    public LoginFlowService(
        OidcProtocolService protocol,
        ClaimsResolver claims,
        StateManager stateManager,
        ILogger<LoginFlowService> logger)
    {
        _protocol = protocol;
        _claims = claims;
        _stateManager = stateManager;
        _logger = logger;
    }

    public async Task<CallbackOutcome> CompleteCallbackAsync(
        OidcProviderConfig provider, string providerId, OidcState oidcState, string code)
    {
        var (disco, failure) = await _protocol.ResolveAndPinDiscoveryAsync(provider, providerId).ConfigureAwait(false);
        if (failure == DiscoveryPinFailure.PinMismatch)
        {
            return CallbackOutcome.Failure(502, "Identity provider endpoint mismatch detected. Re-run Test Connection in the plugin admin UI.");
        }

        if (disco == null)
        {
            return CallbackOutcome.Failure(502, "Failed to contact identity provider");
        }

        var tokenResponse = await _protocol.ExchangeCodeAsync(
            disco, provider, providerId, code, oidcState.RedirectUri, oidcState.CodeVerifier).ConfigureAwait(false);
        if (tokenResponse == null)
        {
            return CallbackOutcome.Failure(502, "Failed to contact identity provider");
        }

        if (tokenResponse.IsError)
        {
            _logger.LogError("Token exchange failed for {Provider}: {Error}", providerId, tokenResponse.Error);
            _logger.LogDebug("Token exchange error detail for {Provider}: {Description}", providerId, tokenResponse.ErrorDescription);
            return CallbackOutcome.Failure(400, "Token exchange failed. Check plugin logs for details.");
        }

        var signingKeys = await _protocol.ResolveSigningKeysAsync(disco, provider, providerId).ConfigureAwait(false);
        if (signingKeys == null)
        {
            return CallbackOutcome.Failure(502, "Failed to fetch identity provider signing keys");
        }

        // An access token is never validated as identity (wrong audience semantics, no nonce).
        var rawIdToken = tokenResponse.IdentityToken;
        if (string.IsNullOrEmpty(rawIdToken))
        {
            _logger.LogWarning("Token response for provider {Provider} carried no id_token", providerId);
            return CallbackOutcome.Failure(400, "IdP returned no id_token. Ensure the 'openid' scope is configured for this client.");
        }

        var idToken = OidcProtocolService.ValidateSignedJwt(rawIdToken, disco.Issuer, provider.ClientId, signingKeys, out _, out var idTokenError);
        if (idToken == null)
        {
            _logger.LogWarning("Token validation failed for provider {Provider}: {Message}", providerId, idTokenError);
            return CallbackOutcome.Failure(400, "Token validation failed");
        }

        // OIDC Core §3.1.3.7 r5: a named authorized party must be our client (ValidateSignedJwt only checks audience).
        if (!OidcProtocolService.AuthorizedPartyMatches(idToken, provider.ClientId))
        {
            _logger.LogWarning("id_token authorized party is a different client for provider {Provider}", providerId);
            return CallbackOutcome.Failure(400, "Token validation failed");
        }

        var nonceClaim = idToken.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (string.IsNullOrEmpty(nonceClaim) || nonceClaim != oidcState.Nonce)
        {
            _logger.LogWarning("Nonce mismatch in OIDC callback for provider {Provider}", providerId);
            return CallbackOutcome.Failure(400, "Token validation failed: nonce mismatch");
        }

        var resolution = await _claims.ResolveAsync(new ClaimsResolutionContext(
            idToken, tokenResponse, disco, provider, providerId, signingKeys)).ConfigureAwait(false);
        if (resolution.Error == ClaimsResolutionError.UsernameMissing)
        {
            return CallbackOutcome.Failure(400, "Could not determine username from token");
        }

        if (resolution.Error == ClaimsResolutionError.AccessTokenValidationFailed)
        {
            return CallbackOutcome.Failure(400, "Access token validation failed");
        }

        var identity = resolution.Identity!;

        var admissionDenied = AdmissionService.Evaluate(
            OidcPlugin.CurrentConfig, identity.Roles, identity.Email, identity.EmailVerified);
        if (admissionDenied != null)
        {
            _logger.LogWarning(
                "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=admission-{Reason}",
                providerId, ClaimParser.RedactSubject(identity.Subject), identity.Username, admissionDenied);
            return CallbackOutcome.Failure(400, "Your account is not permitted to sign in to this server.");
        }

        _logger.LogInformation(
            "OIDC audit: decision=authorize provider={Provider} subject={Subject} user={User} roles=[{Roles}]",
            providerId, ClaimParser.RedactSubject(identity.Subject), identity.Username, string.Join(", ", identity.Roles));

        var sessionToken = _stateManager.StoreAuthorizedSession(new AuthorizedSession
        {
            ProviderId = providerId,
            Username = identity.Username,
            DisplayName = identity.DisplayName,
            PictureUrl = string.IsNullOrEmpty(identity.PictureUrl) ? null : identity.PictureUrl,
            Roles = identity.Roles,
            Subject = identity.Subject,
            Sid = string.IsNullOrEmpty(identity.Sid) ? null : identity.Sid,
            Issuer = disco.Issuer,
            Email = string.IsNullOrEmpty(identity.Email) ? null : identity.Email,
            EmailVerified = identity.EmailVerified
        });

        if (sessionToken == null)
        {
            return CallbackOutcome.Failure(503, "Server is busy. Please try again in a moment.");
        }

        return CallbackOutcome.Success(sessionToken, oidcState.QuickConnect);
    }
}
