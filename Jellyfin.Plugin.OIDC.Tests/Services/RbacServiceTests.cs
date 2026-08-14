using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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

    private static RbacService MakeService(IUserManager userManager, ILibraryManager libraryManager)
        => new(userManager, libraryManager, NullLogger<RbacService>.Instance);

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
