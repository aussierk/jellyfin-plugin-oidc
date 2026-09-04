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
using MediaBrowser.Model.Globalization;
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
        var rbac = new RbacService(
            userManager, libraryManager, Substitute.For<ILocalizationManager>(), NullLogger<RbacService>.Instance);
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
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("", null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task WhitespaceUsername_Throws()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("   ", null, null, null, false, "keycloak"));
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
        var userId = await MakeService(userManager).SyncUserAsync(atLimit, null, null, null, false, "keycloak");

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
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync(overLimit, null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task ControlCharUsername_Throws()
    {
        // Arrange
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("alice\x00", null, null, null, false, "keycloak"));
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
        var userId = await MakeService(userManager).SyncUserAsync("alice", "Alice", null, null, false, "keycloak");

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
        await MakeService(userManager).SyncUserAsync("alice", "Alice", null, null, false, "keycloak");

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
        await MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak");

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
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak"));
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
        var userId = await MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak");

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
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "okta"));
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
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak"));
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
        await MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak");

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
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak"));
    }

    // ── sub-keyed identity (1.1) ───────────────────────────────────────────────

    [Fact]
    public async Task SubKeyedLookup_ResolvesByUserId_EvenWhenUsernameDiffers()
    {
        // Arrange — the map row points at a user whose Jellyfin name is no longer the claim name.
        var user = MakeOidcUser("renamed-alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice").Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry
            {
                Username = "renamed-alice", ProviderId = "keycloak", Subject = "sub-123", UserId = user.Id.ToString()
            }]
        });

        // Act — claim username is "alice" but subject matches the row.
        var userId = await MakeService(userManager).SyncUserAsync("alice", null, "sub-123", null, false, "keycloak");

        // Assert — resolved the existing account, did not create a new one.
        Assert.Equal(user.Id, userId);
        await userManager.DidNotReceive().CreateUserAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task LegacyUsernameOnlyRow_BackfillsSubjectAndUserId()
    {
        // Arrange — a pre-sub-keying row (Subject/UserId empty).
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        };
        _fixture.SetConfiguration(config);

        // Act
        await MakeService(userManager).SyncUserAsync("alice", null, "sub-xyz", null, false, "keycloak");

        // Assert — the row self-healed.
        var row = Assert.Single(config.UserProviderMap);
        Assert.Equal("sub-xyz", row.Subject);
        Assert.Equal(user.Id.ToString(), row.UserId);
    }

    [Fact]
    public async Task DisplayNameSync_Off_DoesNotRename()
    {
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", SyncDisplayName = false }],
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak", Subject = "s", UserId = user.Id.ToString() }]
        });

        await MakeService(userManager).SyncUserAsync("alice", "Alice Anderson", "s", null, false, "keycloak");

        await userManager.DidNotReceive().RenameUser(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DisplayNameSync_On_RenamesToSanitizedClaim()
    {
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        userManager.RenameUser(user.Id, "alice", Arg.Any<string>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", SyncDisplayName = true }],
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak", Subject = "s", UserId = user.Id.ToString() }]
        };
        _fixture.SetConfiguration(config);

        // "Alice (Ops)!" has characters Jellyfin rejects → folded to spaces + collapsed.
        await MakeService(userManager).SyncUserAsync("alice", "Alice (Ops)!", "s", null, false, "keycloak");

        await userManager.Received().RenameUser(user.Id, "alice", "Alice Ops");
        Assert.Equal("Alice Ops", Assert.Single(config.UserProviderMap).Username);
    }

    [Fact]
    public async Task LinkExistingUsersByEmail_MatchesVerifiedEmailRow()
    {
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice2").Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        });

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice2", null, "new-sub", "ALICE@example.com", emailVerified: true, "keycloak");

        Assert.Equal(user.Id, userId);
        await userManager.DidNotReceive().CreateUserAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task LinkExistingUsersByEmail_StoredEmailNotVerified_DoesNotMatch()
    {
        // Arrange — the stored row's email was never verified, so it can't be a link target
        // even though the incoming login presents a verified email that matches it exactly.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice2").Returns((User?)null);
        userManager.CreateUserAsync("alice2").Returns(Task.FromResult(MakeOidcUser("alice2")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = false
            }]
        });

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice2", null, "new-sub", "alice@example.com", emailVerified: true, "keycloak");

        // A brand-new account was created instead of linking to alice's.
        Assert.NotEqual(user.Id, userId);
        await userManager.Received(1).CreateUserAsync("alice2");
    }

    [Fact]
    public async Task LinkExistingUsersByEmail_DifferentProvider_DoesNotMatch()
    {
        // Arrange — same verified email, but the stored row belongs to a different provider.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice2").Returns((User?)null);
        userManager.CreateUserAsync("alice2").Returns(Task.FromResult(MakeOidcUser("alice2")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "authentik", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        });

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice2", null, "new-sub", "alice@example.com", emailVerified: true, "keycloak");

        Assert.NotEqual(user.Id, userId);
        await userManager.Received(1).CreateUserAsync("alice2");
    }

    [Fact]
    public async Task NewUser_VerifiedEmail_IsStoredAsVerified()
    {
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(MakeOidcUser("alice")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration { AutoCreateUsers = true };
        _fixture.SetConfiguration(config);

        await MakeService(userManager)
            .SyncUserAsync("alice", null, "sub-1", "alice@example.com", emailVerified: true, "keycloak");

        var row = Assert.Single(config.UserProviderMap);
        Assert.Equal("alice@example.com", row.Email);
        Assert.True(row.EmailVerified);
    }

    [Fact]
    public async Task NewUser_UnverifiedEmail_IsNotStored()
    {
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(MakeOidcUser("alice")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration { AutoCreateUsers = true };
        _fixture.SetConfiguration(config);

        await MakeService(userManager)
            .SyncUserAsync("alice", null, "sub-1", "alice@example.com", emailVerified: false, "keycloak");

        var row = Assert.Single(config.UserProviderMap);
        Assert.Equal(string.Empty, row.Email);
        Assert.False(row.EmailVerified);
    }
}
