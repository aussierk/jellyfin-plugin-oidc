using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

public class RbacService
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<RbacService> _logger;

    public RbacService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger<RbacService> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task ApplyRoleMappingsAsync(Guid userId, string[] userRoles)
    {
        var config = OidcPlugin.Instance?.Configuration;
        if (config == null)
        {
            return;
        }

        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for RBAC application", userId);
            return;
        }

        var matchedMappings = config.RoleMappings
            .Where(m => userRoles.Contains(m.RoleName, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Priority)
            .ToList();

        if (matchedMappings.Count == 0 && !string.IsNullOrEmpty(config.DefaultRoleName))
        {
            var defaultMapping = config.RoleMappings
                .FirstOrDefault(m => string.Equals(m.RoleName, config.DefaultRoleName, StringComparison.OrdinalIgnoreCase));
            if (defaultMapping != null)
            {
                matchedMappings.Add(defaultMapping);
            }
        }

        if (matchedMappings.Count == 0)
        {
            _logger.LogInformation("No role mappings matched for user {Username} with roles [{Roles}]",
                user.Username, string.Join(", ", userRoles));
            return;
        }

        var merged = MergeMappings(matchedMappings);

        // Resolve library IDs before building policy
        Guid[] enabledFolderIds = Array.Empty<Guid>();
        if (!merged.EnableAllLibraries)
        {
            var resolvedIds = ResolveLibraryIds(merged.LibraryIds, merged.LibraryNames);
            enabledFolderIds = resolvedIds
                .Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToArray();
        }

        // Build a policy from the current user state and override with RBAC values.
        // Using UpdatePolicyAsync ensures Jellyfin refreshes its runtime user state
        // (not just the database), so the session created by AuthenticateDirect
        // picks up the correct admin flag.
        var policy = new UserPolicy
        {
            // Preserve identity fields
            AuthenticationProviderId = user.AuthenticationProviderId,
            PasswordResetProviderId = user.PasswordResetProviderId,

            // Preserve non-RBAC permissions from current user state
            IsHidden = user.HasPermission(PermissionKind.IsHidden),
            EnableUserPreferenceAccess = true,
            EnableRemoteControlOfOtherUsers = user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers),
            EnableSharedDeviceControl = user.HasPermission(PermissionKind.EnableSharedDeviceControl),
            EnablePlaybackRemuxing = user.HasPermission(PermissionKind.EnablePlaybackRemuxing),
            EnableContentDownloading = user.HasPermission(PermissionKind.EnableContentDownloading),
            EnableSyncTranscoding = user.HasPermission(PermissionKind.EnableSyncTranscoding),
            EnableMediaConversion = user.HasPermission(PermissionKind.EnableMediaConversion),
            EnableAllDevices = user.HasPermission(PermissionKind.EnableAllDevices),
            EnableAllChannels = user.HasPermission(PermissionKind.EnableAllChannels),
            MaxParentalRating = user.MaxParentalRatingScore,

            // RBAC-controlled fields
            IsAdministrator = merged.IsAdmin,
            IsDisabled = user.HasPermission(PermissionKind.IsDisabled), // preserve — never re-enable a disabled user
            EnableMediaPlayback = merged.EnableMediaPlayback,
            EnableRemoteAccess = merged.EnableRemoteAccess,
            EnableAudioPlaybackTranscoding = merged.EnableTranscoding,
            EnableVideoPlaybackTranscoding = merged.EnableTranscoding,
            EnableLiveTvAccess = merged.EnableLiveTv,
            EnableLiveTvManagement = merged.EnableLiveTvManagement,
            EnableContentDeletion = merged.EnableContentDeletion,
            EnableCollectionManagement = merged.EnableCollectionManagement,
            EnableSubtitleManagement = merged.EnableSubtitleManagement,
            EnableAllFolders = merged.EnableAllLibraries,
            EnabledFolders = enabledFolderIds,
        };

        if (merged.MaxParentalRating.HasValue)
        {
            policy.MaxParentalRating = merged.MaxParentalRating;
        }

        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied RBAC for user {Username}: admin={IsAdmin}, libraries={Libraries}, roles matched=[{Roles}]",
            user.Username,
            merged.IsAdmin,
            merged.EnableAllLibraries ? "ALL" : enabledFolderIds.Length.ToString(),
            string.Join(", ", matchedMappings.Select(m => m.RoleName)));
    }

    public Dictionary<string, string> GetAvailableLibraries()
    {
        var folders = _libraryManager.GetVirtualFolders();
        return folders.ToDictionary(
            f => f.ItemId,
            f => f.Name);
    }

    private List<string> ResolveLibraryIds(List<string> ids, List<string> names)
    {
        var resolved = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return resolved.ToList();
        }

        var folders = _libraryManager.GetVirtualFolders();
        foreach (var name in names)
        {
            var folder = folders.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (folder != null)
            {
                resolved.Add(folder.ItemId);
            }
            else
            {
                _logger.LogWarning("Library '{LibraryName}' not found during RBAC resolution", name);
            }
        }

        return resolved.ToList();
    }

    private static RoleMapping MergeMappings(List<RoleMapping> mappings)
    {
        return new RoleMapping
        {
            IsAdmin = mappings.Any(m => m.IsAdmin),
            EnableAllLibraries = mappings.Any(m => m.EnableAllLibraries),
            EnableLiveTv = mappings.Any(m => m.EnableLiveTv),
            EnableLiveTvManagement = mappings.Any(m => m.EnableLiveTvManagement),
            EnableMediaPlayback = mappings.Any(m => m.EnableMediaPlayback),
            EnableRemoteAccess = mappings.Any(m => m.EnableRemoteAccess),
            EnableTranscoding = mappings.Any(m => m.EnableTranscoding),
            EnableContentDeletion = mappings.Any(m => m.EnableContentDeletion),
            EnableCollectionManagement = mappings.Any(m => m.EnableCollectionManagement),
            EnableSubtitleManagement = mappings.Any(m => m.EnableSubtitleManagement),
            MaxParentalRating = mappings
                .Where(m => m.MaxParentalRating.HasValue)
                .Select(m => m.MaxParentalRating)
                .DefaultIfEmpty(null)
                .Max(),
            LibraryIds = mappings
                .SelectMany(m => m.LibraryIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LibraryNames = mappings
                .SelectMany(m => m.LibraryNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
