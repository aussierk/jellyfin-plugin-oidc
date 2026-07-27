using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

[Xunit.Collection("OidcPlugin")]
public class ProfileImageServiceTests
{
    private const string OidcAuthProviderId = "Jellyfin.Plugin.OIDC.Auth.OidcAuthProvider";

    // A public IP literal — AuthorityGuard.ValidateAsync passes it without a real DNS lookup,
    // keeping these tests fast and network-independent (mirrors AuthorityGuardTests' convention).
    private const string PublicPictureUrl = "https://8.8.8.8/avatar.png";
    private const string LoopbackPictureUrl = "https://127.0.0.1/avatar.png";
    private const string TestProviderId = "testprovider";

    private readonly PluginTestFixture _fixture;

    public ProfileImageServiceTests(PluginTestFixture fixture) => _fixture = fixture;

    private static (ProfileImageService Service, IUserManager UserManager, IProviderManager ProviderManager) MakeService(
        HttpMessageHandler handler)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("OidcPluginImage").Returns(new HttpClient(handler));

        var userManager = Substitute.For<IUserManager>();
        userManager.UpdateUserAsync(Arg.Any<User>()).Returns(Task.CompletedTask);
        userManager.ClearProfileImageAsync(Arg.Any<User>()).Returns(Task.CompletedTask);

