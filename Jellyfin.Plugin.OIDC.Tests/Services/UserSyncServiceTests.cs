using System.Reflection;
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
        var profileImageService = new ProfileImageService(
            TestHttp.GuardedFactory(),
            userManager,
            Substitute.For<IServerConfigurationManager>(),
            Substitute.For<IProviderManager>(),
            NullLogger<ProfileImageService>.Instance);
        return new UserSyncService(userManager, rbac, profileImageService, _fixture.MapStore, NullLogger<UserSyncService>.Instance);
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
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("", null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task WhitespaceUsername_Throws()
    {
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("   ", null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task Username255Chars_IsValid()
    {
        // 255 chars is exactly at the limit.
        var atLimit = new string('a', 255);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName(atLimit).Returns(MakeOidcUser(atLimit));
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = atLimit, ProviderId = "keycloak" }]
        });

        var userId = await MakeService(userManager).SyncUserAsync(atLimit, null, null, null, false, "keycloak");

        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Username256Chars_Throws()
    {
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });
        var overLimit = new string('a', 256);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync(overLimit, null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task ControlCharUsername_Throws()
    {
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync("alice\x00", null, null, null, false, "keycloak"));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../etc")]
    public async Task PathSeparatorInUsername_Throws(string username)
    {
        // Must fail here with a clean InvalidOperationException (-> 403), not fall through to
        // CreateUserAsync and surface as an unhandled ArgumentException (-> 500).
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(Substitute.For<IUserManager>()).SyncUserAsync(username, null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task CreateUserAsync_RejectsUsername_MappedToCleanDeny()
    {
        // A username the up-front checks let through but Jellyfin's own rules reject: the
        // ArgumentException must be mapped to InvalidOperationException (-> 403), not bubble as a 500.
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("weird.name").Returns((User?)null);
        userManager.CreateUserAsync("weird.name")
            .Returns<Task<User>>(_ => Task.FromException<User>(new ArgumentException("Usernames can contain unicode symbols, numbers...")));
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("weird.name", null, null, null, false, "keycloak"));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    // ── new user creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task NewUser_AutoCreateEnabled_ReturnsNewUserId()
    {
        var newUser = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(newUser));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        var userId = await MakeService(userManager).SyncUserAsync("alice", "Alice", null, null, false, "keycloak");

        Assert.Equal(newUser.Id, userId);
    }

    [Fact]
    public async Task NewUser_AutoCreateEnabled_SetsOidcAuthProvider()
    {
        var newUser = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(newUser));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        await MakeService(userManager).SyncUserAsync("alice", "Alice", null, null, false, "keycloak");

        Assert.Equal(OidcProviderId, newUser.AuthenticationProviderId);
    }

    [Fact]
    public async Task NewUser_AutoCreateEnabled_RegistersInUserProviderMap()
    {
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        userManager.CreateUserAsync("alice").Returns(Task.FromResult(MakeOidcUser("alice")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration { AutoCreateUsers = true };
        _fixture.SetConfiguration(config);

        await MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak");

        Assert.Contains(config.UserProviderMap,
            e => e.Username == "alice" && e.ProviderId == "keycloak");
    }

    [Fact]
    public async Task NewUser_AutoCreateDisabled_Throws()
    {
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns((User?)null);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = false });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak"));
    }

    // ── existing OIDC user ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExistingOidcUser_SameProvider_ReturnsUserId()
    {
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        });

        var userId = await MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak");

        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task ExistingOidcUser_CrossProvider_Throws()
    {
        // Alice was created by "keycloak"; "okta" tries to log in as alice.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "okta"));
    }

    // ── cross-provider account reclaim by verified email ──────────────────────

    [Fact]
    public async Task LinkExistingUsersByEmail_CrossProvider_ReclaimsAndReownsRow()
    {
        // Alice was owned by "keycloak"; the admin switched to "authentik". Same username, matching verified email.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            Providers = [new OidcProviderConfig { ProviderId = "authentik", TrustedForEmailLinking = true }],
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        };
        _fixture.SetConfiguration(config);

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice", null, "new-sub", "alice@example.com", emailVerified: true, "authentik");

        Assert.Equal(user.Id, userId);
        var row = Assert.Single(config.UserProviderMap);
        Assert.Equal("authentik", row.ProviderId);
        Assert.Equal("new-sub", row.Subject);
    }

    [Fact]
    public async Task LinkExistingUsersByEmail_CrossProvider_DifferentUsername_Reclaims()
    {
        // alice2's login resolves via the by-email link, not by username (which also changed).
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice2").Returns((User?)null);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            Providers = [new OidcProviderConfig { ProviderId = "authentik", TrustedForEmailLinking = true }],
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        };
        _fixture.SetConfiguration(config);

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice2", null, "new-sub", "alice@example.com", emailVerified: true, "authentik");

        Assert.Equal(user.Id, userId);
        var row = Assert.Single(config.UserProviderMap);
        Assert.Equal("authentik", row.ProviderId);
    }

    [Fact]
    public async Task UsernameOnlyMatch_CrossProvider_WithoutVerifiedEmail_StillThrows()
    {
        // No verified email at all - the cross-provider-takeover protection must still hold.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(userManager)
            .SyncUserAsync("alice", null, "new-sub", null, emailVerified: false, "authentik"));
    }

    // ── same-provider subject mismatch (username collision) ────────────────────
    // Regression coverage for a silent-impersonation bug: a second principal at the SAME
    // provider presenting a colliding preferred_username must not be let in as (or overwrite
    // ownership of) the existing account just because the username matched.

    [Fact]
    public async Task SameProvider_UsernameMatchWithDifferingSubject_NoVerifiedEmail_Throws()
    {
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "victim-sub", UserId = user.Id.ToString()
            }]
        });

        // Same provider, same preferred_username, but a different subject and no verified email
        // to vouch for the caller - must be denied, not silently authenticated as alice.
        await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(userManager)
            .SyncUserAsync("alice", null, "attacker-sub", null, emailVerified: false, "keycloak"));
    }

    [Fact]
    public async Task SameProvider_UsernameMatchWithDifferingSubject_UnvouchedVerifiedEmail_Throws()
    {
        // The caller supplies a DIFFERENT verified email (not the account's stored one), so the
        // byEmail lookup at the top of SyncUserAsync can't vouch for them either - this must not
        // be enough to rewrite the account's ownership to the attacker's subject.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", TrustedForEmailLinking = true }],
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "victim-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        };
        _fixture.SetConfiguration(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(userManager)
            .SyncUserAsync("alice", null, "attacker-sub", "attacker@evil.com", emailVerified: true, "keycloak"));

        // Ownership must be untouched - the old bug rewrote Subject/UserId here instead of throwing.
        var row = Assert.Single(config.UserProviderMap);
        Assert.Equal("victim-sub", row.Subject);
        Assert.Equal(user.Id.ToString(), row.UserId);
    }

    [Fact]
    public async Task SameProvider_UsernameMatchWithDifferingSubject_VouchedByMatchingVerifiedEmail_Reclaims()
    {
        // Legitimate case (must keep working): the caller's verified email matches the account's
        // stored verified email, so the byEmail link vouches for them even though their subject
        // (e.g. after an IdP-side identity rebuild) differs from what's on file.
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice").Returns(user);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", TrustedForEmailLinking = true }],
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        };
        _fixture.SetConfiguration(config);

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice", null, "new-sub", "alice@example.com", emailVerified: true, "keycloak");

        Assert.Equal(user.Id, userId);
        Assert.Equal("new-sub", Assert.Single(config.UserProviderMap).Subject);
    }

    // ── email-link trust gate + admin exclusion ──────────────────────────────

    [Fact]
    public async Task LinkExistingUsersByEmail_ProviderNotTrusted_DoesNotLink_CreatesNewAccount()
    {
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(user.Id).Returns(user);
        userManager.GetUserByName("alice2").Returns((User?)null);
        userManager.CreateUserAsync("alice2").Returns(Task.FromResult(MakeOidcUser("alice2")));
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            // "authentik" is configured but NOT TrustedForEmailLinking.
            Providers = [new OidcProviderConfig { ProviderId = "authentik", TrustedForEmailLinking = false }],
            UserProviderMap = [new UserProviderEntry
            {
                Username = "alice", ProviderId = "keycloak", Subject = "old-sub", UserId = user.Id.ToString(),
                Email = "alice@example.com", EmailVerified = true
            }]
        };
        _fixture.SetConfiguration(config);

        var userId = await MakeService(userManager)
            .SyncUserAsync("alice2", null, "new-sub", "alice@example.com", emailVerified: true, "authentik");

        Assert.NotEqual(user.Id, userId);
        await userManager.Received(1).CreateUserAsync("alice2");
    }

    [Fact]
    public async Task EmailLink_ToAdministratorAccount_IsRefused()
    {
        var admin = MakeOidcUser("root");
        admin.SetPermission(PermissionKind.IsAdministrator, true);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(admin.Id).Returns(admin);
        userManager.GetUserByName("root").Returns(admin);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            LinkExistingUsersByEmail = true,
            Providers = [new OidcProviderConfig { ProviderId = "authentik", TrustedForEmailLinking = true }],
            UserProviderMap = [new UserProviderEntry
            {
                Username = "root", ProviderId = "keycloak", Subject = "old-sub", UserId = admin.Id.ToString(),
                Email = "root@example.com", EmailVerified = true
            }]
        };
        _fixture.SetConfiguration(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(userManager)
            .SyncUserAsync("root", null, "new-sub", "root@example.com", emailVerified: true, "authentik"));
    }

    [Fact]
    public async Task UsernameMatch_ToAdministratorAccount_WithoutSubjectMatch_IsRefused()
    {
        var admin = MakeOidcUser("admin");
        admin.SetPermission(PermissionKind.IsAdministrator, true);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("admin").Returns(admin);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry
            {
                Username = "admin", ProviderId = "keycloak", Subject = "real-admin-sub", UserId = admin.Id.ToString()
            }]
        });

        // Fresh subject, username "admin" collides with the existing administrator - must not bind.
        await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(userManager)
            .SyncUserAsync("admin", null, "attacker-sub", null, emailVerified: false, "keycloak"));
    }

    [Fact]
    public async Task SubjectMatch_ToAdministratorAccount_StillWorks()
    {
        // The safe path: an admin whose OIDC subject is on file signs in normally.
        var admin = MakeOidcUser("admin");
        admin.SetPermission(PermissionKind.IsAdministrator, true);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserById(admin.Id).Returns(admin);
        _fixture.SetConfiguration(new PluginConfiguration
        {
            UserProviderMap = [new UserProviderEntry
            {
                Username = "admin", ProviderId = "keycloak", Subject = "admin-sub", UserId = admin.Id.ToString()
            }]
        });

        var userId = await MakeService(userManager)
            .SyncUserAsync("admin", null, "admin-sub", null, emailVerified: false, "keycloak");

        Assert.Equal(admin.Id, userId);
    }

    // ── local user migration ───────────────────────────────────────────────────

    [Fact]
    public async Task LocalUser_MigrateDisabled_Throws()
    {
        var user = MakeLocalUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration { MigrateLocalUsers = false });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak"));
    }

    [Fact]
    public async Task LocalUser_MigrateEnabled_UpdatesAuthProvider()
    {
        var user = MakeLocalUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        _fixture.SetConfiguration(new PluginConfiguration { MigrateLocalUsers = true });

        await MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak");

        Assert.Equal(OidcProviderId, user.AuthenticationProviderId);
    }

    // ── disabled user ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisabledUser_AlwaysThrows()
    {
        var user = MakeOidcUser("alice", disabled: true);
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        _fixture.SetConfiguration(new PluginConfiguration { AutoCreateUsers = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeService(userManager).SyncUserAsync("alice", null, null, null, false, "keycloak"));
    }

    // ── sub-keyed identity (1.1) ───────────────────────────────────────────────

    [Fact]
    public async Task SubKeyedLookup_ResolvesByUserId_EvenWhenUsernameDiffers()
    {
        // The map row points at a user whose Jellyfin name is no longer the claim name.
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

        // Claim username is "alice" but subject matches the row.
        var userId = await MakeService(userManager).SyncUserAsync("alice", null, "sub-123", null, false, "keycloak");

        // Resolved the existing account, did not create a new one.
        Assert.Equal(user.Id, userId);
        await userManager.DidNotReceive().CreateUserAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task LegacyUsernameOnlyRow_BackfillsSubjectAndUserId()
    {
        // A pre-sub-keying row (Subject/UserId empty).
        var user = MakeOidcUser("alice");
        var userManager = Substitute.For<IUserManager>();
        userManager.GetUserByName("alice").Returns(user);
        var config = new PluginConfiguration
        {
            AutoCreateUsers = true,
            UserProviderMap = [new UserProviderEntry { Username = "alice", ProviderId = "keycloak" }]
        };
        _fixture.SetConfiguration(config);

        await MakeService(userManager).SyncUserAsync("alice", null, "sub-xyz", null, false, "keycloak");

        // The row self-healed.
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
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", TrustedForEmailLinking = true }],
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
        // The stored row's email was never verified, even though the incoming one matches exactly.
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
            Providers = [new OidcProviderConfig { ProviderId = "keycloak", TrustedForEmailLinking = true }],
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

    [Fact]
    public void Upsert_ConcurrentCalls_DoNotCorruptOrLoseEntries()
    {
        // Regression: the map is a plain List<T> mutated via RemoveAll/Add; concurrent logins used
        // to be able to race on it (corrupted enumeration or a lost write). UserProviderMapStore
        // now serializes the whole read-modify-write internally.
        _fixture.SetConfiguration(new PluginConfiguration());
        var store = _fixture.MapStore;

        const int concurrentLogins = 200;
        Parallel.For(0, concurrentLogins, i =>
        {
            store.Upsert(new UserProviderEntry
            {
                Subject = $"sub-{i}", Username = $"user-{i}", UserId = Guid.NewGuid().ToString(), ProviderId = "keycloak"
            });
        });

        var entries = store.Snapshot();
        Assert.Equal(concurrentLogins, entries.Count);
        Assert.Equal(concurrentLogins, entries.Select(e => e.Username).Distinct().Count());
    }
}
