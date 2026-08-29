using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Configuration;

namespace Jellyfin.Plugin.OIDC.Services;

/// The single entry point for every outbound call to an IdP-controlled URL - discovery, token,
/// JWKS, userinfo and the profile-picture download. It runs <see cref="AuthorityGuard"/> once and,
/// on success, hands back an <see cref="HttpClient"/> pinned to the validated address with
/// redirects disabled; on failure it returns the block reason for the caller to log and map to
/// its own error. The guard fails closed - it never returns "allowed" without a resolved address
/// to pin - so there is no unpinned path out of here.
public sealed class GuardedHttpClientFactory
{
    private readonly Func<IPAddress, bool, HttpClient> _pinnedHttpClientFactory;

    public GuardedHttpClientFactory(Func<IPAddress, bool, HttpClient> pinnedHttpClientFactory)
    {
        _pinnedHttpClientFactory = pinnedHttpClientFactory;
    }

    /// Uses the provider's own loopback / link-local allowances (a null provider allows neither).
    public Task<GuardedHttpClientResult> CreateAsync(string? url, OidcProviderConfig? provider)
        => CreateAsync(
            url,
            provider?.AllowLoopbackAuthority ?? false,
            provider?.AllowLinkLocalAuthority ?? false);

    public async Task<GuardedHttpClientResult> CreateAsync(string? url, bool allowLoopback, bool allowLinkLocal)
    {
        var blockPrivateNetworks = OidcPlugin.CurrentConfig.BlockPrivateNetworkAuthorities;
        var (blockReason, pinnedAddress) = await AuthorityGuard.ValidateAndResolveAsync(
            url ?? string.Empty, allowLoopback, allowLinkLocal, blockPrivateNetworks).ConfigureAwait(false);

        // The pinnedAddress == null arm is belt-and-braces: it stays fail-closed if AuthorityGuard regresses.
        if (blockReason != null || pinnedAddress == null)
        {
            return new GuardedHttpClientResult(
                null, blockReason ?? "Authority did not resolve to a usable address.");
        }

        return new GuardedHttpClientResult(_pinnedHttpClientFactory(pinnedAddress, false), null);
    }
}

/// Result of <see cref="GuardedHttpClientFactory.CreateAsync(string?, bool, bool, string)"/>:
/// exactly one of <see cref="Client"/> / <see cref="BlockReason"/> is non-null.
public readonly struct GuardedHttpClientResult
{
    internal GuardedHttpClientResult(HttpClient? client, string? blockReason)
    {
        Client = client;
        BlockReason = blockReason;
    }

    public HttpClient? Client { get; }

    public string? BlockReason { get; }

    public bool Blocked => BlockReason != null;
}
