using System;
using System.Linq;
using Jellyfin.Plugin.OIDC.Configuration;

namespace Jellyfin.Plugin.OIDC.Services;

///
/// The server-wide admission gate: given a user's roles and (verified) email, decides whether
/// they may sign in at all, independent of any per-provider RBAC role mapping. Shared by the
/// interactive callback and the periodic role/admission re-sync task so both apply the same rule.
///
public static class AdmissionService
{
    ///
    /// Returns a deny reason, or <c>null</c> when the user matches any active allowlist rule
    /// (or no rule is set). A domain rule matches the exact domain or any subdomain of it
    /// (<c>user@mail.example.com</c> satisfies a rule of <c>example.com</c>).
    ///
    public static string? Evaluate(PluginConfiguration cfg, string[] roles, string email, bool emailVerified)
    {
        if (cfg.RequireVerifiedEmail && !emailVerified)
        {
            return "email-not-verified";
        }

        var hasGroupRule = cfg.AllowedGroups.Count > 0;

        // Email/domain rules are inert unless email is verified - see RequireVerifiedEmail.
        var emailRulesActive = cfg.RequireVerifiedEmail;
        var hasEmailRule = emailRulesActive && cfg.AllowedEmails.Count > 0;
        var hasDomainRule = emailRulesActive && cfg.AllowedEmailDomains.Count > 0;

        if (!hasGroupRule && !hasEmailRule && !hasDomainRule)
        {
            return null;
        }

        if (hasGroupRule && roles.Any(r => cfg.AllowedGroups.Contains(r, StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(email))
        {
            if (hasEmailRule && cfg.AllowedEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            var at = email.LastIndexOf('@');
            var domain = at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : string.Empty;
            if (hasDomainRule && domain.Length > 0 && cfg.AllowedEmailDomains.Any(rule =>
                    !string.IsNullOrEmpty(rule)
                    && (domain.Equals(rule, StringComparison.OrdinalIgnoreCase)
                        || domain.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase))))
            {
                return null;
            }
        }

        return "not-on-allowlist";
    }
}
