using System;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

public class UserSyncService
{
    private readonly IUserManager _userManager;
    private readonly RbacService _rbacService;
    private readonly ILogger<UserSyncService> _logger;

    public UserSyncService(
        IUserManager userManager,
        RbacService rbacService,
        ILogger<UserSyncService> logger)
    {
        _userManager = userManager;
        _rbacService = rbacService;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the Jellyfin user exists and is up-to-date with the OIDC identity.
    /// Creates the user if auto-creation is enabled and the account does not exist.
    /// Serves as the migration path for local users moving to SSO.
    /// </summary>
    public async Task<Guid> SyncUserAsync(string username, string? displayName)
    {
        var config = OidcPlugin.Instance?.Configuration;
        var oidcProviderId = typeof(Auth.OidcAuthProvider).FullName!;
        var syncDisplayName = config?.SyncDisplayName == true && !string.IsNullOrWhiteSpace(displayName);

        // Primary lookup by OIDC username. If display name sync is on and the lookup
        // misses (user was renamed to their display name on a previous login), fall back.
        var user = _userManager.GetUserByName(username);
        if (user == null && syncDisplayName && displayName != username)
        {
            user = _userManager.GetUserByName(displayName!);
        }

        if (user == null)
        {
            if (config?.AutoCreateUsers != true)
            {
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            // New users: use display name as the Jellyfin username when sync is on,
            // so the account shows the friendly name from day one.
            var newUsername = syncDisplayName ? displayName! : username;
            user = await _userManager.CreateUserAsync(newUsername).ConfigureAwait(false);
            user.AuthenticationProviderId = oidcProviderId;
            user.SetPermission(PermissionKind.IsDisabled, false);
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            _logger.LogInformation("Created new OIDC user: {Username}", newUsername);
        }
        else
        {
            // Existing user — respect the disabled flag set by a Jellyfin admin.
            if (user.HasPermission(PermissionKind.IsDisabled))
            {
                throw new InvalidOperationException(
                    $"User '{user.Username}' is disabled in Jellyfin. Remove them from the IdP or re-enable them in Jellyfin.");
            }

            bool needsSave = false;

            // Migrate auth provider on first SSO login when opt-in is on.
            if (config?.MigrateLocalUsers == true
                && !string.Equals(user.AuthenticationProviderId, oidcProviderId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Migrating user {Username} from {OldProvider} to OidcAuthProvider",
                    user.Username, user.AuthenticationProviderId ?? "none");
                user.AuthenticationProviderId = oidcProviderId;
                needsSave = true;
            }
            else if (!config?.MigrateLocalUsers == true)
            {
                _logger.LogDebug(
                    "User {Username} has provider {Provider}; migration disabled — skipping",
                    user.Username, user.AuthenticationProviderId ?? "none");
            }

            if (needsSave)
            {
                await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }

            // Sync display name — renames the Jellyfin account to match the IdP name.
            if (syncDisplayName
                && !string.Equals(user.Username, displayName, StringComparison.OrdinalIgnoreCase))
            {
                var oldName = user.Username;
                try
                {
                    await _userManager.RenameUser(user, displayName!).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Updated display name for user {OldName} → {NewName}", oldName, displayName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Could not rename user {OldName} to {NewName} — name may already be taken",
                        oldName, displayName);
                }
            }
        }

        _logger.LogDebug("Synced OIDC user: username={Username}, displayName={DisplayName}",
            username, displayName ?? "(none)");

        return user.Id;
    }

    public Task ApplyRolesAsync(Guid userId, string[] roles)
        => _rbacService.ApplyRoleMappingsAsync(userId, roles);
}
