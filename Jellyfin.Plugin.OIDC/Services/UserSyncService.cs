using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

public class UserSyncService
{
    private const int MaxUsernameLength = 255;

    private readonly IUserManager _userManager;
    private readonly RbacService _rbacService;
    private readonly ProfileImageService _profileImageService;
    private readonly ILogger<UserSyncService> _logger;

    public UserSyncService(
        IUserManager userManager,
        RbacService rbacService,
        ProfileImageService profileImageService,
        ILogger<UserSyncService> logger)
    {
        _userManager = userManager;
        _rbacService = rbacService;
        _profileImageService = profileImageService;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the Jellyfin user exists and is bound to this OIDC identity. Resolution order:
    /// the stable <c>sub</c> (via <see cref="UserProviderEntry"/>), then a verified-email link
    /// (opt-in), then the username claim. Legacy username-only rows self-heal on first login.
    /// Cross-provider collisions and disabled accounts are rejected.
    /// </summary>
    public async Task<Guid> SyncUserAsync(
        string username,
        string? displayName,
        string? subject,
        string? email,
        bool emailVerified,
        string providerId)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username from IdP token is empty.");
        }

        if (username.Length > MaxUsernameLength)
        {
            throw new InvalidOperationException(
                $"Username '{username[..20]}...' exceeds the maximum allowed length of {MaxUsernameLength} characters.");
        }

        if (username.Any(char.IsControl))
        {
            throw new InvalidOperationException("Username from IdP token contains invalid control characters.");
        }

        var config = OidcPlugin.Instance?.Configuration;
        var oidcProviderId = typeof(Auth.OidcAuthProvider).FullName!;
        var sub = (subject ?? string.Empty).Trim();
        var mail = (email ?? string.Empty).Trim();

        // 1. sub-keyed lookup.
        var entry = (config != null && sub.Length > 0)
            ? config.UserProviderMap.FirstOrDefault(e =>
                string.Equals(e.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Subject, sub, StringComparison.Ordinal))
            : null;

        var user = entry != null
            ? ((Guid.TryParse(entry.UserId, out var uid) ? _userManager.GetUserById(uid) : null)
               ?? _userManager.GetUserByName(entry.Username))
            : null;

