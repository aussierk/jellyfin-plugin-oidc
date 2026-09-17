using System;
using System.Linq;

namespace Jellyfin.Plugin.OIDC.Services;

/// Maps IDX token-validation error codes to hints safe to show in the browser - the raw
/// exception (which can carry issuer/key detail) stays server-log-only.
public static class TokenFailureDescriber
{
    private static readonly (string[] Codes, string Hint)[] Hints =
    [
        (["IDX10249"], "the identity provider's signing certificate has expired"),
        (["IDX10214"], "the token audience doesn't match this provider's Client ID"),
        (["IDX10205"], "the token issuer doesn't match the configured Issuer URL - re-run Test Connection"),
        (["IDX10223", "IDX10222", "IDX10225"],
            "the token isn't within its valid time window - check for clock drift between this server and the identity provider"),
        (["IDX10500", "IDX10501", "IDX10503", "IDX10511"],
            "no matching signing key was found - the identity provider's keys may have rotated; re-run Test Connection"),
        (["IDX10517", "IDX10518"], "the token wasn't signed with a supported algorithm - check the identity provider's signing-key configuration")
    ];

    /// Null when <paramref name="error"/> matches no known IDX code.
    public static string? Describe(string? error)
        => string.IsNullOrEmpty(error)
            ? null
            : Hints.FirstOrDefault(h => h.Codes.Any(error.Contains)).Hint;
}
