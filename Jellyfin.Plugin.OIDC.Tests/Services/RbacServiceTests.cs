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
        // Arrange
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(Arg.Any<Guid>()).Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "admin", IsAdmin = true }]
        });

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(Guid.NewGuid(), ["admin"], "keycloak");

        // Assert
        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    // ── role matching ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MatchingRole_SetsIsAdminTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["admins"], "keycloak");

        // Assert
        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.IsAdministrator));
    }

    [Fact]
    public async Task MatchingRole_SetsEnableMediaPlayback()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "viewers", EnableMediaPlayback = true }]
        });

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["viewers"], "keycloak");

        // Assert
        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.EnableMediaPlayback));
    }

    // ── provider filter ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProviderFilter_NonMatchingProvider_LoginDenied()
    {
        // Arrange — mapping is scoped to "keycloak"; user comes from "okta"
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

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeService(userManager, Substitute.For<ILibraryManager>())
                .ApplyRoleMappingsAsync(userId, ["admins"], "okta"));

        // Assert
        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    [Fact]
    public async Task ProviderFilter_EmptyFilter_AppliesToAllProviders()
    {
        // Arrange
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

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["viewers"], "any-provider");

        // Assert
        await userManager.Received(1).UpdatePolicyAsync(userId, Arg.Any<UserPolicy>());
    }

    // ── merge: union of permissions ────────────────────────────────────────────

    [Fact]
    public async Task MergeMappings_UnionOfPermissions()
    {
        // Arrange — role-a grants admin, role-b grants media playback; merged result must have both
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

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["role-a", "role-b"], "keycloak");

        // Assert
        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => p.IsAdministrator && p.EnableMediaPlayback));
    }

    // ── default role fallback ──────────────────────────────────────────────────

    [Fact]
    public async Task DefaultRoleFallback_WhenNoRolesMatch()
    {
        // Arrange — user has no matching roles; DefaultRoleName must kick in
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

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, [], "keycloak");

        // Assert
        await userManager.Received(1)
            .UpdatePolicyAsync(userId, Arg.Is<UserPolicy>(p => !p.IsAdministrator && p.EnableMediaPlayback));
    }

    [Fact]
    public async Task NoMatch_NoDefault_LoginDeniedAndPolicyNotChanged()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        _fixture.SetConfiguration(new PluginConfiguration
        {
            DefaultRoleName = "",
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeService(userManager, Substitute.For<ILibraryManager>())
                .ApplyRoleMappingsAsync(userId, ["viewers"], "keycloak"));

        // Assert
        Assert.Contains("No role mapping matched", exception.Message);
        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    [Fact]
    public async Task NoMatch_DefaultRoleDoesNotExist_LoginDeniedAndPolicyNotChanged()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser());
        _fixture.SetConfiguration(new PluginConfiguration
        {
            DefaultRoleName = "missing-default",
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeService(userManager, Substitute.For<ILibraryManager>())
                .ApplyRoleMappingsAsync(userId, ["viewers"], "keycloak"));

        // Assert
        Assert.Contains("No role mapping matched", exception.Message);
        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    // ── disabled flag preserved ────────────────────────────────────────────────

    [Fact]
    public async Task DisabledFlag_IsPreservedInPolicy()
    {
        // Arrange — Jellyfin admin disabled this user; RBAC must never re-enable them
        var userId = Guid.NewGuid();
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(userId).Returns(MakeUser(disabled: true));
        userManager.UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "admins", IsAdmin = true }]
        });

        // Act
        await MakeService(userManager, Substitute.For<ILibraryManager>())
            .ApplyRoleMappingsAsync(userId, ["admins"], "keycloak");

        // Assert
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
        Assert.Equal(10, p.MaxParentalRating); // carried forward — no mapping restricts it
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

        // Roles match nothing configured; without ManageUserPolicy this must not
        // throw (fail-closed is part of policy management) and must not touch the policy.
        await svc.ApplyRoleMappingsAsync(user.Id, ["nobody-role"], "keycloak");

        await userManager.DidNotReceive().UpdatePolicyAsync(Arg.Any<Guid>(), Arg.Any<UserPolicy>());
    }

    // ── LibraryAccessMode.Ignore ───────────────────────────────────────────────

    [Fact]
    public async Task LibraryAccessMode_Ignore_PreservesUsersCurrentLibraries()
    {
        var user = MakeUser();
        var libId = Guid.NewGuid();
        user.SetPermission(PermissionKind.EnableAllFolders, false);
        user.SetPreference(PreferenceKind.EnabledFolders, new[] { libId });

        var (svc, _) = ServiceCapturing(out var policy, user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            LibraryAccessMode = LibraryAccessMode.Ignore,
            // The mapping grants ALL libraries; Ignore mode must leave the user's own set alone.
            RoleMappings = [new RoleMapping { RoleName = "viewers", EnableAllLibraries = true }]
        });

        await svc.ApplyRoleMappingsAsync(user.Id, ["viewers"], "keycloak");

        Assert.False(policy()!.EnableAllFolders);
        Assert.Equal(new[] { libId }, policy()!.EnabledFolders);
    }

    [Fact]
    public async Task LibraryAccessMode_Replace_IsDefault_UsesMappingLibraries()
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
        // Arrange
        var libraryManager = Substitute.For<ILibraryManager>();
        libraryManager.GetVirtualFolders().Returns(
        [
            new VirtualFolderInfo { ItemId = "lib-001", Name = "Movies" },
            new VirtualFolderInfo { ItemId = "lib-002", Name = "TV Shows" }
        ]);

        // Act
        var result = MakeService(Substitute.For<IUserManager>(), libraryManager)
            .GetAvailableLibraries();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Movies", result["lib-001"]);
        Assert.Equal("TV Shows", result["lib-002"]);
    }
}