        // 2. opt-in email link (only for a verified email that didn't resolve by sub).
        if (user == null && config?.LinkExistingUsersByEmail == true && emailVerified && mail.Length > 0)
        {
            var byEmail = config.UserProviderMap.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.Email) && string.Equals(e.Email, mail, StringComparison.OrdinalIgnoreCase));
            if (byEmail != null)
            {
                user = (Guid.TryParse(byEmail.UserId, out var eid) ? _userManager.GetUserById(eid) : null)
                       ?? _userManager.GetUserByName(byEmail.Username);
                if (user != null)
                {
                    _logger.LogInformation(
                        "Linked OIDC login to existing user {Username} by verified email (provider={Provider})",
                        user.Username, providerId);
                }
            }
        }

        // 3. username claim.
        user ??= _userManager.GetUserByName(username);

        if (user == null)
        {
            if (config?.AutoCreateUsers != true)
            {
                _logger.LogWarning(
                    "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=auto-create-disabled",
                    providerId, Redact(sub), username);
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);
            user.AuthenticationProviderId = oidcProviderId;
            user.SetPermission(PermissionKind.IsDisabled, false);
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            if (config != null)
            {
                UpsertOwnership(config, sub, user.Username, user.Id, providerId, mail);
            }

            _logger.LogInformation(
                "Created new OIDC user: {Username} (provider={Provider}, subject={Subject})",
                user.Username, providerId, Redact(sub));
        }
        else
        {
            if (user.HasPermission(PermissionKind.IsDisabled))
            {
                _logger.LogWarning(
                    "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=account-disabled",
                    providerId, Redact(sub), user.Username);
                throw new InvalidOperationException(
                    $"User '{user.Username}' is disabled in Jellyfin. Remove them from the IdP or re-enable them in Jellyfin.");
            }

            var isOidcUser = string.Equals(user.AuthenticationProviderId, oidcProviderId, StringComparison.Ordinal);

            if (!isOidcUser)
            {
                if (config?.MigrateLocalUsers == true)
                {
                    _logger.LogInformation(
                        "Migrating user {Username} from {OldProvider} to OidcAuthProvider (provider={Provider})",
                        user.Username, user.AuthenticationProviderId ?? "none", providerId);
                    user.AuthenticationProviderId = oidcProviderId;
                    await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

                    if (config != null)
                    {
                        UpsertOwnership(config, sub, user.Username, user.Id, providerId, mail);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Login blocked: user '{Username}' exists as a local account and MigrateLocalUsers is disabled.",
                        user.Username);
                    throw new InvalidOperationException(
                        $"User '{user.Username}' is a local account. Enable MigrateLocalUsers to allow OIDC login for this account.");
                }
            }
            else if (config != null)
            {
                // Existing OIDC user: confirm provider ownership, then heal / add the row.
                var owned = config.UserProviderMap.FirstOrDefault(e =>
                    (sub.Length > 0 && string.Equals(e.Subject, sub, StringComparison.Ordinal))
                    || string.Equals(e.Username, user.Username, StringComparison.OrdinalIgnoreCase));

                if (owned != null
                    && !string.IsNullOrEmpty(owned.ProviderId)
                    && !string.Equals(owned.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Login blocked: user '{Username}' was created by provider '{RegisteredProvider}' " +
                        "but login was attempted via provider '{AttemptedProvider}'.",
                        user.Username, owned.ProviderId, providerId);
                    throw new InvalidOperationException(
                        $"User '{user.Username}' is registered to a different OIDC provider. " +
                        "Use the correct provider or contact an administrator.");
                }

                if (owned == null)
                {
                    UpsertOwnership(config, sub, user.Username, user.Id, providerId, mail);
                }
                else if (string.IsNullOrEmpty(owned.Subject) || string.IsNullOrEmpty(owned.UserId)
                         || (mail.Length > 0 && !string.Equals(owned.Email, mail, StringComparison.OrdinalIgnoreCase)))
                {
                    // Back-fill a legacy row / refresh the last-seen email.
                    owned.Subject = sub.Length > 0 ? sub : owned.Subject;
                    owned.UserId = user.Id.ToString();
                    owned.Username = user.Username;
                    if (mail.Length > 0)
                    {
                        owned.Email = mail;
                    }

                    OidcPlugin.Instance?.SaveConfiguration();
                }
            }
        }

        await ApplyDisplayNameAsync(user, displayName, config, providerId).ConfigureAwait(false);

        _logger.LogDebug(
            "Synced OIDC user: username={Username}, subject={Subject}, provider={Provider}",
            user.Username, Redact(sub), providerId);

        return user.Id;
    }

    public Task ApplyRolesAsync(Guid userId, string[] roles, string providerId)
        => _rbacService.ApplyRoleMappingsAsync(userId, roles, providerId);

    public Task ApplyProfileImageAsync(Guid userId, string? pictureUrl, string providerId)
        => _profileImageService.ApplyProfileImageAsync(userId, pictureUrl, providerId);

    private async Task ApplyDisplayNameAsync(
        Jellyfin.Database.Implementations.Entities.User user,
        string? displayName,
        PluginConfiguration? config,
        string providerId)
    {
        var providerCfg = config?.Providers
            .FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        if (providerCfg?.SyncDisplayName != true || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var desired = SanitizeUsername(displayName);
        if (desired.Length == 0 || string.Equals(desired, user.Username, StringComparison.Ordinal))
        {
            return;
        }

        var old = user.Username;
        try
        {
            await _userManager.RenameUser(user.Id, old, desired).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "Skipped OIDC display-name rename '{Old}' -> '{Desired}': {Message}", old, desired, ex.Message);
            return;
        }

        var row = config!.UserProviderMap.FirstOrDefault(e =>
                      string.Equals(e.UserId, user.Id.ToString(), StringComparison.Ordinal))
                  ?? config.UserProviderMap.FirstOrDefault(e =>
                      string.Equals(e.Username, old, StringComparison.OrdinalIgnoreCase));
        if (row != null)
        {
            row.Username = desired;
            OidcPlugin.Instance?.SaveConfiguration();
        }

        _logger.LogInformation(
            "Renamed Jellyfin user '{Old}' -> '{New}' from OIDC display name (provider={Provider})",
            old, desired, providerId);
    }

    private static void UpsertOwnership(
        PluginConfiguration config, string subject, string username, Guid userId, string providerId, string email)
    {
        config.UserProviderMap.RemoveAll(e =>
            string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase)
            || (subject.Length > 0
                && string.Equals(e.Subject, subject, StringComparison.Ordinal)
                && string.Equals(e.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));

        config.UserProviderMap.Add(new UserProviderEntry
        {
            Username = username,
            ProviderId = providerId,
            Subject = subject,
            UserId = userId.ToString(),
            Email = email
        });

        OidcPlugin.Instance?.SaveConfiguration();
    }

    /// <summary>Fold a display-name claim to Jellyfin's allowed username charset and length.</summary>
    private static string SanitizeUsername(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '.' or '_' or '@' or '+' ? ch : ' ');
        }

        var s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return s.Length > MaxUsernameLength ? s[..MaxUsernameLength].Trim() : s;
    }

    /// <summary>Keep the tail of a subject for correlation without logging the whole identifier.</summary>
    private static string Redact(string sub)
        => string.IsNullOrEmpty(sub) ? "(none)" : (sub.Length <= 8 ? sub : "…" + sub[^6..]);
}
