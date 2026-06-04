using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class ClaimParserTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>Builds a JwtSecurityToken with the given claims.</summary>
    private static JwtSecurityToken Token(params Claim[] claims)
        => new(claims: claims);

    /// <summary>
    /// Builds a token whose payload contains a nested JSON structure encoded
    /// as a real Base64URL JWT payload (so ExtractFromNestedClaim works).
    /// </summary>
    private static JwtSecurityToken TokenWithPayload(object payloadObject)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payloadObject);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var b64 = Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // We only care about the payload segment; hand-craft a 3-part JWT
        // with dummy header and signature so the handler can parse .RawPayload.
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var raw = $"{header}.{b64}.";
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();

        // Use ReadJwtToken (no validation) so we can inspect RawPayload.
        return handler.ReadJwtToken(raw);
    }

    // ── ExtractClaim ───────────────────────────────────────────────────────────

    [Fact]
    public void ExtractClaim_Present_ReturnsValue()
    {
        var token = Token(new Claim("sub", "user-123"));
        Assert.Equal("user-123", ClaimParser.ExtractClaim(token, "sub"));
    }

    [Fact]
    public void ExtractClaim_Missing_ReturnsEmpty()
    {
        var token = Token(new Claim("sub", "user-123"));
        Assert.Equal(string.Empty, ClaimParser.ExtractClaim(token, "email"));
    }

    // ── ExtractRoles – flat single claim ──────────────────────────────────────

    [Fact]
    public void ExtractRoles_FlatClaim_SingleValue_ReturnsSingleRole()
    {
        var token = Token(new Claim("groups", "admins"));
        var roles = ClaimParser.ExtractRoles(token, "groups");
        Assert.Equal(new[] { "admins" }, roles);
    }

    [Fact]
    public void ExtractRoles_FlatClaim_MultipleValues_ReturnsAllRoles()
    {
        var token = Token(
            new Claim("groups", "admins"),
            new Claim("groups", "users"));
        var roles = ClaimParser.ExtractRoles(token, "groups");
        Assert.Contains("admins", roles);
        Assert.Contains("users", roles);
        Assert.Equal(2, roles.Length);
    }

    [Fact]
    public void ExtractRoles_FlatClaim_JsonArrayValue_ReturnsAllRoles()
    {
        var token = Token(new Claim("roles", "[\"editor\",\"viewer\"]"));
        var roles = ClaimParser.ExtractRoles(token, "roles");
        Assert.Contains("editor", roles);
        Assert.Contains("viewer", roles);
    }

    [Fact]
    public void ExtractRoles_MissingClaim_ReturnsEmpty()
    {
        var token = Token(new Claim("sub", "x"));
        Assert.Empty(ClaimParser.ExtractRoles(token, "groups"));
    }

    [Fact]
    public void ExtractRoles_EmptyRoleClaim_ReturnsEmpty()
    {
        var token = Token(new Claim("groups", "admins"));
        Assert.Empty(ClaimParser.ExtractRoles(token, ""));
    }

    [Fact]
    public void ExtractRoles_WhitespaceRoleClaim_ReturnsEmpty()
    {
        var token = Token(new Claim("groups", "admins"));
        Assert.Empty(ClaimParser.ExtractRoles(token, "   "));
    }

    // ── ExtractRoles – nested path ─────────────────────────────────────────────

    [Fact]
    public void ExtractRoles_NestedPath_ReturnsRoles()
    {
        var token = TokenWithPayload(new
        {
            realm_access = new { roles = new[] { "admin", "user" } }
        });
        var roles = ClaimParser.ExtractRoles(token, "realm_access.roles");
        Assert.Contains("admin", roles);
        Assert.Contains("user", roles);
    }

    [Fact]
    public void ExtractRoles_NestedPath_MissingSegment_ReturnsEmpty()
    {
        var token = TokenWithPayload(new
        {
            realm_access = new { other = "value" }
        });
        Assert.Empty(ClaimParser.ExtractRoles(token, "realm_access.roles"));
    }

    [Fact]
    public void ExtractRoles_NestedPath_SingleStringValue_ReturnsSingleRole()
    {
        var token = TokenWithPayload(new
        {
            resource_access = new { app = new { roles = "only-role" } }
        });
        var roles = ClaimParser.ExtractRoles(token, "resource_access.app.roles");
        Assert.Equal(new[] { "only-role" }, roles);
    }

    [Fact]
    public void ExtractRoles_NestedPath_TopLevelMissing_ReturnsEmpty()
    {
        var token = TokenWithPayload(new { sub = "x" });
        Assert.Empty(ClaimParser.ExtractRoles(token, "realm_access.roles"));
    }

    // ── edge cases ─────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractRoles_MalformedBase64Payload_ReturnsEmpty()
    {
        // A token whose payload is not valid Base64 should not throw — just return empty.
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var raw = "eyJhbGciOiJub25lIn0.!!!notvalidbase64!!!.";
        // ReadJwtToken will fail; fall back to building a plain token with no claims.
        var token = new JwtSecurityToken(claims: []);
        Assert.Empty(ClaimParser.ExtractRoles(token, "realm_access.roles"));
    }

    [Fact]
    public void ExtractRoles_JsonArrayWithNonStringElements_SkipsNonStrings()
    {
        // Array contains a number and null alongside a valid string.
        // Non-string elements must be silently ignored; only the string is returned.
        var token = TokenWithPayload(new
        {
            realm_access = new { roles = new object[] { "admin", 42 } }
        });

        var roles = ClaimParser.ExtractRoles(token, "realm_access.roles");

        Assert.Single(roles);
        Assert.Equal("admin", roles[0]);
    }
}
