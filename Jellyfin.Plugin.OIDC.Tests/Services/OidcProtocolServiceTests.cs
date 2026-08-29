using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Duende.IdentityModel.Client;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

/// <summary>
/// Direct tests of the token cryptography and TOFU-pinning logic that used to live as private
/// methods on <c>OidcController</c> (previously reached only by reflection).
/// </summary>
[Xunit.Collection("OidcPlugin")]
public class OidcProtocolServiceTests
{
    private readonly PluginTestFixture _fixture;

    public OidcProtocolServiceTests(PluginTestFixture fixture) => _fixture = fixture;

    private static OidcProtocolService MakeService(HttpMessageHandler? handler = null)
        => new(TestHttp.GuardedFactory(handler), NullLogger<OidcProtocolService>.Instance);

    // ── ValidateSignedJwt (audience enforcement for the access-token roles path) ──

    private static string SignHs256(string issuer, string audience, SecurityKey key)
        => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", "u1")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

    [Fact]
    public void ValidateSignedJwt_AudiencePassed_RejectsMismatchedAudience()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var jwt = SignHs256("https://idp.test", "some-other-client", key);

        var result = OidcProtocolService.ValidateSignedJwt(
            jwt, "https://idp.test", "my-client", [key], out _, out var error);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateSignedJwt_AudiencePassed_AcceptsMatchingAudience()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var jwt = SignHs256("https://idp.test", "my-client", key);

        var result = OidcProtocolService.ValidateSignedJwt(
            jwt, "https://idp.test", "my-client", [key], out _, out _);

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSignedJwt_NullAudience_AcceptsAnyAudience()
    {
        // The access-token roles path passes null when the token isn't client-audienced, so a
        // resource-audienced token (Entra Graph, custom API) still validates on issuer+sig+lifetime.
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var jwt = SignHs256("https://idp.test", "https://graph.example/resource", key);

        var result = OidcProtocolService.ValidateSignedJwt(
            jwt, "https://idp.test", null, [key], out _, out _);

        Assert.NotNull(result);
    }

    // ── AuthorizedPartyMatches (access token issued to a different client) ─────────

    private static bool AuthorizedPartyMatches(string clientId, params (string Type, string Value)[] claims)
        => OidcProtocolService.AuthorizedPartyMatches(
            new JwtSecurityToken(claims: claims.Select(c => new Claim(c.Type, c.Value))), clientId);

    [Fact]
    public void AuthorizedPartyMatches_NoAuthorizedPartyClaim_ReturnsTrue()
        => Assert.True(AuthorizedPartyMatches("my-client", ("sub", "u1")));

    [Theory]
    [InlineData("azp")]
    [InlineData("client_id")]
    [InlineData("cid")]
    [InlineData("appid")]
    public void AuthorizedPartyMatches_ClaimMatchesClient_ReturnsTrue(string claimType)
        => Assert.True(AuthorizedPartyMatches("my-client", (claimType, "my-client")));

    [Theory]
    [InlineData("azp")]
    [InlineData("client_id")]
    [InlineData("cid")]
    [InlineData("appid")]
    public void AuthorizedPartyMatches_ClaimIsDifferentClient_ReturnsFalse(string claimType)
        => Assert.False(AuthorizedPartyMatches("my-client", (claimType, "other-client")));

    [Fact]
    public void AuthorizedPartyMatches_OneOfSeveralClaimsMismatches_ReturnsFalse()
        => Assert.False(AuthorizedPartyMatches(
            "my-client", ("azp", "my-client"), ("client_id", "other-client")));

    // ── InspectAccessToken ───────────────────────────────────────────────────────

    [Fact]
    public void InspectAccessToken_OpaqueToken_RawIsNull()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

        var result = OidcProtocolService.InspectAccessToken("opaque-not-a-jwt", "https://idp.test", "my-client", [key]);

