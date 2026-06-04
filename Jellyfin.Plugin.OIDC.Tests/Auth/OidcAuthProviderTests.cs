using Jellyfin.Plugin.OIDC.Auth;
using MediaBrowser.Controller.Authentication;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Auth;

public class OidcAuthProviderTests
{
    private readonly OidcAuthProvider _provider = new();

    [Fact]
    public async Task Authenticate_AlwaysThrowsAuthenticationException()
        => await Assert.ThrowsAsync<AuthenticationException>(
            () => _provider.Authenticate("alice", "password123"));

    [Fact]
    public void HasPassword_AlwaysReturnsFalse()
        => Assert.False(_provider.HasPassword(null!));

    [Fact]
    public async Task ChangePassword_AlwaysThrowsNotSupportedException()
        => await Assert.ThrowsAsync<NotSupportedException>(
            () => _provider.ChangePassword(null!, "newpass"));
}
