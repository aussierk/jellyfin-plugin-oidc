using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

/// <summary>
/// Drives <see cref="LoginFlowService.CompleteCallbackAsync"/> end-to-end against a fake IdP.
/// The <see cref="Failures"/> theory pins every failure status + message so a future reword
/// breaks a test on purpose.
/// </summary>
[Xunit.Collection("OidcPlugin")]
public class LoginFlowServiceTests
{
    private const string Authority = "https://203.0.113.10";
    private const string ClientId = "test-client";
    private const string ProviderId = "p1";
    private const string Nonce = "nonce-xyz";

    private readonly PluginTestFixture _fixture;

    public LoginFlowServiceTests(PluginTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CompleteCallback_HappyPath_MintsSession()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["staff"]);
        var (flow, stateManager) = Build(HandlerFor(key, idToken));

        var outcome = await flow.CompleteCallbackAsync(Provider(), ProviderId, State(quickConnect: false), "code");

        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.SessionToken);
        Assert.False(outcome.QuickConnect);

        var session = stateManager.PeekAuthorizedSession(outcome.SessionToken!);
        Assert.NotNull(session);
        Assert.Equal("alice", session!.Username);
        Assert.Equal(["staff"], session.Roles);
        Assert.Equal("sub-123", session.Subject);
        Assert.Equal(Authority, session.Issuer);
    }

    [Fact]
    public async Task CompleteCallback_QuickConnectState_FlagsOutcome()
    {
        var key = OidcTestTokens.CreateSigningKey();
        var idToken = OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["staff"]);
        var (flow, _) = Build(HandlerFor(key, idToken));

        var outcome = await flow.CompleteCallbackAsync(Provider(), ProviderId, State(quickConnect: true), "code");

        Assert.False(outcome.IsFailure);
        Assert.True(outcome.QuickConnect);
    }

    public enum Scenario
    {
        NonceMismatch,
        AdmissionDenied,
        TokenExchangeError,
        NoIdToken,
        PinMismatch,
        AzpMismatch,
        WrongAudience,
    }

    [Theory]
    [InlineData(Scenario.NonceMismatch, 400, "Token validation failed: nonce mismatch")]
    [InlineData(Scenario.AdmissionDenied, 400, "Your account is not permitted to sign in to this server.")]
    [InlineData(Scenario.TokenExchangeError, 400, "Token exchange failed. Check plugin logs for details.")]
    [InlineData(Scenario.NoIdToken, 400, "IdP returned no id_token. Ensure the 'openid' scope is configured for this client.")]
    [InlineData(Scenario.PinMismatch, 502, "Identity provider endpoint mismatch detected. Re-run Test Connection in the plugin admin UI.")]
    [InlineData(Scenario.AzpMismatch, 400, "Token validation failed")]
    [InlineData(Scenario.WrongAudience, 400, "Token validation failed: the token audience doesn't match this provider's Client ID.")]
    public async Task Failures(Scenario scenario, int expectedStatus, string expectedMessage)
    {
        var key = OidcTestTokens.CreateSigningKey();
        var provider = Provider();
        RoutingHttpMessageHandler handler;

        switch (scenario)
        {
            case Scenario.NonceMismatch:
                handler = HandlerFor(key, OidcTestTokens.IdToken(key, Authority, ClientId, "wrong-nonce", groups: ["staff"]));
                break;
            case Scenario.AdmissionDenied:
                _fixtureConfig.AllowedGroups = ["staff"];
                handler = HandlerFor(key, OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["guest"]));
                break;
            case Scenario.TokenExchangeError:
                handler = new RoutingHttpMessageHandler(
                    new("/.well-known/openid-configuration", HttpStatusCode.OK, () => OidcTestTokens.DiscoveryJson(Authority)),
                    new("/token", HttpStatusCode.BadRequest, () => """{ "error": "invalid_grant" }"""),
                    new("/jwks", HttpStatusCode.OK, () => OidcTestTokens.JwksJson(key)));
                break;
            case Scenario.NoIdToken:
                handler = new RoutingHttpMessageHandler(
                    new("/.well-known/openid-configuration", HttpStatusCode.OK, () => OidcTestTokens.DiscoveryJson(Authority)),
                    new("/token", HttpStatusCode.OK, () => """{ "access_token": "opaque", "token_type": "Bearer" }"""),
                    new("/jwks", HttpStatusCode.OK, () => OidcTestTokens.JwksJson(key)));
                break;
            case Scenario.AzpMismatch:
                // Signature + audience are fine, but the id_token names a different authorized party.
                handler = HandlerFor(key, OidcTestTokens.SignRs256(key, Authority, ClientId, new[]
                {
                    new Claim("sub", "sub-123"),
                    new Claim("preferred_username", "alice"),
                    new Claim("nonce", Nonce),
                    new Claim("azp", "a-different-client"),
                }));
                break;
            case Scenario.WrongAudience:
                // Valid signature and issuer, but the token was minted for a different client.
                handler = HandlerFor(key, OidcTestTokens.SignRs256(key, Authority, "wrong-audience", new[]
                {
                    new Claim("sub", "sub-123"),
                    new Claim("preferred_username", "alice"),
                    new Claim("nonce", Nonce),
                }));
                break;
            case Scenario.PinMismatch:
                provider.PinnedAuthority = Authority;
                provider.PinnedIssuer = Authority;
                provider.PinnedTokenEndpoint = $"{Authority}/OLD-token";
                provider.PinnedJwksUri = $"{Authority}/jwks";
                provider.PinnedAuthorizeEndpoint = $"{Authority}/authorize";
                provider.PinnedUserInfoEndpoint = string.Empty;
                handler = HandlerFor(key, OidcTestTokens.IdToken(key, Authority, ClientId, Nonce, groups: ["staff"]));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var (flow, _) = Build(handler);

        var outcome = await flow.CompleteCallbackAsync(provider, ProviderId, State(quickConnect: false), "code");

        Assert.True(outcome.IsFailure);
        Assert.Equal(expectedStatus, outcome.FailureStatusCode);
        Assert.Equal(expectedMessage, outcome.FailureMessage);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private PluginConfiguration _fixtureConfig = new();

    private static OidcProviderConfig Provider() => new()
    {
        ProviderId = ProviderId,
        Authority = Authority,
        ClientId = ClientId,
        Enabled = true,
    };

    private static OidcState State(bool quickConnect) => new()
    {
        ProviderId = ProviderId,
        Nonce = Nonce,
        CodeVerifier = "verifier",
        RedirectUri = $"{Authority}/sso/OIDC/Callback/{ProviderId}",
        CsrfToken = "csrf",
        QuickConnect = quickConnect,
    };

    private static RoutingHttpMessageHandler HandlerFor(RsaSecurityKey key, string idToken)
        => RoutingHttpMessageHandler.ForFlow(
            discovery: () => OidcTestTokens.DiscoveryJson(Authority),
            token: () => OidcTestTokens.TokenResponseJson(idToken),
            jwks: () => OidcTestTokens.JwksJson(key));

    private (LoginFlowService Flow, StateManager StateManager) Build(RoutingHttpMessageHandler handler)
    {
        _fixture.SetConfiguration(_fixtureConfig);
        var stateManager = new StateManager(NullLogger<StateManager>.Instance);
        var protocol = new OidcProtocolService(
            TestHttp.GuardedFactory(handler), NullLogger<OidcProtocolService>.Instance);
        var flow = new LoginFlowService(
            protocol,
            new ClaimsResolver(protocol, NullLogger<ClaimsResolver>.Instance),
            stateManager,
            NullLogger<LoginFlowService>.Instance);
        return (flow, stateManager);
    }
}