        Assert.Null(result.Raw);
        Assert.Null(result.Validated);
    }

    [Fact]
    public void InspectAccessToken_ValidResourceAudiencedJwt_Validates()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var jwt = SignHs256("https://idp.test", "https://resource.example", key);

        var result = OidcProtocolService.InspectAccessToken(jwt, "https://idp.test", "my-client", [key]);

        Assert.NotNull(result.Raw);
        Assert.NotNull(result.Validated);
        Assert.Null(result.Error);
    }

    [Fact]
    public void InspectAccessToken_AuthorizedPartyIsDifferentClient_RejectsWithError()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var jwt = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://idp.test",
            audience: "https://resource.example",
            claims: [new Claim("sub", "u1"), new Claim("azp", "some-other-client")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        var result = OidcProtocolService.InspectAccessToken(jwt, "https://idp.test", "my-client", [key]);

        Assert.NotNull(result.Raw);
        Assert.Null(result.Validated);
        Assert.NotNull(result.Error);
    }

    // ── ValidateOrPinEndpoints (TOFU pinning of discovery endpoints) ──────────────
    // Uses a real DiscoveryDocumentResponse parsed from mocked discovery JSON.

    private static async Task<DiscoveryDocumentResponse> MakeDiscoAsync(
        string authority, string? userInfoEndpoint = null, string? authorizeEndpoint = null)
    {
        var doc = OidcTestTokens.DiscoveryJson(authority, userInfoEndpoint, authorizeEndpoint);
        using var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, doc));
        return await httpClient.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = authority,
            Policy = new DiscoveryPolicy { ValidateIssuerName = true, ValidateEndpoints = false }
        });
    }

    private static bool ValidateOrPinEndpoints(OidcProviderConfig provider, DiscoveryDocumentResponse disco)
        => MakeService().ValidateOrPinEndpoints(provider, disco);

    [Fact]
    public async Task ValidateOrPinEndpoints_Unpinned_PinsAllFiveEndpoints()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        var provider = new OidcProviderConfig { ProviderId = "p1", Authority = authority };
        var disco = await MakeDiscoAsync(authority, $"{authority}/userinfo");

        var result = ValidateOrPinEndpoints(provider, disco);

        Assert.True(result);
        Assert.Equal(authority, provider.PinnedAuthority);
        Assert.Equal(disco.Issuer, provider.PinnedIssuer);
        Assert.Equal(disco.TokenEndpoint, provider.PinnedTokenEndpoint);
        Assert.Equal(disco.JwksUri, provider.PinnedJwksUri);
        Assert.Equal(disco.UserInfoEndpoint, provider.PinnedUserInfoEndpoint);
        Assert.Equal($"{authority}/userinfo", provider.PinnedUserInfoEndpoint);
        Assert.Equal(disco.AuthorizeEndpoint, provider.PinnedAuthorizeEndpoint);
        Assert.Equal($"{authority}/authorize", provider.PinnedAuthorizeEndpoint);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_UnpinnedWithNoUserInfoEndpoint_PinsEmptyAndSucceeds()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        var provider = new OidcProviderConfig { ProviderId = "p1", Authority = authority };
        var disco = await MakeDiscoAsync(authority);

        var firstLogin = ValidateOrPinEndpoints(provider, disco);
        var secondLogin = ValidateOrPinEndpoints(provider, disco);

        Assert.True(firstLogin);
        Assert.Equal(string.Empty, provider.PinnedUserInfoEndpoint);
        Assert.True(secondLogin);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_AllEndpointsMatchPins_ReturnsTrueAndLeavesPinsUnchanged()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        var disco = await MakeDiscoAsync(authority, $"{authority}/userinfo");
        var provider = new OidcProviderConfig
        {
            ProviderId = "p1",
            Authority = authority,
            PinnedAuthority = authority,
            PinnedIssuer = disco.Issuer!,
            PinnedTokenEndpoint = disco.TokenEndpoint!,
            PinnedJwksUri = disco.JwksUri!,
            PinnedUserInfoEndpoint = disco.UserInfoEndpoint!
        };

        var result = ValidateOrPinEndpoints(provider, disco);

        Assert.True(result);
        Assert.Equal(disco.UserInfoEndpoint, provider.PinnedUserInfoEndpoint);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_UserInfoEndpointDriftedFromPin_ReturnsFalseAndRetainsOldPin()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        const string trustedUserInfo = "https://203.0.113.10/userinfo";
        const string attackerUserInfo = "https://attacker.example/userinfo";
        var pinnedDisco = await MakeDiscoAsync(authority, trustedUserInfo);
        var provider = new OidcProviderConfig
        {
            ProviderId = "p1",
            Authority = authority,
            PinnedAuthority = authority,
            PinnedIssuer = pinnedDisco.Issuer!,
            PinnedTokenEndpoint = pinnedDisco.TokenEndpoint!,
            PinnedJwksUri = pinnedDisco.JwksUri!,
            PinnedUserInfoEndpoint = trustedUserInfo
        };
        var tamperedDisco = await MakeDiscoAsync(authority, attackerUserInfo);

        var result = ValidateOrPinEndpoints(provider, tamperedDisco);

        Assert.False(result);
        Assert.Equal(trustedUserInfo, provider.PinnedUserInfoEndpoint);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_TokenEndpointDriftedFromPin_ReturnsFalse()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        var pinnedDisco = await MakeDiscoAsync(authority);
        var provider = new OidcProviderConfig
        {
            ProviderId = "p1",
            Authority = authority,
            PinnedAuthority = authority,
            PinnedIssuer = pinnedDisco.Issuer!,
            PinnedTokenEndpoint = "https://203.0.113.10/old-token",
            PinnedJwksUri = pinnedDisco.JwksUri!,
            PinnedUserInfoEndpoint = string.Empty
        };

        var result = ValidateOrPinEndpoints(provider, pinnedDisco);

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_AuthorityChanged_RePinsAllFiveEndpoints()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string oldAuthority = "https://203.0.113.10";
        const string newAuthority = "https://203.0.113.20";
        var newDisco = await MakeDiscoAsync(newAuthority, $"{newAuthority}/userinfo");
        var provider = new OidcProviderConfig
        {
            ProviderId = "p1",
            Authority = newAuthority,
            PinnedAuthority = oldAuthority,
            PinnedIssuer = $"{oldAuthority}",
            PinnedTokenEndpoint = $"{oldAuthority}/token",
            PinnedJwksUri = $"{oldAuthority}/jwks",
            PinnedUserInfoEndpoint = $"{oldAuthority}/userinfo",
            PinnedAuthorizeEndpoint = $"{oldAuthority}/authorize"
        };

        var result = ValidateOrPinEndpoints(provider, newDisco);

        Assert.True(result);
        Assert.Equal(newAuthority, provider.PinnedAuthority);
        Assert.Equal(newDisco.UserInfoEndpoint, provider.PinnedUserInfoEndpoint);
        Assert.Equal($"{newAuthority}/userinfo", provider.PinnedUserInfoEndpoint);
        Assert.Equal(newDisco.AuthorizeEndpoint, provider.PinnedAuthorizeEndpoint);
        Assert.Equal($"{newAuthority}/authorize", provider.PinnedAuthorizeEndpoint);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_AuthorizeEndpointDriftedFromPin_ReturnsFalseAndRetainsOldPin()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        const string trustedAuthorize = "https://203.0.113.10/authorize";
        const string attackerAuthorize = "https://attacker.example/authorize";
        var pinnedDisco = await MakeDiscoAsync(authority, $"{authority}/userinfo", trustedAuthorize);
        var provider = new OidcProviderConfig
        {
            ProviderId = "p1",
            Authority = authority,
            PinnedAuthority = authority,
            PinnedIssuer = pinnedDisco.Issuer!,
            PinnedTokenEndpoint = pinnedDisco.TokenEndpoint!,
            PinnedJwksUri = pinnedDisco.JwksUri!,
            PinnedUserInfoEndpoint = pinnedDisco.UserInfoEndpoint!,
            PinnedAuthorizeEndpoint = trustedAuthorize
        };
        var tamperedDisco = await MakeDiscoAsync(authority, $"{authority}/userinfo", attackerAuthorize);

        var result = ValidateOrPinEndpoints(provider, tamperedDisco);

        Assert.False(result);
        Assert.Equal(trustedAuthorize, provider.PinnedAuthorizeEndpoint);
    }

    [Fact]
    public async Task ValidateOrPinEndpoints_PreExistingPinMissingNewerFields_BackfillsWithoutRejecting()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        const string authority = "https://203.0.113.10";
        var disco = await MakeDiscoAsync(authority, $"{authority}/userinfo");
        var provider = new OidcProviderConfig
        {
            ProviderId = "p1",
            Authority = authority,
            PinnedAuthority = authority,
            PinnedIssuer = disco.Issuer!,
            PinnedTokenEndpoint = disco.TokenEndpoint!,
            PinnedJwksUri = disco.JwksUri!,
            PinnedUserInfoEndpoint = string.Empty,
            PinnedAuthorizeEndpoint = string.Empty
        };

        var firstLoginAfterUpgrade = ValidateOrPinEndpoints(provider, disco);
        var secondLogin = ValidateOrPinEndpoints(provider, disco);

        Assert.True(firstLoginAfterUpgrade);
        Assert.Equal(disco.UserInfoEndpoint, provider.PinnedUserInfoEndpoint);
        Assert.Equal(disco.AuthorizeEndpoint, provider.PinnedAuthorizeEndpoint);
        Assert.True(secondLogin);
    }
}
