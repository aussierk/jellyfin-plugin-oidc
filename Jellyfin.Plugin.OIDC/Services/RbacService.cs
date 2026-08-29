using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

public class RbacService
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<RbacService> _logger;

    public RbacService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILocalizationManager localization,
        ILogger<RbacService> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _localization = localization;
        _logger = logger;
    }

    public async Task ApplyRoleMappingsAsync(Guid userId, string[] userRoles, string providerId)
    {
        var config = OidcPlugin.CurrentConfig;

        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for RBAC application", userId);
            return;
        }

        // Off: skip policy entirely, including the fail-closed denial below. Admission gate still runs independently.
        if (!config.ManageUserPolicy)
        {
            _logger.LogInformation(
                "OIDC audit: decision=rbac-skipped provider={Provider} user={User} reason=policy-management-disabled",
                providerId, user.Username);
            return;
        }

        var applicableMappings = config.RoleMappings
            .Where(m => string.IsNullOrEmpty(m.ProviderFilter)
                        || string.Equals(m.ProviderFilter, providerId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matchedMappings = applicableMappings
            .Where(m => userRoles.Contains(m.RoleName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (matchedMappings.Count == 0 && !string.IsNullOrEmpty(config.DefaultRoleName))
        {
            var defaultMapping = applicableMappings
                .FirstOrDefault(m => string.Equals(m.RoleName, config.DefaultRoleName, StringComparison.OrdinalIgnoreCase));
            if (defaultMapping != null)
            {
                matchedMappings.Add(defaultMapping);
            }
        }

        if (matchedMappings.Count == 0)
        {
            _logger.LogWarning(
                "OIDC audit: decision=deny provider={Provider} user={User} reason=no-role-match roles=[{Roles}]",
                providerId, user.Username, string.Join(", ", userRoles));
            throw new InvalidOperationException(
                $"No role mapping matched user '{user.Username}'. Configure a matching role or a non-privileged default role.");
        }

        var merged = MergeMappings(matchedMappings);

        var manageLibraries = config.EnableLibraryAccessManagement;

        bool enableAllFolders;
        Guid[] enabledFolderIds;
        if (manageLibraries)
        {
            enableAllFolders = merged.EnableAllLibraries;
            enabledFolderIds = Array.Empty<Guid>();
            if (!merged.EnableAllLibraries)
            {
                var resolvedIds = ResolveLibraryIds(merged.LibraryIds, merged.LibraryNames);
                enabledFolderIds = resolvedIds
                    .Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null)
                    .Where(g => g.HasValue)
                    .Select(g => g!.Value)
                    .ToArray();
            }
        }
        else
        {
            enableAllFolders = user.HasPermission(PermissionKind.EnableAllFolders);
            enabledFolderIds = user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders);
        }

        var (parentalScore, parentalSubScore) = ResolveParentalRating(matchedMappings)
            ?? (user.MaxParentalRatingScore, user.MaxParentalRatingSubScore);

        // Snapshot current state, then mutate only the RBAC-owned fields below so everything else
        // (schedules, tags, bitrate caps) survives login. UpdatePolicyAsync, not UpdateUserAsync,
        // so the runtime state AuthenticateDirect reads right after reflects the new admin flag.
        var policy = ReadCurrentPolicy(user);

        policy.IsAdministrator = merged.IsAdmin;
        policy.EnableMediaPlayback = merged.EnableMediaPlayback;
        policy.EnableRemoteAccess = merged.EnableRemoteAccess;
        policy.EnableAudioPlaybackTranscoding = merged.EnableTranscoding;
        policy.EnableVideoPlaybackTranscoding = merged.EnableTranscoding;
        policy.EnableLiveTvAccess = merged.EnableLiveTv;
        policy.EnableLiveTvManagement = merged.EnableLiveTvManagement;
        policy.EnableContentDeletion = merged.EnableContentDeletion;
        policy.EnableCollectionManagement = merged.EnableCollectionManagement;
        policy.EnableSubtitleManagement = merged.EnableSubtitleManagement;
        policy.EnableAllFolders = enableAllFolders;
        policy.EnabledFolders = enabledFolderIds;
        policy.MaxParentalRating = parentalScore;
        policy.MaxParentalSubRating = parentalSubScore;

        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);

        // Name which matched mapping(s) carried IsAdmin, so "why is this user an admin" is auditable.
        var adminGrantedBy = merged.IsAdmin
            ? string.Join(", ", matchedMappings.Where(m => m.IsAdmin).Select(m => m.RoleName))
            : "none";

        _logger.LogInformation(
            "OIDC audit: decision=rbac provider={Provider} user={User} admin={IsAdmin} adminGrantedBy=[{AdminGrantedBy}] libraries={Libraries} roles=[{Roles}]",
            providerId,
            user.Username,
            merged.IsAdmin,
            adminGrantedBy,
            manageLibraries ? (enableAllFolders ? "ALL" : enabledFolderIds.Length.ToString()) : "unchanged",
            string.Join(", ", matchedMappings.Select(m => m.RoleName)));
    }

    /// Re-derives a <see cref="UserPolicy"/> snapshot of the user's current permission/preference
    /// flags. There's no framework method to read one back - <see cref="UserPolicy"/> only exists
    /// as the write-side DTO for <c>UpdatePolicyAsync</c> - so this is the read half of that
    /// round-trip; callers mutate only the fields they own on the object it returns.
    private static UserPolicy ReadCurrentPolicy(Jellyfin.Database.Implementations.Entities.User user)
    {
        var policy = new UserPolicy
        {
            AuthenticationProviderId = user.AuthenticationProviderId,
            PasswordResetProviderId = user.PasswordResetProviderId,
            IsHidden = user.HasPermission(PermissionKind.IsHidden),
            IsDisabled = user.HasPermission(PermissionKind.IsDisabled), // never re-enable a disabled user
            IsAdministrator = user.HasPermission(PermissionKind.IsAdministrator),
            EnableUserPreferenceAccess = user.EnableUserPreferenceAccess, // not a PermissionKind - a direct User property
            EnableRemoteControlOfOtherUsers = user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers),
            EnableSharedDeviceControl = user.HasPermission(PermissionKind.EnableSharedDeviceControl),
            EnableMediaPlayback = user.HasPermission(PermissionKind.EnableMediaPlayback),
            EnableAudioPlaybackTranscoding = user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding),
            EnableVideoPlaybackTranscoding = user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding),
            EnablePlaybackRemuxing = user.HasPermission(PermissionKind.EnablePlaybackRemuxing),
            EnableContentDownloading = user.HasPermission(PermissionKind.EnableContentDownloading),
            EnableSyncTranscoding = user.HasPermission(PermissionKind.EnableSyncTranscoding),
            EnableMediaConversion = user.HasPermission(PermissionKind.EnableMediaConversion),
            EnableAllDevices = user.HasPermission(PermissionKind.EnableAllDevices),
            EnableAllChannels = user.HasPermission(PermissionKind.EnableAllChannels),
            EnableAllFolders = user.HasPermission(PermissionKind.EnableAllFolders),
            EnableRemoteAccess = user.HasPermission(PermissionKind.EnableRemoteAccess),
            EnableLiveTvAccess = user.HasPermission(PermissionKind.EnableLiveTvAccess),
            EnableLiveTvManagement = user.HasPermission(PermissionKind.EnableLiveTvManagement),
            EnableContentDeletion = user.HasPermission(PermissionKind.EnableContentDeletion),
            EnableCollectionManagement = user.HasPermission(PermissionKind.EnableCollectionManagement),
            EnableSubtitleManagement = user.HasPermission(PermissionKind.EnableSubtitleManagement),
            ForceRemoteSourceTranscoding = user.HasPermission(PermissionKind.ForceRemoteSourceTranscoding),
            EnablePublicSharing = user.HasPermission(PermissionKind.EnablePublicSharing),
            EnableLyricManagement = user.HasPermission(PermissionKind.EnableLyricManagement),
            BlockedTags = user.GetPreference(PreferenceKind.BlockedTags),
            AllowedTags = user.GetPreference(PreferenceKind.AllowedTags),
            EnabledDevices = user.GetPreference(PreferenceKind.EnabledDevices),
            EnableContentDeletionFromFolders = user.GetPreference(PreferenceKind.EnableContentDeletionFromFolders),
            EnabledChannels = user.GetPreferenceValues<Guid>(PreferenceKind.EnabledChannels),
            BlockedChannels = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedChannels),
            EnabledFolders = user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders),
            BlockedMediaFolders = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedMediaFolders),
            BlockUnratedItems = user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems),
            AccessSchedules = user.AccessSchedules.ToArray(),
            MaxActiveSessions = user.MaxActiveSessions,
            InvalidLoginAttemptCount = user.InvalidLoginAttemptCount,
            SyncPlayAccess = user.SyncPlayAccess,
            MaxParentalRating = user.MaxParentalRatingScore,
            MaxParentalSubRating = user.MaxParentalRatingSubScore
        };

        // These are non-nullable on UserPolicy, so a null entity value (no override) can't be copied
        // across; leave the property at UserPolicy's own default rather than hard-coding one here.
        if (user.LoginAttemptsBeforeLockout.HasValue)
        {
            policy.LoginAttemptsBeforeLockout = user.LoginAttemptsBeforeLockout.Value;
        }

        if (user.RemoteClientBitrateLimit.HasValue)
        {
            policy.RemoteClientBitrateLimit = user.RemoteClientBitrateLimit.Value;
        }

        return policy;
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
            // Parental rating is resolved separately - see ResolveParentalRating.
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

    /// The strictest (lowest Score/SubScore) parental rating across the matched mappings, or null.
    private (int? Score, int? SubScore)? ResolveParentalRating(List<RoleMapping> mappings)
    {
        (int Score, int? SubScore)? strictest = null;

        foreach (var mapping in mappings)
        {
            (int Score, int? SubScore)? candidate = null;

            if (!string.IsNullOrWhiteSpace(mapping.MaxParentalRatingName))
            {
                var resolved = _localization.GetRatingScore(mapping.MaxParentalRatingName.Trim(), string.Empty);
                if (resolved != null)
                {
                    candidate = (resolved.Score, resolved.SubScore);
                }
                else
                {
                    // Named rating isn't defined on this server; fail closed to the strictest cap.
                    _logger.LogWarning(
                        "OIDC RBAC: parental rating '{Rating}' is not defined on this server - applying the "
                        + "most restrictive cap for this role mapping. Correct the rating name in the mapping.",
                        mapping.MaxParentalRatingName);
                    candidate = (0, null);
                }
            }
            else if (mapping.MaxParentalRating.HasValue)
            {
                candidate = (mapping.MaxParentalRating.Value, null);
            }

            if (candidate == null)
            {
                continue;
            }

            if (strictest == null
                || candidate.Value.Score < strictest.Value.Score
                || (candidate.Value.Score == strictest.Value.Score
                    && (candidate.Value.SubScore ?? int.MaxValue) < (strictest.Value.SubScore ?? int.MaxValue)))
            {
                strictest = candidate;
            }
        }

        return strictest == null ? null : (strictest.Value.Score, strictest.Value.SubScore);
    }
}
