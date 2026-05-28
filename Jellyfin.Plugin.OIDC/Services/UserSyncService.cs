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

    public async Task<Guid> SyncUserAsync(string username, string? displayName, string[] roles)
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
            user.AuthenticationProviderId = typeof(Auth.OidcAuthProvider).FullName!;

            _logger.LogInformation("Created new OIDC user: {Username}", username);
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            // Only update if the display name is not already set or is different
            // The User entity doesn't expose DisplayName directly; it's part of the DTO
            // We skip display name update here as it requires additional API surface
        }

        user.SetPermission(PermissionKind.IsDisabled, false);

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        await _rbacService.ApplyRoleMappingsAsync(user.Id, roles).ConfigureAwait(false);

        return user.Id;
    }
}
