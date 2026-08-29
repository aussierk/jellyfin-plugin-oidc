using System.Net;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Services;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class AuthorityGuardTests
{
    // ── IsLoopback / IsLinkLocal / IsPrivateNetworkOrUla ────────────────────────

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.5.5.5")]
    [InlineData("::1")]
    public void IsLoopback_LoopbackAddress_ReturnsTrue(string ip)
    {
        Assert.True(AuthorityGuard.IsLoopback(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("169.254.1.1")]
    [InlineData("8.8.8.8")]
    public void IsLoopback_NonLoopbackAddress_ReturnsFalse(string ip)
    {
        Assert.False(AuthorityGuard.IsLoopback(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("169.254.1.1")]
    [InlineData("169.254.255.254")]
    public void IsLinkLocal_LinkLocalIPv4_ReturnsTrue(string ip)
    {
        Assert.True(AuthorityGuard.IsLinkLocal(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsLinkLocal_LinkLocalIPv6_ReturnsTrue()
    {
        Assert.True(AuthorityGuard.IsLinkLocal(IPAddress.Parse("fe80::1")));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("10.0.0.5")]
    public void IsLinkLocal_NonLinkLocalAddress_ReturnsFalse(string ip)
    {
        Assert.False(AuthorityGuard.IsLinkLocal(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.5")]
    public void IsPrivateNetworkOrUla_Rfc1918Address_ReturnsTrue(string ip)
    {
        Assert.True(AuthorityGuard.IsPrivateNetworkOrUla(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivateNetworkOrUla_UlaIPv6Address_ReturnsTrue()
    {
        Assert.True(AuthorityGuard.IsPrivateNetworkOrUla(IPAddress.Parse("fd00::1")));
    }

    [Theory]
    [InlineData("172.15.255.255")] // just below 172.16/12
    [InlineData("172.32.0.0")]     // just above 172.16/12
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    public void IsPrivateNetworkOrUla_NonPrivateAddress_ReturnsFalse(string ip)
    {
        Assert.False(AuthorityGuard.IsPrivateNetworkOrUla(IPAddress.Parse(ip)));
    }

    // ── ValidateAndResolveAsync ────────────────────────────────────────────────
    // ValidateAsync (a resolve-and-discard-address wrapper with no production caller) was
    // removed; only the cases below that weren't already covered were migrated here.

    [Fact]
    public async Task ValidateAndResolveAsync_LinkLocalIpLiteralAuthority_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://169.254.169.254/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("link-local address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_LinkLocalAuthority_WithLinkLocalOptOut_ReturnsNull()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://169.254.169.254/", allowLoopback: false, allowLinkLocal: true, blockPrivateNetworks: false);

        Assert.Null(blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_LinkLocalAuthority_WithOnlyLoopbackOptOut_StillBlocked()
    {
        // Opt-outs are independent - allowing loopback must not also allow link-local.
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://169.254.169.254/", allowLoopback: true, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("link-local address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_LoopbackAuthority_WithOnlyLinkLocalOptOut_StillBlocked()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://127.0.0.1/", allowLoopback: false, allowLinkLocal: true, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("loopback address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_Rfc1918Authority_BlockPrivateNetworksFalse_ReturnsNull()
    {
        // RFC1918 is allowed by default (blockPrivateNetworks off) - no opt-out needed.
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://10.0.40.10/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_UlaAuthority_BlockPrivateNetworksTrue_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://[fd00::1]/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.NotNull(blockReason);
        Assert.Contains("private-network address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_PublicAuthority_BlockPrivateNetworksTrue_ReturnsNull()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://8.8.8.8/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.Null(blockReason);
    }

    // ── ValidateAndResolveAsync - pinned-address behaviour ─────────────────────

    [Fact]
    public async Task ValidateAndResolveAsync_PublicIpLiteralAuthority_ReturnsAddressAndNoBlockReason()
    {
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://8.8.8.8/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(blockReason);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), pinnedAddress);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_LoopbackIpLiteralAuthority_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://127.0.0.1/realms/test", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("loopback address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_LoopbackAuthority_WithLoopbackOptOut_ReturnsAddress()
    {
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://127.0.0.1/realms/test", allowLoopback: true, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(blockReason);
        Assert.Equal(IPAddress.Parse("127.0.0.1"), pinnedAddress);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_Rfc1918Authority_BlockPrivateNetworksTrue_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://10.0.40.10/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.NotNull(blockReason);
        Assert.Contains("private-network address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_MalformedAuthority_ReturnsBlockReason()
    {
        // Fail closed: an unparseable Authority must not fall through to an unguarded fetch.
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            "not-a-valid-url", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Null(pinnedAddress);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_NonHttpScheme_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "ftp://example.com/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("scheme", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_UnresolvableHost_ReturnsBlockReason()
    {
        // .invalid is reserved (RFC 6761) and never resolves - exercises the fail-closed DNS path.
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://oidc-guard-test.invalid/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Null(pinnedAddress);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_UnspecifiedLiteralAuthority_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://0.0.0.0/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("unspecified address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_IPv4MappedLinkLocalLiteral_ReturnsBlockReason()
    {
        // ::ffff:169.254.169.254 - the cloud-metadata address in IPv4-mapped form.
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://[::ffff:169.254.169.254]/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(blockReason);
        Assert.Contains("link-local address", blockReason);
    }

    [Fact]
    public async Task ValidateAndResolveAsync_IPv4MappedRfc1918Literal_BlockPrivateNetworksTrue_ReturnsBlockReason()
    {
        var (blockReason, _) = await AuthorityGuard.ValidateAndResolveAsync(
            "https://[::ffff:10.0.0.5]/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.NotNull(blockReason);
        Assert.Contains("private-network address", blockReason);
    }

    // ── IPv4-mapped IPv6 + CGNAT + unspecified classification ─────────────────────

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    public void IsLoopback_IPv4MappedLoopback_ReturnsTrue(string ip)
        => Assert.True(AuthorityGuard.IsLoopback(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("::ffff:169.254.169.254")]
    public void IsLinkLocal_IPv4MappedLinkLocal_ReturnsTrue(string ip)
        => Assert.True(AuthorityGuard.IsLinkLocal(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:192.168.1.1")]
    public void IsPrivateNetworkOrUla_IPv4Mapped_ReturnsTrue(string ip)
        => Assert.True(AuthorityGuard.IsPrivateNetworkOrUla(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    public void IsPrivateNetworkOrUla_CgnatAddress_ReturnsTrue(string ip)
        => Assert.True(AuthorityGuard.IsPrivateNetworkOrUla(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    public void IsPrivateNetworkOrUla_JustOutsideCgnat_ReturnsFalse(string ip)
        => Assert.False(AuthorityGuard.IsPrivateNetworkOrUla(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::ffff:0.0.0.0")]
    public void IsUnspecified_AnyAddress_ReturnsTrue(string ip)
        => Assert.True(AuthorityGuard.IsUnspecified(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    public void IsUnspecified_RoutableOrLoopback_ReturnsFalse(string ip)
        => Assert.False(AuthorityGuard.IsUnspecified(IPAddress.Parse(ip)));

    // ── CreatePinnedHttpClient ────────────────────────────────────────────────────

    [Fact]
    public void CreatePinnedHttpClient_ReturnsUsableHttpClient()
    {
        // Socket-level pinning needs a real network peer; this only checks the factory shape.
        using var client1 = AuthorityGuard.CreatePinnedHttpClient(IPAddress.Parse("8.8.8.8"));
        using var client2 = AuthorityGuard.CreatePinnedHttpClient(IPAddress.Parse("8.8.8.8"));

        Assert.NotNull(client1);
        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void CreatePinnedHttpClient_AllowAutoRedirectFalse_ReturnsUsableHttpClient()
    {
        // Not reflecting into BCL internals to assert the flag directly (fragile across .NET versions).
        using var client = AuthorityGuard.CreatePinnedHttpClient(IPAddress.Parse("8.8.8.8"), allowAutoRedirect: false);

        Assert.NotNull(client);
    }
}
