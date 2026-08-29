using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class ClaimParserTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// Builds a JwtSecurityToken with the given claims.
    private static JwtSecurityToken Token(params Claim[] claims)
        => new(claims: claims);

    /// Builds a token with a real Base64URL-encoded JSON payload (so ExtractFromNestedClaim works).
    private static JwtSecurityToken TokenWithPayload(object payloadObject)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payloadObject);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var b64 = Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    // ── ExtractBool ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]     // some IdPs emit email_verified as an integer
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public void ExtractBool_TreatsTrueAndOneAsTruthy(string value, bool expected)
        => Assert.Equal(expected, ClaimParser.ExtractBool(Token(new Claim("email_verified", value)), "email_verified"));

    [Fact]
    public void ExtractBool_MissingClaim_ReturnsFalse()
        => Assert.False(ClaimParser.ExtractBool(Token(new Claim("sub", "u")), "email_verified"));

    // ── ExtractClaimValues / ExtractFirstClaim (e.g. Entra "emails") ──────────

    [Fact]
    public void ExtractClaimValues_RepeatedClaims_ReturnsAll()
        => Assert.Equal(
            new[] { "a@x.com", "b@x.com" },
            ClaimParser.ExtractClaimValues(Token(new Claim("emails", "a@x.com"), new Claim("emails", "b@x.com")), "emails"));

    [Fact]
    public void ExtractClaimValues_SingleStringifiedArray_IsExpanded()
        => Assert.Equal(
            new[] { "a@x.com", "b@x.com" },
            ClaimParser.ExtractClaimValues(Token(new Claim("emails", "[\"a@x.com\",\"b@x.com\"]")), "emails"));

    [Fact]
    public void ExtractClaimValues_NestedPath_ReturnsValues()
        => Assert.Equal(
            new[] { "u@corp.com" },
            ClaimParser.ExtractClaimValues(TokenWithPayload(new { user = new { emails = new[] { "u@corp.com" } } }), "user.emails"));

    [Fact]
    public void ExtractClaimValues_Missing_ReturnsEmpty()
        => Assert.Empty(ClaimParser.ExtractClaimValues(Token(new Claim("sub", "x")), "emails"));

    [Fact]
    public void ExtractFirstClaim_ReturnsFirstNonEmpty()
        => Assert.Equal(
            "b@x.com",
            ClaimParser.ExtractFirstClaim(Token(new Claim("emails", ""), new Claim("emails", "b@x.com")), "emails"));

    [Fact]
    public void ExtractFirstClaim_Missing_ReturnsEmptyString()
        => Assert.Equal(string.Empty, ClaimParser.ExtractFirstClaim(Token(new Claim("sub", "x")), "emails"));

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
        // A token whose payload can't be read as claims should not throw - just return empty.
        var token = new JwtSecurityToken(claims: []);
        Assert.Empty(ClaimParser.ExtractRoles(token, "realm_access.roles"));
    }

    [Fact]
    public void ExtractRoles_JsonArrayWithNonStringElements_SkipsNonStrings()
    {
        // Non-string array elements must be silently ignored.
        var token = TokenWithPayload(new
        {
            realm_access = new { roles = new object[] { "admin", 42 } }
        });

        var roles = ClaimParser.ExtractRoles(token, "realm_access.roles");

        Assert.Single(roles);
        Assert.Equal("admin", roles[0]);
    }

    // ── ExtractRolesFromJson (userinfo endpoint body) ─────────────────────────

    [Fact]
    public void ExtractRolesFromJson_FlatArray_ReturnsAllRoles()
        => Assert.Equal(
            new[] { "admin", "user" },
            ClaimParser.ExtractRolesFromJson("{\"groups\":[\"admin\",\"user\"]}", "groups"));

    [Fact]
    public void ExtractRolesFromJson_NestedPath_ReturnsRoles()
        => Assert.Equal(
            new[] { "admin" },
            ClaimParser.ExtractRolesFromJson("{\"realm_access\":{\"roles\":[\"admin\"]}}", "realm_access.roles"));

    [Fact]
    public void ExtractRolesFromJson_SingleStringValue_ReturnsSingleRole()
        => Assert.Equal(
            new[] { "only-role" },
            ClaimParser.ExtractRolesFromJson("{\"role\":\"only-role\"}", "role"));

    [Fact]
    public void ExtractRolesFromJson_StringifiedJsonArray_IsExpanded()
        => Assert.Equal(
            new[] { "a", "b" },
            ClaimParser.ExtractRolesFromJson("{\"groups\":\"[\\\"a\\\",\\\"b\\\"]\"}", "groups"));

    [Fact]
    public void ExtractRolesFromJson_MissingPath_ReturnsEmpty()
        => Assert.Empty(ClaimParser.ExtractRolesFromJson("{\"sub\":\"x\"}", "groups"));

    [Fact]
    public void ExtractRolesFromJson_MissingNestedSegment_ReturnsEmpty()
        => Assert.Empty(ClaimParser.ExtractRolesFromJson("{\"realm_access\":{\"other\":1}}", "realm_access.roles"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[\"array\",\"root\"]")]
    public void ExtractRolesFromJson_UnusableBody_ReturnsEmpty(string? json)
        => Assert.Empty(ClaimParser.ExtractRolesFromJson(json, "groups"));

    [Fact]
    public void ExtractRolesFromJson_EmptyRoleClaim_ReturnsEmpty()
        => Assert.Empty(ClaimParser.ExtractRolesFromJson("{\"groups\":[\"a\"]}", ""));

    [Fact]
    public void ExtractRolesFromJson_ArrayWithNonStrings_SkipsThem()
        => Assert.Equal(
            new[] { "admin" },
            ClaimParser.ExtractRolesFromJson("{\"groups\":[\"admin\",42,null]}", "groups"));
}
