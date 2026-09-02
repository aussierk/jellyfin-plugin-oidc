using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class KnownProviderIconsTests
{
    [Theory]
    [InlineData("authentik")]
    [InlineData("keycloak")]
    [InlineData("google")]
    [InlineData("microsoft")]
    [InlineData("okta")]
    [InlineData("auth0")]
    [InlineData("discord")]
    [InlineData("github")]
    public void TryGet_KnownKey_ReturnsSvgDataUri(string key)
    {
        var uri = KnownProviderIcons.TryGet(key);

        Assert.NotNull(uri);
        Assert.StartsWith("data:image/svg+xml;base64,", uri);
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        Assert.Equal(KnownProviderIcons.TryGet("authentik"), KnownProviderIcons.TryGet("AuthentiK"));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGet_UnknownOrBlank_ReturnsNull(string? key)
    {
        Assert.Null(KnownProviderIcons.TryGet(key));
    }

    [Fact]
    public void Keys_AreTheSixBundledProviders()
    {
        Assert.Equal(
            new[] { "authentik", "keycloak", "google", "microsoft", "okta", "auth0", "discord", "github" },
            KnownProviderIcons.Keys);
    }
}
