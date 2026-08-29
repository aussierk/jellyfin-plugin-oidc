using System.Reflection;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

[Xunit.Collection("OidcPlugin")]
public class RbacServiceTests
{
    private const string OidcProviderId = "Jellyfin.Plugin.OIDC.Auth.OidcAuthProvider";
    private readonly PluginTestFixture _fixture;

    public RbacServiceTests(PluginTestFixture fixture) => _fixture = fixture;

    private static User MakeUser(string username = "testuser", bool disabled = false)
    {
        var user = new User(username, OidcProviderId, "DefaultPasswordResetProvider");
        if (disabled)
        {
            user.SetPermission(PermissionKind.IsDisabled, true);
        }

        return user;
    }

    private static RbacService MakeService(
        IUserManager userManager, ILibraryManager libraryManager, ILocalizationManager? localization = null)
        => new(userManager, libraryManager, localization ?? Substitute.For<ILocalizationManager>(),
            NullLogger<RbacService>.Instance);

    // ── early-exit when user not found ─────────────────────────────────────────

    [Fact]
    public async Task UserNotFound_EarlyReturn_UpdatePolicyNotCalled()
    {
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(Arg.Any<Guid>()).Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "admin", IsAdmin = true }]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(Guid.NewGuid(), ["admin"], "keycloak");

        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    // ── role matching ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MatchingRole_SetsIsAdminTrue()
    {
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["admins"], "keycloak");

        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.IsAdministrator));
    }

    [Fact]
    public async Task MatchingRole_SetsEnableMediaPlayback()
    {
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "viewers", EnableMediaPlayback = true }]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["viewers"], "keycloak");

        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.EnableMediaPlayback));
    }

    // ── provider filter ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProviderFilter_NonMatchingProvider_LoginDenied()
    {
        // Mapping is scoped to "keycloak"; user comes from "okta".
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings =
            [
                new RoleMapping { RoleName = "admins", IsAdmin = true, ProviderFilter = "keycloak" }
            ]
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeService(userManager, Substitute.For<ILibraryManager>())
                .ApplyRoleMappingsAsync(userId, ["admins"], "okta"));

        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    [Fact]
    public async Task ProviderFilter_EmptyFilter_AppliesToAllProviders()
    {
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings =
            [
                new RoleMapping { RoleName = "viewers", EnableMediaPlayback = true, ProviderFilter = "" }
            ]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["viewers"], "any-provider");

        await userManager.Received(1).UpdatePolicyAsync(userId, Arg.Any<UserPolicy>());
    }

    // ── merge: union of permissions ────────────────────────────────────────────

    [Fact]
    public async Task MergeMappings_UnionOfPermissions()
    {
        // role-a grants admin, role-b grants media playback; merged result must have both.
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings =
            [
                new RoleMapping { RoleName = "role-a", IsAdmin = true, EnableMediaPlayback = false },
                new RoleMapping { RoleName = "role-b", IsAdmin = false, EnableMediaPlayback = true }
            ]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["role-a", "role-b"], "keycloak");

        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.IsAdministrator && p.EnableMediaPlayback));
    }

    // ── default role fallback ──────────────────────────────────────────────────

    [Fact]
    public async Task DefaultRoleFallback_WhenNoRolesMatch()
    {
        // User has no matching roles; DefaultRoleName must kick in.
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            DefaultRoleName = "default-viewer",
            RoleMappings =
            [
                new RoleMapping { RoleName = "admins", IsAdmin = true },
                new RoleMapping { RoleName = "default-viewer", EnableMediaPlayback = true }
            ]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, [], "keycloak");

        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => !p.IsAdministrator && p.EnableMediaPlayback));
    }

    [Fact]
    public async Task NoMatch_NoDefault_LoginDeniedAndPolicyNotChanged()
    {
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        _fixture.SetConfiguration(new PluginConfiguration
        {
            DefaultRoleName = "",
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeService(userManager, Substitute.For<ILibraryManager>())
                .ApplyRoleMappingsAsync(userId, ["viewers"], "keycloak"));

        Assert.Contains("No role mapping matched", exception.Message);
        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    [Fact]
    public async Task NoMatch_DefaultRoleDoesNotExist_LoginDeniedAndPolicyNotChanged()
    {
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        _fixture.SetConfiguration(new PluginConfiguration
        {
            DefaultRoleName = "missing-default",
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeService(userManager, Substitute.For<ILibraryManager>())
                .ApplyRoleMappingsAsync(userId, ["viewers"], "keycloak"));

        Assert.Contains("No role mapping matched", exception.Message);
        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    // ── disabled flag preserved ────────────────────────────────────────────────

    [Fact]
    public async Task DisabledFlag_IsPreservedInPolicy()
    {
        // Jellyfin admin disabled this user; RBAC must never re-enable them.
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser(disabled: true));
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["admins"], "keycloak");

        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.IsDisabled));
    }

    // ── library name resolution ────────────────────────────────────────────────

    // ── non-RBAC policy fields survive an OIDC login ──────────────────────────

    private static (RbacService Svc, IUserManager Um) ServiceCapturing(out System.Func<UserPolicy?> captured, User user, ILocalizationManager? loc = null)
    {
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        UserPolicy? cap = null;
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Do<UserPolicy>(p => cap = p)).Returns(Task.CompletedTask);
        captured = () => cap;
        return (MakeService(userManager, Substitute.For<ILibraryManager>(), loc), userManager);
    }

    [Fact]
    public async Task NonRbacPolicyFields_ArePreserved()
    {
        var user = MakeUser();
        user.AccessSchedules.Add(new AccessSchedule(DynamicDayOfWeek.Everyday, 0, 12, user.Id));
        user.SetPreference(PreferenceKind.BlockedTags, ["horror"]);
        user.SetPreference(PreferenceKind.BlockUnratedItems, [UnratedItem.Movie.ToString()]);
        user.RemoteClientBitrateLimit = 5_000_000;
        user.MaxActiveSessions = 3;
        user.SyncPlayAccess = SyncPlayUserAccessType.None;
        user.MaxParentalRatingScore = 10;

        var (svc, _) = ServiceCapturing(out var policy, user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "viewers", EnableMediaPlayback = true }]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["viewers"], "keycloak");

        var p = policy()!;
        Assert.Single(p.AccessSchedules);
        Assert.Contains("horror", p.BlockedTags);
        Assert.Contains(UnratedItem.Movie, p.BlockUnratedItems);
        Assert.Equal(5_000_000, p.RemoteClientBitrateLimit);
        Assert.Equal(3, p.MaxActiveSessions);
        Assert.Equal(SyncPlayUserAccessType.None, p.SyncPlayAccess);
        Assert.Equal(10, p.MaxParentalRating); // carried forward - no mapping restricts it
    }

    // ── parental rating: name -> score via ILocalizationManager, strictest wins ──

    [Fact]
    public async Task ParentalRating_ResolvesNameToScore()
    {
        var user = MakeUser();
        var loc = Substitute.For<ILocalizationManager>();
        loc.GetRatingScore("PG-13", Arg.Any<string>()).Returns(new ParentalRatingScore(9, null));

        var (svc, _) = ServiceCapturing(out var policy, user, loc);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "teens", MaxParentalRatingName = "PG-13" }]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["teens"], "keycloak");

        Assert.Equal(9, policy()!.MaxParentalRating);
        Assert.Null(policy()!.MaxParentalSubRating);
    }

    [Fact]
    public async Task ParentalRating_UnresolvedName_FailsClosedToStrictestCap()
    {
        var user = MakeUser();
        user.MaxParentalRatingScore = 12; // would be carried forward if the name were simply ignored
        var loc = Substitute.For<ILocalizationManager>();
        loc.GetRatingScore("Totally Made Up", Arg.Any<string>()).Returns((ParentalRatingScore?)null);

        var (svc, _) = ServiceCapturing(out var policy, user, loc);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "teens", MaxParentalRatingName = "Totally Made Up" }]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["teens"], "keycloak");

        Assert.Equal(0, policy()!.MaxParentalRating);
    }

    [Fact]
    public async Task ParentalRating_LegacyNumericStillHonoured()
    {
        var user = MakeUser();
        var (svc, _) = ServiceCapturing(out var policy, user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "kids", MaxParentalRating = 7 }]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["kids"], "keycloak");

        Assert.Equal(7, policy()!.MaxParentalRating);
    }

    [Fact]
    public async Task ParentalRating_StrictestWins_AcrossMappings()
    {
        var user = MakeUser();
        var loc = Substitute.For<ILocalizationManager>();
        loc.GetRatingScore("PG-13", Arg.Any<string>()).Returns(new ParentalRatingScore(9, null));

        var (svc, _) = ServiceCapturing(out var policy, user, loc);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings =
            [
                new RoleMapping { RoleName = "a", MaxParentalRatingName = "PG-13" }, // score 9
                new RoleMapping { RoleName = "b", MaxParentalRating = 5 }             // score 5 (stricter)
            ]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["a", "b"], "keycloak");

        Assert.Equal(5, policy()!.MaxParentalRating);
    }

    // ── ManageUserPolicy opt-out ───────────────────────────────────────────────

    [Fact]
    public async Task ManageUserPolicy_False_SkipsRbacEntirely_EvenWithNoRoleMatch()
    {
        var user = MakeUser();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        var svc = MakeService(userManager, Substitute.For<ILibraryManager>());
        _fixture.SetConfiguration(new PluginConfiguration
        {
            ManageUserPolicy = false,
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        // Roles match nothing; without ManageUserPolicy this must not throw or touch the policy.
        await svc.ApplyRoleMappingsAsync(user.Id, ["nobody-role"], "keycloak");

        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

   // ── EnableLibraryAccessManagement = false ─────────────────────────────────────

[Fact]
public async Task EnableLibraryAccessManagement_False_PreservesUsersCurrentLibraries()
{
    var user = MakeUser();
    var libId = Guid.NewGuid();
    user.SetPermission(PermissionKind.EnableAllFolders, false);
    user.SetPreference(PreferenceKind.EnabledFolders, new[] { libId });

    var (svc, _) = ServiceCapturing(out var policy, user);
    _fixture.SetConfiguration(new PluginConfiguration
    {
        EnableLibraryAccessManagement = false,
        // The mapping grants ALL libraries; management off must leave the user's own set alone.
        RoleMappings = [new RoleMapping { RoleName = "viewers", EnableAllLibraries = true }]
    });

    await svc.ApplyRoleMappingsAsync(user.Id, ["viewers"], "keycloak");

    Assert.False(policy()!.EnableAllFolders);
    Assert.Equal(new[] { libId }, policy()!.EnabledFolders);
}

[Fact]
public async Task EnableLibraryAccessManagement_DefaultsToTrue_UsesMappingLibraries()
{
    var user = MakeUser();
    var (svc, _) = ServiceCapturing(out var policy, user);
    _fixture.SetConfiguration(new PluginConfiguration
    {
        RoleMappings = [new RoleMapping { RoleName = "viewers", EnableAllLibraries = true }]
    });

    await svc.ApplyRoleMappingsAsync(user.Id, ["viewers"], "keycloak");

    Assert.True(policy()!.EnableAllFolders);
}

    [Fact]
    public void GetAvailableLibraries_ReturnsNameToIdDictionary()
    {
        var libraryManager = Substitute.For<ILibraryManager>();
        libraryManager.GetVirtualFolders().Returns(
        [
            new VirtualFolderInfo { ItemId = "lib-001", Name = "Movies" },
            new VirtualFolderInfo { ItemId = "lib-002", Name = "TV Shows" }
        ]);

        var result = MakeService(Substitute.For<IUserManager>(), libraryManager)
            .GetAvailableLibraries();

        Assert.Equal(2, result.Count);
        Assert.Equal("Movies", result["lib-001"]);
        Assert.Equal("TV Shows", result["lib-002"]);
    }

    // ── UserPolicy schema-drift canary ────────────────────────────────────────
    //
    // RbacService.ReadCurrentPolicy hand-copies the User entity into a UserPolicy on every
    // OIDC login; ApplyRoleMappingsAsync then overwrites only the RBAC-owned fields. If
    // Jellyfin adds a UserPolicy field and ReadCurrentPolicy doesn't copy it, that field
    // silently resets to its default for every OIDC user on every login. This test fails
    // when the DTO grows so someone re-checks ReadCurrentPolicy and classifies the new field.

    // Overwritten from the role mapping after ReadCurrentPolicy runs (RbacService.ApplyRoleMappingsAsync).
    private static readonly HashSet<string> RbacOwnedPolicyFields = new()
    {
        nameof(UserPolicy.IsAdministrator),
        nameof(UserPolicy.EnableMediaPlayback),
        nameof(UserPolicy.EnableRemoteAccess),
        nameof(UserPolicy.EnableAudioPlaybackTranscoding),
        nameof(UserPolicy.EnableVideoPlaybackTranscoding),
        nameof(UserPolicy.EnableLiveTvAccess),
        nameof(UserPolicy.EnableLiveTvManagement),
        nameof(UserPolicy.EnableContentDeletion),
        nameof(UserPolicy.EnableCollectionManagement),
        nameof(UserPolicy.EnableSubtitleManagement),
        nameof(UserPolicy.EnableAllFolders),
        nameof(UserPolicy.EnabledFolders),
        nameof(UserPolicy.MaxParentalRating),
        nameof(UserPolicy.MaxParentalSubRating),
    };

    // Re-derived from the User entity by ReadCurrentPolicy so they survive login untouched.
    private static readonly HashSet<string> PreservedPolicyFields = new()
    {
        nameof(UserPolicy.IsHidden),
        nameof(UserPolicy.IsDisabled),
        nameof(UserPolicy.EnableUserPreferenceAccess),
        nameof(UserPolicy.EnableRemoteControlOfOtherUsers),
        nameof(UserPolicy.EnableSharedDeviceControl),
        nameof(UserPolicy.EnablePlaybackRemuxing),
        nameof(UserPolicy.EnableContentDownloading),
        nameof(UserPolicy.EnableSyncTranscoding),
        nameof(UserPolicy.EnableMediaConversion),
        nameof(UserPolicy.EnableAllDevices),
        nameof(UserPolicy.EnableAllChannels),
        nameof(UserPolicy.ForceRemoteSourceTranscoding),
        nameof(UserPolicy.EnablePublicSharing),
        nameof(UserPolicy.EnableLyricManagement),
        nameof(UserPolicy.BlockedTags),
        nameof(UserPolicy.AllowedTags),
        nameof(UserPolicy.EnabledDevices),
        nameof(UserPolicy.EnableContentDeletionFromFolders),
        nameof(UserPolicy.EnabledChannels),
        nameof(UserPolicy.BlockedChannels),
        nameof(UserPolicy.BlockedMediaFolders),
        nameof(UserPolicy.BlockUnratedItems),
        nameof(UserPolicy.AccessSchedules),
        nameof(UserPolicy.MaxActiveSessions),
        nameof(UserPolicy.InvalidLoginAttemptCount),
        nameof(UserPolicy.LoginAttemptsBeforeLockout),
        nameof(UserPolicy.SyncPlayAccess),
        nameof(UserPolicy.RemoteClientBitrateLimit),
        nameof(UserPolicy.AuthenticationProviderId),
        nameof(UserPolicy.PasswordResetProviderId),
    };

    [Fact]
    public async Task EveryPreservedPolicyField_SurvivesAnOidcLogin()
    {
        // Complements NonRbacPolicyFields_ArePreserved by covering the fields it doesn't:
        // every entry in PreservedPolicyFields is set to a non-default on the entity and
        // asserted to round-trip through ReadCurrentPolicy unchanged.
        var user = MakeUser();
        var chan1 = Guid.NewGuid();
        var folder1 = Guid.NewGuid();

        foreach (var perm in new[]
                 {
                     PermissionKind.IsHidden,
                     PermissionKind.EnableRemoteControlOfOtherUsers,
                     PermissionKind.EnableSharedDeviceControl,
                     PermissionKind.EnablePlaybackRemuxing,
                     PermissionKind.EnableContentDownloading,
                     PermissionKind.EnableSyncTranscoding,
                     PermissionKind.EnableMediaConversion,
                     PermissionKind.EnableAllDevices,
                     PermissionKind.EnableAllChannels,
                     PermissionKind.ForceRemoteSourceTranscoding,
                     PermissionKind.EnablePublicSharing,
                     PermissionKind.EnableLyricManagement,
                 })
        {
            user.SetPermission(perm, true);
        }

        user.EnableUserPreferenceAccess = false;
        user.SetPreference(PreferenceKind.AllowedTags, ["allowed-tag"]);
        user.SetPreference(PreferenceKind.EnabledDevices, ["device-a"]);
        user.SetPreference(PreferenceKind.EnableContentDeletionFromFolders, ["del-folder"]);
        user.SetPreference(PreferenceKind.EnabledChannels, new[] { chan1 });
        user.SetPreference(PreferenceKind.BlockedChannels, new[] { chan1 });
        user.SetPreference(PreferenceKind.BlockedMediaFolders, new[] { folder1 });
        user.InvalidLoginAttemptCount = 4;
        user.LoginAttemptsBeforeLockout = 7;
        user.RemoteClientBitrateLimit = 3_000_000;
        user.SyncPlayAccess = SyncPlayUserAccessType.JoinGroups;
        // AuthenticationProviderId is set by MakeUser to the OIDC provider id.

        var (svc, _) = ServiceCapturing(out var policy, user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "viewers", EnableMediaPlayback = true }]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["viewers"], "keycloak");
        var p = policy()!;

        Assert.True(p.IsHidden);
        Assert.True(p.EnableRemoteControlOfOtherUsers);
        Assert.True(p.EnableSharedDeviceControl);
        Assert.True(p.EnablePlaybackRemuxing);
        Assert.True(p.EnableContentDownloading);
        Assert.True(p.EnableSyncTranscoding);
        Assert.True(p.EnableMediaConversion);
        Assert.True(p.EnableAllDevices);
        Assert.True(p.EnableAllChannels);
        Assert.True(p.ForceRemoteSourceTranscoding);
        Assert.True(p.EnablePublicSharing);
        Assert.True(p.EnableLyricManagement);
        Assert.False(p.EnableUserPreferenceAccess);
        Assert.Contains("allowed-tag", p.AllowedTags);
        Assert.Contains("device-a", p.EnabledDevices);
        Assert.Contains("del-folder", p.EnableContentDeletionFromFolders);
        Assert.Contains(chan1, p.EnabledChannels);
        Assert.Contains(chan1, p.BlockedChannels);
        Assert.Contains(folder1, p.BlockedMediaFolders);
        Assert.Equal(4, p.InvalidLoginAttemptCount);
        Assert.Equal(7, p.LoginAttemptsBeforeLockout);
        Assert.Equal(3_000_000, p.RemoteClientBitrateLimit);
        Assert.Equal(SyncPlayUserAccessType.JoinGroups, p.SyncPlayAccess);
        Assert.Equal(OidcProviderId, p.AuthenticationProviderId);
        Assert.Equal("DefaultPasswordResetProvider", p.PasswordResetProviderId);
    }

    [Fact]
    public void UserPolicy_EveryPropertyIsClassified_ElseReadCurrentPolicyMayNeedUpdating()
    {
        var actual = typeof(UserPolicy)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .ToHashSet();

        var classified = new HashSet<string>(RbacOwnedPolicyFields);
        classified.UnionWith(PreservedPolicyFields);

        var unaccounted = actual.Except(classified).OrderBy(n => n).ToList();
        var stale = classified.Except(actual).Where(n => n.Length > 0).OrderBy(n => n).ToList();

        Assert.True(
            unaccounted.Count == 0,
            "UserPolicy has field(s) not classified as RBAC-owned or preserved - re-check "
            + "RbacService.ReadCurrentPolicy, then add them to one of the sets in this test: "
            + string.Join(", ", unaccounted));
        Assert.True(
            stale.Count == 0,
            "This test's field lists reference UserPolicy field(s) that no longer exist: "
            + string.Join(", ", stale));
    }
}
