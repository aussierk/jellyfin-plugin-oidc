using System.Net;
using System.Net.Http;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

[Xunit.Collection("OidcPlugin")]
public class UserSyncServiceTests
{
    private const string OidcProviderId = "Jellyfin.Plugin.OIDC.Auth.OidcAuthProvider";
    private const string LocalProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";
    private const string PasswordResetProviderId = "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider";

    private readonly PluginTestFixture _fixture;

    public UserSyncServiceTests(PluginTestFixture fixture) => _fixture = fixture;

    private UserSyncService MakeService(IUserManager userManager)
    {
        var libraryManager = Substitute.For<ILibraryManager>();
        var rbac = new RbacService(userManager, libraryManager, NullLogger<RbacService>.Instance);
        // Not exercised by these tests (no profile-image sync coverage here).
        HttpClient PinnedClientFactory(IPAddress address, bool allowAutoRedirect) => new();

        var profileImageService = new ProfileImageService(
            Substitute.For<IHttpClientFactory>(),
            PinnedClientFactory,
            userManager,
            Substitute.For<IServerConfigurationManager>(),
            Substitute.For<IProviderManager>(),
            NullLogger<ProfileImageService>.Instance);
        return new UserSyncService(userManager, rbac, profileImageService, NullLogger<UserSyncService>.Instance);
    }

    private static User MakeOidcUser(string username, bool disabled = false)
    {
        var user = new User(username, OidcProviderId, PasswordResetProviderId);
        if (disabled)
        {
            user.SetPermission(PermissionKind.IsDisabled, true);
        }

        return user;
    }

    private static User MakeLocalUser(string username)
        => new User(username, LocalProviderId, PasswordResetProviderId);

    // ── username validation ────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyUsername_Throws()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("", null, "keycloak"));
    }

    [Fact]
    public async Task WhitespaceUsername_Throws()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("   ", null, "keycloak"));
    }

    [Fact]
    public async Task Username255Chars_IsValid()
    {
        // Arrange — 255 chars is exactly at the limit, must not throw on length validation
        var atLimit = new string('a', 255);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName(atLimit).Returns(MakeOidcUser(atLimit));
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = atLimit, ProviderId = "keycloak" }]
        });

        // Act
        var userId = await MakeService(userManager).SyncUserAsync(atLimit, null, "keycloak");

        // Assert
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Username256Chars_Throws()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });
        var overLimit = new string('a', 256);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync(overLimit, null, "keycloak"));
    }

    [Fact]
    public async Task ControlCharUsername_Throws()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("alice\x00", null, "keycloak"));
    }

    // ── new user creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task NewUser_AutoCreateEnabled_ReturnsNewUserId()
    {
        // Arrange
        var newUser = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(newUser));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act
        var userId = await MakeService(userManager).SyncUserAsync("alice", "Alice", "keycloak");

        // Assert
        Assert.Equal(newUser.Id, userId);
    }

    [Fact]
    public async Task NewUser_AutoCreateEnabled_SetsOidcAuthProvider()
    {
        // Arrange
        var newUser = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(newUser));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act
        await MakeService(userManager).SyncUserAsync("alice", "Alice", "keycloak");

        // Assert
        Assert.Equal(OidcProviderId, newUser.AuthenticationProviderId);
    }

    [Fact]
    public async Task NewUser_AutoCreateEnabled_RegistersInUserProviderMap()
    {
        // Arrange
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(MakeOidcUser("alice")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration { AutoCreateUsers = true };
        _fixture.SetConfiguration(config);

        // Act
        await MakeService(userManager).SyncUserAsync("alice", null, "keycloak");

        // Assert
        Assert.Contains(config.UserProviderMap,
            e => e.Username == "alice" && e.ProviderId == "keycloak");
    }

    [Fact]
    public async Task NewUser_AutoCreateDisabled_Throws()
    {
        // Arrange
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = false });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, "keycloak"));
    }

    // ── existing OIDC user ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExistingOidcUser_SameProvider_ReturnsUserId()
    {
        // Arrange
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        });

        // Act
        var userId = await MakeService(userManager).SyncUserAsync("alice", null, "keycloak");

        // Assert
        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task ExistingOidcUser_CrossProvider_Throws()
    {
        // Arrange — alice was created by "keycloak"; "okta" tries to log in as alice
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, "okta"));
    }

    // ── local user migration ───────────────────────────────────────────────────

    [Fact]
    public async Task LocalUser_MigrateDisabled_Throws()
    {
        // Arrange
        var user = MakeLocalUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration { MigrateLocalUsers = false });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, "keycloak"));
    }

    [Fact]
    public async Task LocalUser_MigrateEnabled_UpdatesAuthProvider()
    {
        // Arrange
        var user = MakeLocalUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration { MigrateLocalUsers = true });

        // Act
        await MakeService(userManager).SyncUserAsync("alice", null, "keycloak");

        // Assert
        Assert.Equal(OidcProviderId, user.AuthenticationProviderId);
    }

    // ── disabled user ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisabledUser_AlwaysThrows()
    {
        // Arrange
        var user = MakeOidcUser("alice", disabled: true);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, "keycloak"));
    }
}
