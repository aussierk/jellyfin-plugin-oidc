using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.OIDC.Services;

public static class ClaimParser
{
    ///
    /// Extracts every value of a claim addressed by a dot-separated path.
    /// Supports nested JSON objects (e.g. "realm_access.roles"), repeated claims, and a single
    /// claim whose value is a JSON string array (e.g. Entra's <c>emails</c>).
    ///
    public static string[] ExtractClaimValues(JwtSecurityToken token, string claimPath)
    {
        if (string.IsNullOrWhiteSpace(claimPath))
        {
            return Array.Empty<string>();
        }

        var parts = claimPath.Split('.');
        return parts.Length == 1
            ? ExtractFromFlatClaim(token, claimPath)
            : ExtractFromNestedClaim(token, parts);
    }

    /// Roles from a JWT using a dot-separated claim path - see <see cref="ExtractClaimValues"/>.
    public static string[] ExtractRoles(JwtSecurityToken token, string roleClaim)
        => ExtractClaimValues(token, roleClaim);

    /// The first non-empty value of a claim path (see <see cref="ExtractClaimValues"/>), or <c>""</c>.
    public static string ExtractFirstClaim(JwtSecurityToken token, string claimPath)
        => Array.Find(ExtractClaimValues(token, claimPath), v => !string.IsNullOrEmpty(v)) ?? string.Empty;

    /// Same path semantics as <see cref="ExtractClaimValues"/>, applied to a raw JSON body (e.g. userinfo).
    public static string[] ExtractClaimValuesFromJson(string? json, string claimPath)
        => string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(claimPath)
            ? Array.Empty<string>()
            : WalkJson(json, claimPath.Split('.'));

    /// Roles from a JSON body using a dot-separated claim path - see <see cref="ExtractClaimValuesFromJson"/>.
    public static string[] ExtractRolesFromJson(string? json, string roleClaim)
        => ExtractClaimValuesFromJson(json, roleClaim);

    /// The first non-empty value of a claim path in a raw JSON body - see <see cref="ExtractClaimValuesFromJson"/>.
    public static string ExtractFirstClaimFromJson(string? json, string claimPath)
        => Array.Find(ExtractClaimValuesFromJson(json, claimPath), v => !string.IsNullOrEmpty(v)) ?? string.Empty;

    /// True when the claim in a raw JSON body is truthy - see <see cref="ExtractBool(JwtSecurityToken, string)"/>.
    public static bool ExtractBoolFromJson(string? json, string claimType)
        => IsTruthy(ExtractFirstClaimFromJson(json, claimType));

    public static string ExtractClaim(JwtSecurityToken token, string claimType)
    {
        return token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value ?? string.Empty;
    }

    /// True when the claim is truthy - <c>true</c> (case-insensitive) or the integer <c>1</c>,
    /// covering IdPs that emit e.g. <c>email_verified</c> as a JSON boolean or a number. Any
    /// other value (including a missing claim) is false, so the gate fails closed.
    public static bool ExtractBool(JwtSecurityToken token, string claimType)
        => IsTruthy(ExtractClaim(token, claimType));

    private static bool IsTruthy(string value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    /// Shortens a <c>sub</c> for audit logs - enough tail to correlate without logging the full identifier.
    public static string RedactSubject(string? sub)
        => string.IsNullOrEmpty(sub) ? "(none)" : (sub.Length <= 8 ? sub : "…" + sub[^6..]);

    private static string[] ExtractFromFlatClaim(JwtSecurityToken token, string claimType)
    {
        var claims = token.Claims.Where(c => c.Type == claimType).ToArray();

        if (claims.Length == 0)
        {
            return Array.Empty<string>();
        }

        if (claims.Length > 1)
        {
            return claims.Select(c => c.Value).ToArray();
        }

        return ExpandJsonStringArray(claims[0].Value);
    }

    private static string[] ExtractFromNestedClaim(JwtSecurityToken token, string[] pathParts)
    {
        var rootClaim = token.Claims.FirstOrDefault(c => c.Type == pathParts[0])?.Value;
        if (!string.IsNullOrEmpty(rootClaim))
        {
            // The first path segment is a claim whose value is itself a JSON object.
            return WalkJson(rootClaim, pathParts.Skip(1).ToArray());
        }

        // Path flattened differently or absent, so walk the whole payload as JSON. Payload round-trips
        // nested structures for parsed and hand-built tokens alike, so no need to decode RawPayload.
        return WalkJson(token.Payload.SerializeToJson(), pathParts);
    }

    /// Parses <paramref name="json"/> and walks <paramref name="path"/>; returns [] on any malformed input.
    private static string[] WalkJson(string json, string[] path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return WalkJsonPath(doc.RootElement, path);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string[] WalkJsonPath(JsonElement element, string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return Array.Empty<string>();
            }

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.Array => StringElements(current),
            JsonValueKind.String => ExpandJsonStringArray(current.GetString()!),

            // email_verified is often a JSON literal, not a string.
            JsonValueKind.True or JsonValueKind.False => [current.GetBoolean() ? "true" : "false"],
            JsonValueKind.Number => [current.GetRawText()],
            _ => Array.Empty<string>()
        };
    }

    /// The string elements of a JSON array, non-string entries skipped.
    private static string[] StringElements(JsonElement array)
        => array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();

    /// <paramref name="value"/> expanded to its elements when it is a JSON array of strings
    /// (e.g. Entra's stringified <c>emails</c>); otherwise the single value, unchanged.
    private static string[] ExpandJsonStringArray(string value)
        => value.TrimStart().StartsWith('[') && ParseJsonStringArray(value) is { Length: > 0 } arr
            ? arr
            : [value];

    private static string[] ParseJsonStringArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return StringElements(doc.RootElement);
            }
        }
        catch (JsonException)
        {
            // Not valid JSON
        }

        return Array.Empty<string>();
    }
}
