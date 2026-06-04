using System;
using System.Linq;
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
    /// Validates that an existing account was created by the same OIDC provider to
    /// prevent cross-provider username collision attacks.
    /// </summary>
    private const int MaxUsernameLength = 255;

    public async Task<Guid> SyncUserAsync(string username, string? displayName, string providerId)
    {
        // Reject usernames that are empty, too long, or contain control / null characters.
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

            // Record which OIDC provider owns this account.
            if (config != null)
            {
                config.UserProviderMap.RemoveAll(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase));
                config.UserProviderMap.Add(new Configuration.UserProviderEntry { Username = username, ProviderId = providerId });
                OidcPlugin.Instance?.SaveConfiguration();
            }

            _logger.LogInformation("Created new OIDC user: {Username} (provider={Provider})", username, providerId);
        }
        else
        {
            // Respect a disabled flag set by a Jellyfin admin — never re-enable it here.
            if (user.HasPermission(PermissionKind.IsDisabled))
            {
                throw new InvalidOperationException(
                    $"User '{user.Username}' is disabled in Jellyfin. Remove them from the IdP or re-enable them in Jellyfin.");
            }

            var isOidcUser = string.Equals(user.AuthenticationProviderId, oidcProviderId, StringComparison.Ordinal);

            if (!isOidcUser)
            {
                if (config?.MigrateLocalUsers == true)
                {
                    // Opt-in migration: move local user to OIDC.
                    _logger.LogInformation(
                        "Migrating user {Username} from {OldProvider} to OidcAuthProvider (provider={Provider})",
                        user.Username, user.AuthenticationProviderId ?? "none", providerId);
                    user.AuthenticationProviderId = oidcProviderId;
                    await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

                    if (config != null)
                    {
                        config.UserProviderMap.RemoveAll(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase));
                        config.UserProviderMap.Add(new Configuration.UserProviderEntry { Username = username, ProviderId = providerId });
                        OidcPlugin.Instance?.SaveConfiguration();
                    }
                }
                else
                {
                    // MigrateLocalUsers is off — block OIDC from taking over a local account.
                    _logger.LogWarning(
                        "Login blocked: user '{Username}' exists as a local account and MigrateLocalUsers is disabled. " +
                        "Enable MigrateLocalUsers in the plugin settings or rename the account in your IdP.",
                        user.Username);
                    throw new InvalidOperationException(
                        $"User '{user.Username}' is a local account. Enable MigrateLocalUsers to allow OIDC login for this account.");
                }
            }
            else
            {
                // Existing OIDC user: validate it belongs to the same provider.
                var registeredProvider = config?.UserProviderMap
                    .FirstOrDefault(e => string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase))
                    ?.ProviderId;

                if (config != null
                    && registeredProvider != null
                    && !string.Equals(registeredProvider, providerId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Login blocked: user '{Username}' was created by provider '{RegisteredProvider}' " +
                        "but login was attempted via provider '{AttemptedProvider}'.",
                        username, registeredProvider, providerId);
                    throw new InvalidOperationException(
                        $"User '{username}' is registered to a different OIDC provider. " +
                        "Use the correct provider or contact an administrator.");
                }
            }
        }

        _logger.LogDebug("Synced OIDC user: username={Username}, displayName={DisplayName}, provider={Provider}",
            username, displayName ?? "(none)", providerId);

        return user.Id;
    }

    public Task ApplyRolesAsync(Guid userId, string[] roles, string providerId)
        => _rbacService.ApplyRoleMappingsAsync(userId, roles, providerId);
}
