using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// <summary>
/// Builds the artifacts an end-to-end OIDC callback test needs: a discovery document, an RSA
/// signing key, RS256-signed JWTs, and a JWKS that carries that key's public half. Used with
/// <see cref="RoutingHttpMessageHandler"/> to drive the real callback flow without a live IdP.
/// </summary>
public static class OidcTestTokens
{
    public const string DefaultKeyId = "oidc-test-key";

    /// <summary>A fresh RSA key. Dispose is left to GC - test scope only.</summary>
    public static RsaSecurityKey CreateSigningKey(string keyId = DefaultKeyId)
        => new(RSA.Create(2048)) { KeyId = keyId };

    /// <summary>
    /// A discovery document byte-compatible with the one <c>OidcControllerTests.MakeDiscoAsync</c>
    /// hand-rolls: endpoints derived from <paramref name="authority"/>, issuer == authority.
    /// </summary>
    public static string DiscoveryJson(
        string authority, string? userInfoEndpoint = null, string? authorizeEndpoint = null)
    {
        var userInfoLine = userInfoEndpoint != null
            ? $",\n    \"userinfo_endpoint\": \"{userInfoEndpoint}\""
            : string.Empty;

        return $$"""
            {
                "issuer": "{{authority}}",
                "authorization_endpoint": "{{authorizeEndpoint ?? authority + "/authorize"}}",
                "token_endpoint": "{{authority}}/token",
                "jwks_uri": "{{authority}}/jwks"{{userInfoLine}}
            }
            """;
    }

    /// <summary><c>{ "keys": [ &lt;public RSA JWK&gt; ] }</c> for <paramref name="key"/>.</summary>
    public static string JwksJson(RsaSecurityKey key)
    {
        var parameters = (key.Rsa ?? RSA.Create(key.Parameters)).ExportParameters(false);
        var n = Base64UrlEncoder.Encode(parameters.Modulus);
        var e = Base64UrlEncoder.Encode(parameters.Exponent);
        return $$"""
            { "keys": [ { "kty": "RSA", "use": "sig", "alg": "RS256", "kid": "{{key.KeyId}}", "n": "{{n}}", "e": "{{e}}" } ] }
            """;
    }

    /// <summary>A token endpoint response carrying <paramref name="idToken"/> (+ optional access token).</summary>
    public static string TokenResponseJson(string idToken, string? accessToken = "opaque-access-token")
    {
        var accessLine = accessToken != null ? $"\"access_token\": \"{accessToken}\", " : string.Empty;
        return $$"""
            { {{accessLine}}"id_token": "{{idToken}}", "token_type": "Bearer", "expires_in": 3600 }
            """;
    }

    /// <summary>An RS256-signed compact JWT.</summary>
    public static string SignRs256(
        RsaSecurityKey key,
        string issuer,
        string? audience,
        IEnumerable<Claim>? claims = null,
        DateTime? expires = null)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: (claims ?? Enumerable.Empty<Claim>()).ToArray(),
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Convenience: a typical id_token (sub/username/name/email/email_verified + nonce + optional groups).</summary>
    public static string IdToken(
        RsaSecurityKey key,
        string issuer,
        string audience,
        string nonce,
        string subject = "sub-123",
        string username = "alice",
        string? email = "alice@example.com",
        bool emailVerified = true,
        IEnumerable<string>? groups = null,
        string? pictureUrl = null)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("preferred_username", username),
            new("name", username),
            new("nonce", nonce),
        };
        if (email != null)
        {
            claims.Add(new Claim("email", email));
            claims.Add(new Claim("email_verified", emailVerified ? "true" : "false", ClaimValueTypes.Boolean));
        }

        if (groups != null)
        {
            claims.AddRange(groups.Select(g => new Claim("groups", g)));
        }

        if (pictureUrl != null)
        {
            claims.Add(new Claim("picture", pictureUrl));
        }

        return SignRs256(key, issuer, audience, claims);
    }
}
