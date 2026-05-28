using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Controller.Library;
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

        user.SetPermission(PermissionKind.IsAdministrator, merged.IsAdmin);
        user.SetPermission(PermissionKind.EnableMediaPlayback, merged.EnableMediaPlayback);
        user.SetPermission(PermissionKind.EnableRemoteAccess, merged.EnableRemoteAccess);
        user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, merged.EnableTranscoding);
        user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, merged.EnableTranscoding);
        user.SetPermission(PermissionKind.EnableLiveTvAccess, merged.EnableLiveTv);
        user.SetPermission(PermissionKind.EnableLiveTvManagement, merged.EnableLiveTvManagement);
        user.SetPermission(PermissionKind.EnableContentDeletion, merged.EnableContentDeletion);
        user.SetPermission(PermissionKind.EnableCollectionManagement, merged.EnableCollectionManagement);
        user.SetPermission(PermissionKind.EnableSubtitleManagement, merged.EnableSubtitleManagement);

        if (merged.EnableAllLibraries)
        {
            user.SetPermission(PermissionKind.EnableAllFolders, true);
        }
        else
        {
            user.SetPermission(PermissionKind.EnableAllFolders, false);
            var resolvedIds = ResolveLibraryIds(merged.LibraryIds, merged.LibraryNames);
            user.SetPreference(PreferenceKind.EnabledFolders, resolvedIds.ToArray());
        }

        if (merged.MaxParentalRating.HasValue)
        {
            user.MaxParentalRatingScore = merged.MaxParentalRating;
        }

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied RBAC for user {Username}: admin={IsAdmin}, libraries={LibraryCount}, roles matched=[{Roles}]",
            user.Username,
            merged.IsAdmin,
            merged.EnableAllLibraries ? "ALL" : merged.LibraryIds.Count.ToString(),
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
        var merged = new RoleMapping
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

        return merged;
    }
}
