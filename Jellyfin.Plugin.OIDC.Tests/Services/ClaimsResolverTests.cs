using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
/// Direct coverage of the two 3-tier fallback ladders (roles: id_token → validated access token →
/// userinfo; picture: id_token → validated access token → userinfo), the username fallback, the
/// strict-access-token failure channel, and the run-once guarantee on the userinfo call.
/// </summary>
[Xunit.Collection("OidcPlugin")]
public class ClaimsResolverTests
{
    private const string Authority = "https://203.0.113.10";
    private const string ClientId = "test-client";

    private readonly PluginTestFixture _fixture;

    public ClaimsResolverTests(PluginTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.SetConfiguration(new PluginConfiguration());
    }

    // ── role ladder ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Roles_FromIdToken_TakesPrecedence()
    {
        var (resolver, handler) = MakeResolver();
        var ctx = Context(IdToken(groups: ["from-id"]), TokenResponseJson(accessToken: "opaque"));

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal(["from-id"], result.Identity!.Roles);
        Assert.Equal(0, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Roles_FromValidatedAccessToken_WhenIdTokenHasNone()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var accessJwt = SignHs256(key, "https://resource.example", [new Claim("groups", "from-access")]);
        var (resolver, _) = MakeResolver();
        var ctx = Context(IdToken(groups: null), TokenResponseJson(accessToken: accessJwt), signingKey: key);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal(["from-access"], result.Identity!.Roles);
    }

    [Fact]
    public async Task Roles_FromUserInfo_WhenIdTokenAndAccessTokenHaveNone()
    {
        var (resolver, handler) = MakeResolver(
            userInfo: () => """{ "sub": "sub-1", "groups": ["from-userinfo"] }""");
        var ctx = Context(
            IdToken(groups: null), TokenResponseJson(accessToken: "opaque"), withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal(["from-userinfo"], result.Identity!.Roles);
        Assert.Equal(1, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Roles_StrictAccessTokenValidationFails_ReturnsFailureBeforePicture()
    {
        var inKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var otherKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var accessJwt = SignHs256(otherKey, "https://resource.example", [new Claim("groups", "x")]);
        var (resolver, handler) = MakeResolver(userInfo: () => """{ "picture": "p" }""");
        var provider = Provider();
        provider.StrictAccessTokenValidation = true;
        var ctx = Context(
            IdToken(groups: null, picture: null), TokenResponseJson(accessToken: accessJwt),
            provider: provider, signingKey: inKey, withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Null(result.Identity);
        Assert.Equal(ClaimsResolutionError.AccessTokenValidationFailed, result.Error);
        Assert.Equal(0, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Roles_NonStrictAccessTokenValidationFails_RolesEmptyNoFailure()
    {
        var inKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var otherKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var accessJwt = SignHs256(otherKey, "https://resource.example", [new Claim("groups", "x")]);
        var (resolver, _) = MakeResolver();
        var provider = Provider();
        provider.StrictAccessTokenValidation = false;
        var ctx = Context(
            IdToken(groups: null), TokenResponseJson(accessToken: accessJwt),
            provider: provider, signingKey: inKey);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal(ClaimsResolutionError.None, result.Error);
        Assert.Empty(result.Identity!.Roles);
    }

    // ── picture ladder ───────────────────────────────────────────────────────

    [Fact]
    public async Task Picture_FromIdToken()
    {
        var (resolver, _) = MakeResolver();
        var ctx = Context(
            IdToken(groups: ["staff"], picture: "https://cdn/id.png"), TokenResponseJson(accessToken: "opaque"));

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("https://cdn/id.png", result.Identity!.PictureUrl);
    }

    [Fact]
    public async Task Picture_FromValidatedAccessToken_WhenIdTokenHasNone()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var accessJwt = SignHs256(key, "https://resource.example", [new Claim("picture", "https://cdn/access.png")]);
        var (resolver, _) = MakeResolver();
        var ctx = Context(
            IdToken(groups: ["staff"], picture: null), TokenResponseJson(accessToken: accessJwt), signingKey: key);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("https://cdn/access.png", result.Identity!.PictureUrl);
    }

    [Fact]
    public async Task Picture_UnvalidatedAccessToken_NotTrusted_FallsThroughToUserInfo()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var accessJwt = SignHs256(key, "https://resource.example", [new Claim("picture", "https://cdn/access.png")]);
        var (resolver, handler) = MakeResolver(
            userInfo: () => """{ "sub": "sub-1", "picture": "https://cdn/userinfo.png" }""");
        var ctx = Context(
            IdToken(groups: ["staff"], picture: null), TokenResponseJson(accessToken: accessJwt),
            withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("https://cdn/userinfo.png", result.Identity!.PictureUrl);
        Assert.Equal(1, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Picture_FromUserInfo_WhenNeitherTokenHasIt()
    {
        var (resolver, handler) = MakeResolver(
            userInfo: () => """{ "sub": "sub-1", "picture": "https://cdn/userinfo.png" }""");
        var ctx = Context(
            IdToken(groups: ["staff"], picture: null), TokenResponseJson(accessToken: "opaque"),
            withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("https://cdn/userinfo.png", result.Identity!.PictureUrl);
        Assert.Equal(1, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Picture_SyncProfileImageDisabled_IsNull()
    {
        var (resolver, _) = MakeResolver();
        var provider = Provider();
        provider.SyncProfileImage = false;
        var ctx = Context(
            IdToken(groups: ["staff"], picture: "https://cdn/id.png"), TokenResponseJson(accessToken: "opaque"),
            provider: provider);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Null(result.Identity!.PictureUrl);
    }

    // ── username ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Username_FallsBackToSubject_WhenUsernameClaimMissing()
    {
        var (resolver, _) = MakeResolver();
        var idToken = new JwtSecurityToken(claims: [new Claim("sub", "sub-only"), new Claim("groups", "staff")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"));

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("sub-only", result.Identity!.Username);
    }

    [Fact]
    public async Task Username_BothMissing_ReturnsUsernameMissing()
    {
        var (resolver, _) = MakeResolver();
        var idToken = new JwtSecurityToken(claims: [new Claim("groups", "staff")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"));

        var result = await resolver.ResolveAsync(ctx);

        Assert.Null(result.Identity);
        Assert.Equal(ClaimsResolutionError.UsernameMissing, result.Error);
    }

    // ── email ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Email_FromEmailsArray_WhenEmailClaimAbsent()
    {
        var (resolver, _) = MakeResolver();
        var idToken = new JwtSecurityToken(claims:
        [
            new Claim("sub", "sub-1"), new Claim("preferred_username", "alice"), new Claim("groups", "staff"),
            new Claim("emails", "alice@corp.com"),
        ]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"));

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("alice@corp.com", result.Identity!.Email);
    }

    [Fact]
    public async Task Email_FromEmailsArray_DoesNotFireWhenEmailClaimIsCustomized()
    {
        // The "emails" fallback is an Entra-specific quirk for the spec-default claim name only -
        // it must not override an admin's explicit EmailClaim customization.
        var (resolver, _) = MakeResolver();
        var provider = Provider();
        provider.EmailClaim = "mail";
        var idToken = new JwtSecurityToken(claims:
        [
            new Claim("sub", "sub-1"), new Claim("preferred_username", "alice"), new Claim("groups", "staff"),
            new Claim("emails", "alice@corp.com"),
        ]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), provider: provider);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal(string.Empty, result.Identity!.Email);
    }

    [Fact]
    public async Task EmailVerified_ReadsConfiguredClaimName()
    {
        var (resolver, _) = MakeResolver();
        var provider = Provider();
        provider.EmailVerifiedClaim = "verified";
        var idToken = new JwtSecurityToken(claims:
        [
            new Claim("sub", "sub-1"), new Claim("preferred_username", "alice"), new Claim("groups", "staff"),
            new Claim("email", "alice@corp.com"), new Claim("verified", "true", ClaimValueTypes.Boolean),
        ]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), provider: provider);

        var result = await resolver.ResolveAsync(ctx);

        Assert.True(result.Identity!.EmailVerified);
    }

    // ── userinfo fallback for username / displayName / email / email_verified ──

    [Fact]
    public async Task Username_FromUserInfo_WhenIdTokenLacksUsernameClaim()
    {
        var (resolver, handler) = MakeResolver(
            userInfo: () => """{ "sub": "sub-1", "preferred_username": "alice-from-userinfo" }""");
        var idToken = new JwtSecurityToken(claims: [new Claim("sub", "sub-1"), new Claim("groups", "staff")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("alice-from-userinfo", result.Identity!.Username);
        Assert.Equal(1, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task Username_IdTokenClaimPresent_UserInfoNotConsulted()
    {
        var (resolver, handler) = MakeResolver(userInfo: () => """{ "preferred_username": "should-not-be-used" }""");
        var ctx = Context(
            IdToken(groups: ["staff"], picture: "https://cdn/id.png"), TokenResponseJson(accessToken: "opaque"),
            withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("alice", result.Identity!.Username);
        Assert.Equal(0, handler.HitCount("/userinfo"));
    }

    [Fact]
    public async Task DisplayName_FromUserInfo_WhenIdTokenLacksIt()
    {
        var (resolver, _) = MakeResolver(userInfo: () => """{ "sub": "sub-1", "name": "Alice From UserInfo" }""");
        var idToken = new JwtSecurityToken(claims: [new Claim("sub", "sub-1"), new Claim("preferred_username", "alice")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("Alice From UserInfo", result.Identity!.DisplayName);
    }

    [Fact]
    public async Task Email_FromUserInfo_WhenIdTokenLacksEmailClaim()
    {
        var (resolver, _) = MakeResolver(userInfo: () => """{ "sub": "sub-1", "email": "alice@userinfo.com" }""");
        var idToken = new JwtSecurityToken(claims: [new Claim("sub", "sub-1"), new Claim("preferred_username", "alice")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("alice@userinfo.com", result.Identity!.Email);
    }

    [Fact]
    public async Task Email_FromUserInfoEmailsArray_WhenEmailClaimIsSpecDefault()
    {
        var (resolver, _) = MakeResolver(userInfo: () => """{ "sub": "sub-1", "emails": ["alice@userinfo.com"] }""");
        var idToken = new JwtSecurityToken(claims: [new Claim("sub", "sub-1"), new Claim("preferred_username", "alice")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal("alice@userinfo.com", result.Identity!.Email);
    }

    [Fact]
    public async Task EmailVerified_FromUserInfo_AsJsonBoolean_WhenIdTokenLacksIt()
    {
        var (resolver, _) = MakeResolver(
            userInfo: () => """{ "sub": "sub-1", "email": "alice@userinfo.com", "email_verified": true }""");
        var idToken = new JwtSecurityToken(claims: [new Claim("sub", "sub-1"), new Claim("preferred_username", "alice")]);
        var ctx = Context(idToken, TokenResponseJson(accessToken: "opaque"), withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.True(result.Identity!.EmailVerified);
    }

    [Fact]
    public async Task EmailVerified_IdTokenTrue_UserInfoNotConsulted()
    {
        var (resolver, handler) = MakeResolver(userInfo: () => """{ "email_verified": false }""");
        var ctx = Context(
            IdToken(groups: ["staff"], picture: "https://cdn/id.png"), TokenResponseJson(accessToken: "opaque"),
            withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.True(result.Identity!.EmailVerified);
        Assert.Equal(0, handler.HitCount("/userinfo"));
    }

    // ── run-once ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserInfo_RequestedAtMostOnce_AcrossBothLadders()
    {
        // id_token carries neither roles nor picture → both ladders reach the userinfo tier.
        var (resolver, handler) = MakeResolver(
            userInfo: () => """{ "sub": "sub-1", "groups": ["staff"], "picture": "https://cdn/u.png" }""");
        var ctx = Context(
            IdToken(groups: null, picture: null), TokenResponseJson(accessToken: "opaque"),
            withUserInfoEndpoint: true);

        var result = await resolver.ResolveAsync(ctx);

        Assert.Equal(["staff"], result.Identity!.Roles);
        Assert.Equal("https://cdn/u.png", result.Identity.PictureUrl);
        Assert.Equal(1, handler.HitCount("/userinfo"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static OidcProviderConfig Provider() => new()
    {
        ProviderId = "p1",
        Authority = Authority,
        ClientId = ClientId,
        Enabled = true,
    };

    private (ClaimsResolver Resolver, RoutingHttpMessageHandler Handler) MakeResolver(
        Func<string>? userInfo = null)
    {
        var handler = RoutingHttpMessageHandler.ForFlow(
            discovery: () => OidcTestTokens.DiscoveryJson(Authority, $"{Authority}/userinfo"),
            token: () => "{}",
            jwks: () => "{ \"keys\": [] }",
            userInfo: userInfo ?? (() => "{}"));
        var protocol = new OidcProtocolService(
            TestHttp.GuardedFactory(handler), NullLogger<OidcProtocolService>.Instance);
        return (new ClaimsResolver(protocol, NullLogger<ClaimsResolver>.Instance), handler);
    }

    private static ClaimsResolutionContext Context(
        JwtSecurityToken idToken,
        string tokenResponseJson,
        OidcProviderConfig? provider = null,
        SymmetricSecurityKey? signingKey = null,
        bool withUserInfoEndpoint = false)
    {
        var disco = MakeDisco(withUserInfoEndpoint ? $"{Authority}/userinfo" : null);
        var keys = signingKey != null ? new List<SecurityKey> { signingKey } : new List<SecurityKey>();
        return new ClaimsResolutionContext(
            idToken, TokenResponse(tokenResponseJson), disco, provider ?? Provider(), "p1", keys);
    }

    private static JwtSecurityToken IdToken(IEnumerable<string>? groups, string? picture = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "sub-1"),
            new("preferred_username", "alice"),
            new("name", "Alice"),
            new("email", "alice@example.com"),
            new("email_verified", "true", ClaimValueTypes.Boolean),
        };
        if (groups != null)
        {
            claims.AddRange(groups.Select(g => new Claim("groups", g)));
        }

        if (picture != null)
        {
            claims.Add(new Claim("picture", picture));
        }

        return new JwtSecurityToken(claims: claims);
    }

    private static string TokenResponseJson(string? accessToken)
        => OidcTestTokens.TokenResponseJson(idToken: "unused", accessToken: accessToken);

    private static string SignHs256(SecurityKey key, string audience, IEnumerable<Claim> claims)
        => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: Authority,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

    private static Duende.IdentityModel.Client.TokenResponse TokenResponse(string json)
    {
        var msg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return ProtocolResponse.FromHttpResponseAsync<Duende.IdentityModel.Client.TokenResponse>(msg)
            .GetAwaiter().GetResult();
    }

    private static Duende.IdentityModel.Client.DiscoveryDocumentResponse MakeDisco(string? userInfoEndpoint)
    {
        var json = OidcTestTokens.DiscoveryJson(Authority, userInfoEndpoint);
        using var http = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, json));
        return http.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest
        {
            Address = Authority,
            Policy = new DiscoveryPolicy { ValidateIssuerName = true, ValidateEndpoints = false },
        }).GetAwaiter().GetResult();
    }
}
