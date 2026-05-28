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
    /// Also serves as the migration path for existing local users moving to SSO:
    /// the AuthenticationProviderId is updated to OidcAuthProvider on first SSO login.
    /// </summary>
    public async Task<Guid> SyncUserAsync(string username, string? displayName)
    {
        var user = _userManager.GetUserByName(username);

        if (user == null)
        {
            var config = OidcPlugin.Instance?.Configuration;
            if (config?.AutoCreateUsers != true)
            {
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);
            _logger.LogInformation("Created new OIDC user: {Username}", username);
        }

        var oidcProviderId = typeof(Auth.OidcAuthProvider).FullName!;
        if (!string.Equals(user.AuthenticationProviderId, oidcProviderId, StringComparison.Ordinal))
        {
            var config = OidcPlugin.Instance?.Configuration;
            if (config?.MigrateLocalUsers == true)
            {
                _logger.LogInformation(
                    "Migrating user {Username} from {OldProvider} to OidcAuthProvider",
                    username, user.AuthenticationProviderId ?? "none");
                user.AuthenticationProviderId = oidcProviderId;
            }
            else
            {
                _logger.LogDebug(
                    "User {Username} has provider {Provider}; migration disabled — skipping",
                    username, user.AuthenticationProviderId ?? "none");
            }
        }

        user.SetPermission(PermissionKind.IsDisabled, false);
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        _logger.LogDebug("Synced OIDC user: username={Username}, displayName={DisplayName}",
            username, displayName ?? "(none)");

        return user.Id;
    }

    public Task ApplyRolesAsync(Guid userId, string[] roles)
        => _rbacService.ApplyRoleMappingsAsync(userId, roles);
}