        var providerManager = Substitute.For<IProviderManager>();
        providerManager.SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);

        var appPaths = Substitute.For<IServerApplicationPaths>();
        appPaths.UserConfigurationDirectoryPath.Returns(Path.Combine(Path.GetTempPath(), "oidc-profileimage-tests"));
        var serverConfigManager = Substitute.For<IServerConfigurationManager>();
        serverConfigManager.ApplicationPaths.Returns(appPaths);

        var service = new ProfileImageService(
            httpClientFactory, userManager, serverConfigManager, providerManager, NullLogger<ProfileImageService>.Instance);

        return (service, userManager, providerManager);
    }

    private static User MakeUser(string username = "alice") => new(username, OidcAuthProviderId, "PasswordResetProviderId");

    private void SetProviderConfig(bool allowLoopback = false, bool allowLinkLocal = false, bool blockPrivateNetworks = false) =>
        _fixture.SetConfiguration(new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    ProviderId = TestProviderId,
                    AllowLoopbackAuthority = allowLoopback,
                    AllowLinkLocalAuthority = allowLinkLocal
                }
            ],
            BlockPrivateNetworkAuthorities = blockPrivateNetworks
        });

    // ── no-op guard clauses ────────────────────────────────────────────────────

    [Fact]
    public async Task NullUrl_DoesNotTouchUserManager()
    {
        // Arrange — a throwing handler proves the method returns before making any HTTP call.
        var (service, userManager, _) = MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        // Act
        await service.ApplyProfileImageAsync(Guid.NewGuid(), null, TestProviderId);

        // Assert
        userManager.DidNotReceive().GetUserById(Arg.Any<Guid>());
    }

    [Fact]
    public async Task EmptyUrl_DoesNotTouchUserManager()
    {
        var (service, userManager, _) = MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        await service.ApplyProfileImageAsync(Guid.NewGuid(), string.Empty, TestProviderId);

        userManager.DidNotReceive().GetUserById(Arg.Any<Guid>());
    }

    [Fact]
    public async Task WhitespaceUrl_DoesNotTouchUserManager()
    {
        var (service, userManager, _) = MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));

        await service.ApplyProfileImageAsync(Guid.NewGuid(), "   ", TestProviderId);

        userManager.DidNotReceive().GetUserById(Arg.Any<Guid>());
    }

    // ── SSRF guard ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ftp://8.8.8.8/avatar.png")]
    [InlineData("file:///etc/passwd")]
    public async Task DisallowedScheme_DoesNotSaveImage(string url)
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), url, TestProviderId);

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoopbackUrl_ProviderDoesNotAllowLoopback_DoesNotSaveImage()
    {
        SetProviderConfig(allowLoopback: false);
        var (service, userManager, providerManager) =
            MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), LoopbackPictureUrl, TestProviderId);

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoopbackUrl_ProviderAllowsLoopback_Proceeds()
    {
        SetProviderConfig(allowLoopback: true);
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), LoopbackPictureUrl, TestProviderId);

        await providerManager.Received(1).SaveImage(Arg.Any<Stream>(), "image/png", Arg.Any<string>());
    }

    [Fact]
    public async Task UnknownProviderId_TreatedAsNoOptOuts_LoopbackBlocked()
    {
        // No provider config matches this ID — the guard must fail closed (no opt-outs), not throw.
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("should never be called")));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), LoopbackPictureUrl, "unknown-provider");

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ── download / validation failures never save, never throw ────────────────

    [Fact]
    public async Task NonSuccessStatusCode_DoesNotSaveImage()
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.NotFound, "not found", "text/plain"));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), PublicPictureUrl, TestProviderId);

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task NonImageContentType_DoesNotSaveImage()
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "<html></html>", "text/html"));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), PublicPictureUrl, TestProviderId);

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task OversizedContentLength_DoesNotSaveImage()
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, new string('a', 100), "image/png", contentLengthOverride: 10L * 1024 * 1024));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), PublicPictureUrl, TestProviderId);

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UserNotFound_DoesNotSaveImage()
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        userManager.GetUserById(Arg.Any<Guid>()).Returns((User?)null);

        await service.ApplyProfileImageAsync(Guid.NewGuid(), PublicPictureUrl, TestProviderId);

        await providerManager.DidNotReceive().SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HttpTransportThrows_DoesNotThrow()
    {
        // Avatar sync must never break login — success is simply not throwing.
        SetProviderConfig();
        var (service, userManager, _) =
            MakeService(new ThrowingHttpMessageHandler(new HttpRequestException("No route to host")));
        userManager.GetUserById(Arg.Any<Guid>()).Returns(MakeUser());

        await service.ApplyProfileImageAsync(Guid.NewGuid(), PublicPictureUrl, TestProviderId);
    }

    [Fact]
    public async Task SaveImageThrows_DoesNotThrow()
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        var userId = Guid.NewGuid();
        userManager.GetUserById(userId).Returns(MakeUser());
        providerManager.SaveImage(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => throw new IOException("disk full"));

        await service.ApplyProfileImageAsync(userId, PublicPictureUrl, TestProviderId);
    }

    // ── success path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Success_SavesImageWithDownloadedMimeType()
    {
        SetProviderConfig();
        var (service, userManager, providerManager) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        var userId = Guid.NewGuid();
        userManager.GetUserById(userId).Returns(MakeUser());

        await service.ApplyProfileImageAsync(userId, PublicPictureUrl, TestProviderId);

        await providerManager.Received(1).SaveImage(Arg.Any<Stream>(), "image/png", Arg.Any<string>());
    }

    [Fact]
    public async Task Success_UpdatesUser()
    {
        SetProviderConfig();
        var (service, userManager, _) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        var userId = Guid.NewGuid();
        var user = MakeUser();
        userManager.GetUserById(userId).Returns(user);

        await service.ApplyProfileImageAsync(userId, PublicPictureUrl, TestProviderId);

        await userManager.Received(1).UpdateUserAsync(user);
    }

    [Fact]
    public async Task Success_SetsProfileImagePathWithMatchingExtension()
    {
        SetProviderConfig();
        var (service, userManager, _) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        var userId = Guid.NewGuid();
        var user = MakeUser();
        userManager.GetUserById(userId).Returns(user);

        await service.ApplyProfileImageAsync(userId, PublicPictureUrl, TestProviderId);

        Assert.NotNull(user.ProfileImage);
        Assert.EndsWith(".png", user.ProfileImage!.Path);
    }

    [Fact]
    public async Task ExistingProfileImage_IsClearedBeforeSettingNewOne()
    {
        SetProviderConfig();
        var (service, userManager, _) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        var userId = Guid.NewGuid();
        var user = MakeUser();
        user.ProfileImage = new ImageInfo("/existing/old-avatar.jpg");
        userManager.GetUserById(userId).Returns(user);

        await service.ApplyProfileImageAsync(userId, PublicPictureUrl, TestProviderId);

        await userManager.Received(1).ClearProfileImageAsync(user);
    }

    [Fact]
    public async Task NoExistingProfileImage_DoesNotCallClear()
    {
        SetProviderConfig();
        var (service, userManager, _) =
            MakeService(new MockHttpMessageHandler(HttpStatusCode.OK, "binary-data", "image/png"));
        var userId = Guid.NewGuid();
        var user = MakeUser();
        userManager.GetUserById(userId).Returns(user);

        await service.ApplyProfileImageAsync(userId, PublicPictureUrl, TestProviderId);

        await userManager.DidNotReceive().ClearProfileImageAsync(Arg.Any<User>());
    }

    // ── extension mapping ──────────────────────────────────────────────────────

    private static readonly MethodInfo _getExtension =
        typeof(ProfileImageService).GetMethod(
            "GetExtensionForMimeType",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/jpg", ".jpg")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/bmp", ".jpg")]
    [InlineData("IMAGE/PNG", ".png")]
    public void GetExtensionForMimeType_MapsKnownTypes(string mimeType, string expectedExtension)
    {
        var result = (string)_getExtension.Invoke(null, [mimeType])!;
        Assert.Equal(expectedExtension, result);
    }
}
