using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.OIDC.Services;

public static class ClaimParser
{
    /// <summary>
    /// Extracts roles from a JWT using a dot-separated claim path.
    /// Supports nested JSON objects (e.g. "realm_access.roles") and flat claim arrays.
    /// </summary>
    public static string[] ExtractRoles(JwtSecurityToken token, string roleClaim)
    {
        if (string.IsNullOrWhiteSpace(roleClaim))
        {
            return Array.Empty<string>();
        }

        var parts = roleClaim.Split('.');

        // Try flat claim first (single segment like "roles" or "groups")
        if (parts.Length == 1)
        {
            return ExtractFromFlatClaim(token, roleClaim);
        }

        // Nested path: walk the JSON payload
        return ExtractFromNestedClaim(token, parts);
    }

    public static string ExtractClaim(JwtSecurityToken token, string claimType)
    {
        return token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value ?? string.Empty;
    }

    private static string[] ExtractFromFlatClaim(JwtSecurityToken token, string claimType)
    {
        var claims = token.Claims.Where(c => c.Type == claimType).ToArray();

        if (claims.Length == 0)
        {
            return Array.Empty<string>();
        }

        // Multiple separate claim values (standard multi-value encoding) → return them all.
        if (claims.Length > 1)
        {
            return claims.Select(c => c.Value).ToArray();
        }

        // Single claim: if its value is a JSON array, parse and expand it.
        var singleValue = claims[0].Value;
        if (singleValue.TrimStart().StartsWith('['))
        {
            var parsed = ParseJsonStringArray(singleValue);
            if (parsed.Length > 0)
            {
                return parsed;
            }
        }

        return [singleValue];
    }

    private static string[] ExtractFromNestedClaim(JwtSecurityToken token, string[] pathParts)
    {
        // The root claim is the first segment
        var rootClaim = token.Claims.FirstOrDefault(c => c.Type == pathParts[0])?.Value;
        if (string.IsNullOrEmpty(rootClaim))
        {
            // Try to reconstruct from the raw payload
            try
            {
                using var doc = JsonDocument.Parse(
                    Base64UrlDecode(token.RawPayload));
                return WalkJsonPath(doc.RootElement, pathParts);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        // If root claim is JSON, parse and walk
        try
        {
            using var doc = JsonDocument.Parse(rootClaim);
            var remaining = pathParts.Skip(1).ToArray();
            return WalkJsonPath(doc.RootElement, remaining);
        }
        catch
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

        if (current.ValueKind == JsonValueKind.Array)
        {
            return current.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();
        }

        if (current.ValueKind == JsonValueKind.String)
        {
            return new[] { current.GetString()! };
        }

        return Array.Empty<string>();
    }

    private static string[] ParseJsonStringArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray();
            }
        }
        catch
        {
            // Not valid JSON
        }

        return Array.Empty<string>();
    }

    private static string Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        var bytes = Convert.FromBase64String(padded);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
