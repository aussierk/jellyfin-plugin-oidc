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

        // A mapping applies when its ProviderFilter is empty (global) or matches the current provider.
        var applicableMappings = config.RoleMappings
            .Where(m => string.IsNullOrEmpty(m.ProviderFilter)
                        || string.Equals(m.ProviderFilter, providerId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // All matched mappings are merged (union of permissions, strictest parental rating).
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

        // The strictest parental rating across the matched mappings, or the user's current
        // one when no mapping restricts it. Lower score = more restrictive.
        var (parentalScore, parentalSubScore) = ResolveParentalRating(matchedMappings)
            ?? (user.MaxParentalRatingScore, user.MaxParentalRatingSubScore);

        // Build the policy by carrying EVERY field forward from the user's current entity
        // state, then overriding ONLY the fields the plugin owns (the "RBAC-controlled"
        // block below). Anything not in that block — access schedules, blocked/allowed tags,
        // unrated-item blocks, bitrate/session caps, SyncPlay level, channel/device lists —
        // is the Jellyfin admin's to set and must survive an OIDC login untouched.
        //
        // Using UpdatePolicyAsync (not UpdateUserAsync) so Jellyfin refreshes its runtime
        // user state, not just the DB row — the session minted right after by
        // AuthenticateDirect then sees the correct admin flag.
        var policy = new UserPolicy
        {
            // ── carried forward from current user state (NOT plugin-managed) ──
            AuthenticationProviderId = user.AuthenticationProviderId,
            PasswordResetProviderId = user.PasswordResetProviderId,
            IsHidden = user.HasPermission(PermissionKind.IsHidden),
            IsDisabled = user.HasPermission(PermissionKind.IsDisabled), // never re-enable a disabled user
            EnableUserPreferenceAccess = true,
            EnableRemoteControlOfOtherUsers = user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers),
            EnableSharedDeviceControl = user.HasPermission(PermissionKind.EnableSharedDeviceControl),
            EnablePlaybackRemuxing = user.HasPermission(PermissionKind.EnablePlaybackRemuxing),
            EnableContentDownloading = user.HasPermission(PermissionKind.EnableContentDownloading),
            EnableSyncTranscoding = user.HasPermission(PermissionKind.EnableSyncTranscoding),
            EnableMediaConversion = user.HasPermission(PermissionKind.EnableMediaConversion),
            EnableAllDevices = user.HasPermission(PermissionKind.EnableAllDevices),
            EnableAllChannels = user.HasPermission(PermissionKind.EnableAllChannels),
            ForceRemoteSourceTranscoding = user.HasPermission(PermissionKind.ForceRemoteSourceTranscoding),
            EnablePublicSharing = user.HasPermission(PermissionKind.EnablePublicSharing),
            EnableLyricManagement = user.HasPermission(PermissionKind.EnableLyricManagement),
            BlockedTags = user.GetPreference(PreferenceKind.BlockedTags),
            AllowedTags = user.GetPreference(PreferenceKind.AllowedTags),
            EnabledDevices = user.GetPreference(PreferenceKind.EnabledDevices),
            EnableContentDeletionFromFolders = user.GetPreference(PreferenceKind.EnableContentDeletionFromFolders),
            EnabledChannels = user.GetPreferenceValues<Guid>(PreferenceKind.EnabledChannels),
            BlockedChannels = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedChannels),
            BlockedMediaFolders = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedMediaFolders),
            BlockUnratedItems = user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems),
            AccessSchedules = user.AccessSchedules.ToArray(),
            MaxActiveSessions = user.MaxActiveSessions,
            LoginAttemptsBeforeLockout = user.LoginAttemptsBeforeLockout ?? -1,
            InvalidLoginAttemptCount = user.InvalidLoginAttemptCount,
            RemoteClientBitrateLimit = user.RemoteClientBitrateLimit ?? 0,
            SyncPlayAccess = user.SyncPlayAccess,

            // ── RBAC-controlled: the ONLY fields the plugin owns ──
            IsAdministrator = merged.IsAdmin,
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
            MaxParentalRating = parentalScore,
            MaxParentalSubRating = parentalSubScore,
        };

        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);

        _logger.LogInformation(
            "OIDC audit: decision=rbac provider={Provider} user={User} admin={IsAdmin} libraries={Libraries} roles=[{Roles}]",
            providerId,
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
            // Parental rating is resolved separately (name -> score) and merged strictest-wins;
            // see ResolveParentalRating.
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

    /// <summary>
    /// The strictest parental rating across the matched mappings, or null when none restrict it.
    /// Each mapping's <see cref="RoleMapping.MaxParentalRatingName"/> is resolved through
    /// Jellyfin's own <see cref="ILocalizationManager"/> (server metadata country); the legacy
    /// numeric <see cref="RoleMapping.MaxParentalRating"/> is honoured as a fallback. "Strictest"
    /// = lowest <c>(Score, SubScore)</c>.
    /// </summary>
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
                    _logger.LogWarning(
                        "OIDC RBAC: parental rating '{Rating}' not recognised by this server; ignoring it for the merge",
                        mapping.MaxParentalRatingName);
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
