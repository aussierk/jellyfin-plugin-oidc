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

        var user = _userManager.GetUserByName(username);

        if (user == null)
        {
            if (config?.AutoCreateUsers != true)
            {
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);
            user.AuthenticationProviderId = oidcProviderId;
            user.SetPermission(PermissionKind.IsDisabled, false);
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            _logger.LogInformation("Created new OIDC user: {Username}", username);
        }
        else
        {
            // Respect a disabled flag set by a Jellyfin admin — never re-enable it here.
            if (user.HasPermission(PermissionKind.IsDisabled))
            {
                throw new InvalidOperationException(
                    $"User '{user.Username}' is disabled in Jellyfin. Remove them from the IdP or re-enable them in Jellyfin.");
            }

            // Migrate auth provider on first SSO login when opt-in is on.
            if (config?.MigrateLocalUsers == true
                && !string.Equals(user.AuthenticationProviderId, oidcProviderId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Migrating user {Username} from {OldProvider} to OidcAuthProvider",
                    user.Username, user.AuthenticationProviderId ?? "none");
                user.AuthenticationProviderId = oidcProviderId;
                await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            }
        }

        _logger.LogDebug("Synced OIDC user: username={Username}, displayName={DisplayName}",
            username, displayName ?? "(none)");

        return user.Id;
    }

    public Task ApplyRolesAsync(Guid userId, string[] roles)
        => _rbacService.ApplyRoleMappingsAsync(userId, roles);
}
