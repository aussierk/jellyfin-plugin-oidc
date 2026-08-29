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
    private readonly UserProviderMapStore _mapStore;
    private readonly ILogger<UserSyncService> _logger;

    public UserSyncService(
        IUserManager userManager,
        RbacService rbacService,
        ProfileImageService profileImageService,
        UserProviderMapStore mapStore,
        ILogger<UserSyncService> logger)
    {
        _userManager = userManager;
        _rbacService = rbacService;
        _profileImageService = profileImageService;
        _mapStore = mapStore;
        _logger = logger;
    }

    /// Ensures the Jellyfin user exists and is bound to this identity, resolving by sub, then verified email, then username.
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

        // '/' and '\' aren't rejected by CreateUserAsync/Jellyfin's own validation until deeper in
        // the framework, where the failure surfaces as an ArgumentException - caught by the generic
        // handler at the controller call site and returned as a 500, logged as an error, even though
        // this is a deterministic bad-input case (a colliding/crafted preferred_username), not a
        // server fault. Reject it here instead so it takes the same clean 403 path as every other
        // rejected username.
        if (username.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new InvalidOperationException("Username from IdP token contains a path separator ('/' or '\\'), which Jellyfin does not allow in usernames.");
        }

        var config = OidcPlugin.CurrentConfig;
        var oidcProviderId = typeof(Auth.OidcAuthProvider).FullName!;
        var sub = (subject ?? string.Empty).Trim();
        var mail = (email ?? string.Empty).Trim();

        var entry = sub.Length > 0 ? _mapStore.FindBySubject(providerId, sub) : null;

        var user = entry != null
            ? ((Guid.TryParse(entry.UserId, out var uid) ? _userManager.GetUserById(uid) : null)
               ?? _userManager.GetUserByName(entry.Username))
            : null;
        var resolvedBySubject = user != null;

        // Opt-in, and only from a TrustedForEmailLinking provider: a verified email is only as
        // trustworthy as the IdP asserting it. Both stored and incoming email must be verified.
        var linkedByEmail = false;
        if (user == null && config.LinkExistingUsersByEmail && emailVerified && mail.Length > 0
            && ProviderTrustedForEmailLinking(config, providerId))
        {
            var byEmail = _mapStore.FindByVerifiedEmail(mail);
            if (byEmail != null)
            {
                var candidate = (Guid.TryParse(byEmail.UserId, out var eid) ? _userManager.GetUserById(eid) : null)
                                ?? _userManager.GetUserByName(byEmail.Username);
                if (candidate != null && candidate.HasPermission(PermissionKind.IsAdministrator))
                {
                    _logger.LogWarning(
                        "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=admin-email-link-refused",
                        providerId, ClaimParser.RedactSubject(sub), candidate.Username);
                    throw new InvalidOperationException(
                        $"'{candidate.Username}' is an administrator account and cannot be linked to an OIDC identity by email. "
                        + "Sign in with the identity already registered to it.");
                }

                if (candidate != null)
                {
                    user = candidate;
                    linkedByEmail = true;
                    _logger.LogInformation(
                        "Linked OIDC login to existing user {Username} by verified email (provider={Provider})",
                        user.Username, providerId);
                }
            }
        }

        user ??= _userManager.GetUserByName(username);

        // A pure username match (no subject match, no vouched-email link) must never land on an
        // administrator: end users can often choose their own username at the IdP.
        if (user != null && !resolvedBySubject && !linkedByEmail
            && user.HasPermission(PermissionKind.IsAdministrator))
        {
            _logger.LogWarning(
                "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=admin-no-nonsub-link",
                providerId, ClaimParser.RedactSubject(sub), user.Username);
            throw new InvalidOperationException(
                $"'{user.Username}' is an administrator account. An OIDC login can only bind to it by its "
                + "registered subject identity, not by a matching username.");
        }

        if (user == null)
        {
            if (!config.AutoCreateUsers)
            {
                _logger.LogWarning(
                    "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=auto-create-disabled",
                    providerId, ClaimParser.RedactSubject(sub), username);
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            // The up-front checks above catch the common cases ('/', '\', control chars, length),
            // but Jellyfin's own username rules can reject more (and can change between versions).
            // Map that to the same clean deny path rather than letting it bubble up as a 500.
            try
            {
                user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(
                    "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=invalid-username",
                    providerId, ClaimParser.RedactSubject(sub), username);
                throw new InvalidOperationException(
                    $"Username '{username}' from the IdP token is not valid for Jellyfin: {ex.Message}", ex);
            }

            user.AuthenticationProviderId = oidcProviderId;
            user.SetPermission(PermissionKind.IsDisabled, false);
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            _mapStore.Upsert(UserProviderMapStore.CreateEntry(user.Username, providerId, sub, user.Id.ToString(), mail, emailVerified));

            _logger.LogInformation(
                "Created new OIDC user: {Username} (provider={Provider}, subject={Subject})",
                user.Username, providerId, ClaimParser.RedactSubject(sub));
        }
        else
        {
            if (user.HasPermission(PermissionKind.IsDisabled))
            {
                _logger.LogWarning(
                    "OIDC audit: decision=deny provider={Provider} subject={Subject} user={User} reason=account-disabled",
                    providerId, ClaimParser.RedactSubject(sub), user.Username);
                throw new InvalidOperationException(
                    $"User '{user.Username}' is disabled in Jellyfin. Remove them from the IdP or re-enable them in Jellyfin.");
            }

            var isOidcUser = string.Equals(user.AuthenticationProviderId, oidcProviderId, StringComparison.Ordinal);

            if (!isOidcUser)
            {
                if (config.MigrateLocalUsers)
                {
                    _logger.LogInformation(
                        "Migrating user {Username} from {OldProvider} to OidcAuthProvider (provider={Provider})",
                        user.Username, user.AuthenticationProviderId ?? "none", providerId);
                    user.AuthenticationProviderId = oidcProviderId;
                    await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

                    _mapStore.Upsert(UserProviderMapStore.CreateEntry(user.Username, providerId, sub, user.Id.ToString(), mail, emailVerified));
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
            else
            {
                // One atomic find-decide-mutate in the store, closing the check-then-write race.
                var bind = _mapStore.BindOidcLogin(
                    providerId, sub, user.Username, user.Id.ToString(), mail, emailVerified, trustLink: linkedByEmail);

                if (bind.Outcome == MapBindOutcome.Conflict)
                {
                    // owned by a different identity, and no verified-email link vouched for it.
                    _logger.LogWarning(
                        "Login blocked: user '{Username}' is already owned by a different {What} " +
                        "(provider={RegisteredProvider}); login attempted via provider={AttemptedProvider} " +
                        "subject={Subject}.",
                        user.Username, bind.MismatchOnProvider ? "provider" : "subject",
                        bind.PreviousProviderId, providerId, ClaimParser.RedactSubject(sub));
                    throw new InvalidOperationException(
                        $"User '{user.Username}' is registered to a different OIDC identity. " +
                        "Use the correct account or contact an administrator.");
                }

                if (bind is { Outcome: MapBindOutcome.Rebound, WasIdentityMismatch: true })
                {
                    _logger.LogInformation(
                        "OIDC audit: decision=migrate-provider provider={Provider} user={User} from={OldProvider}",
                        providerId, user.Username, bind.PreviousProviderId);
                }
            }
        }

        await ApplyDisplayNameAsync(user, displayName, config, providerId).ConfigureAwait(false);

        _logger.LogDebug(
            "Synced OIDC user: username={Username}, subject={Subject}, provider={Provider}",
            user.Username, ClaimParser.RedactSubject(sub), providerId);

        return user.Id;
    }

    public Task ApplyRolesAsync(Guid userId, string[] roles, string providerId)
        => _rbacService.ApplyRoleMappingsAsync(userId, roles, providerId);

    public Task ApplyProfileImageAsync(Guid userId, string? pictureUrl, string providerId)
        => _profileImageService.ApplyProfileImageAsync(userId, pictureUrl, providerId);

    private async Task ApplyDisplayNameAsync(
        Jellyfin.Database.Implementations.Entities.User user,
        string? displayName,
        PluginConfiguration config,
        string providerId)
    {
        var providerCfg = config.FindProvider(providerId);
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

        _mapStore.Rename(user.Id.ToString(), old, desired);

        _logger.LogInformation(
            "Renamed Jellyfin user '{Old}' -> '{New}' from OIDC display name (provider={Provider})",
            old, desired, providerId);
    }

    private static bool ProviderTrustedForEmailLinking(PluginConfiguration config, string providerId)
        => config.FindProvider(providerId)?.TrustedForEmailLinking == true;

    /// Fold a display-name claim to Jellyfin's allowed username charset and length.
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
}
