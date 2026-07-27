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

    // ── ValidateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_LoopbackIpLiteralAuthority_ReturnsBlockReason()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://127.0.0.1/realms/test", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(result);
        Assert.Contains("loopback address", result);
    }

    [Fact]
    public async Task ValidateAsync_LinkLocalIpLiteralAuthority_ReturnsBlockReason()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://169.254.169.254/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(result);
        Assert.Contains("link-local address", result);
    }

    [Fact]
    public async Task ValidateAsync_LoopbackAuthority_WithLoopbackOptOut_ReturnsNull()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://127.0.0.1/realms/test", allowLoopback: true, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_LinkLocalAuthority_WithLinkLocalOptOut_ReturnsNull()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://169.254.169.254/", allowLoopback: false, allowLinkLocal: true, blockPrivateNetworks: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_LinkLocalAuthority_WithOnlyLoopbackOptOut_StillBlocked()
    {
        // Opt-outs are independent — allowing loopback must not also allow link-local.
        var result = await AuthorityGuard.ValidateAsync(
            "https://169.254.169.254/", allowLoopback: true, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.NotNull(result);
        Assert.Contains("link-local address", result);
    }

    [Fact]
    public async Task ValidateAsync_LoopbackAuthority_WithOnlyLinkLocalOptOut_StillBlocked()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://127.0.0.1/", allowLoopback: false, allowLinkLocal: true, blockPrivateNetworks: false);

        Assert.NotNull(result);
        Assert.Contains("loopback address", result);
    }

    [Fact]
    public async Task ValidateAsync_PublicIpLiteralAuthority_ReturnsNull()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://8.8.8.8/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_Rfc1918Authority_BlockPrivateNetworksFalse_ReturnsNull()
    {
        // RFC1918 is allowed by default (blockPrivateNetworks off) — no opt-out needed.
        var result = await AuthorityGuard.ValidateAsync(
            "https://10.0.40.10/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_Rfc1918Authority_BlockPrivateNetworksTrue_ReturnsBlockReason()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://10.0.40.10/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.NotNull(result);
        Assert.Contains("private-network address", result);
    }

    [Fact]
    public async Task ValidateAsync_UlaAuthority_BlockPrivateNetworksTrue_ReturnsBlockReason()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://[fd00::1]/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.NotNull(result);
        Assert.Contains("private-network address", result);
    }

    [Fact]
    public async Task ValidateAsync_PublicAuthority_BlockPrivateNetworksTrue_ReturnsNull()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "https://8.8.8.8/", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: true);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_MalformedAuthority_ReturnsNullAndDoesNotThrow()
    {
        var result = await AuthorityGuard.ValidateAsync(
            "not-a-valid-url", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(result);
    }

    // ── ValidateAndResolveAsync ──────────────────────────────────────────────────
    // Mirrors ValidateAsync's cases, plus asserting the resolved address that a caller
    // would pin the subsequent connection to.

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
    public async Task ValidateAndResolveAsync_MalformedAuthority_ReturnsNullReasonAndNullAddress()
    {
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            "not-a-valid-url", allowLoopback: false, allowLinkLocal: false, blockPrivateNetworks: false);

        Assert.Null(blockReason);
        Assert.Null(pinnedAddress);
    }

    // ── CreatePinnedHttpClient ────────────────────────────────────────────────────

    [Fact]
    public void CreatePinnedHttpClient_ReturnsUsableHttpClient()
    {
        // Socket-level pinning behavior needs a real network peer to verify end-to-end, so this
        // only confirms the factory produces a valid, distinct HttpClient per call — the actual
        // ConnectCallback wiring is the standard documented SocketsHttpHandler pinning pattern,
        // verified by inspection rather than a unit test here.
        using var client1 = AuthorityGuard.CreatePinnedHttpClient(IPAddress.Parse("8.8.8.8"));
        using var client2 = AuthorityGuard.CreatePinnedHttpClient(IPAddress.Parse("8.8.8.8"));

        Assert.NotNull(client1);
        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void CreatePinnedHttpClient_AllowAutoRedirectFalse_ReturnsUsableHttpClient()
    {
        // Same rationale as above — the AllowAutoRedirect wiring is a single property
        // assignment onto SocketsHttpHandler, verified by inspection rather than reflecting
        // into BCL internals (fragile across .NET versions) to assert the flag directly.
        using var client = AuthorityGuard.CreatePinnedHttpClient(IPAddress.Parse("8.8.8.8"), allowAutoRedirect: false);

        Assert.NotNull(client);
    }
}
