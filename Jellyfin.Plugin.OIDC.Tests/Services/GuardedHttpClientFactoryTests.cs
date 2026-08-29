using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

[Xunit.Collection("OidcPlugin")]
public class GuardedHttpClientFactoryTests
{
    private readonly PluginTestFixture _fixture;

    public GuardedHttpClientFactoryTests(PluginTestFixture fixture) => _fixture = fixture;

    private static GuardedHttpClientFactory MakeFactory()
        => new((_, _) => new HttpClient());

    [Fact]
    public async Task CreateAsync_PublicIpLiteral_ReturnsClientAndNoBlockReason()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var factory = MakeFactory();

        var result = await factory.CreateAsync("https://8.8.8.8/token", allowLoopback: false, allowLinkLocal: false);

        Assert.False(result.Blocked);
        Assert.NotNull(result.Client);
    }

    [Fact]
    public async Task CreateAsync_LoopbackWithoutOptIn_IsBlockedWithNoClient()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var factory = MakeFactory();

        var result = await factory.CreateAsync("https://127.0.0.1/token", allowLoopback: false, allowLinkLocal: false);

        Assert.True(result.Blocked);
        Assert.Null(result.Client);
        Assert.Contains("loopback", result.BlockReason);
    }

    [Fact]
    public async Task CreateAsync_ProviderOverload_UsesProviderLoopbackAllowance()
    {
        _fixture.SetConfiguration(new PluginConfiguration());
        var factory = MakeFactory();
        var provider = new OidcProviderConfig { AllowLoopbackAuthority = true };

        var result = await factory.CreateAsync("https://127.0.0.1/.well-known/openid-configuration", provider);

        Assert.False(result.Blocked);
        Assert.NotNull(result.Client);
    }

    [Fact]
    public async Task CreateAsync_Rfc1918_BlockedOnlyWhenPluginSettingOn()
    {
        var factory = MakeFactory();

        _fixture.SetConfiguration(new PluginConfiguration { BlockPrivateNetworkAuthorities = false });
        Assert.False((await factory.CreateAsync("https://10.1.2.3/token", false, false)).Blocked);

        _fixture.SetConfiguration(new PluginConfiguration { BlockPrivateNetworkAuthorities = true });
        var blocked = await factory.CreateAsync("https://10.1.2.3/token", false, false);
        Assert.True(blocked.Blocked);
        Assert.Contains("private-network", blocked.BlockReason);
    }
}
