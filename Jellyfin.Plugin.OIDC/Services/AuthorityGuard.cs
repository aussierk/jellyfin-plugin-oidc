using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Rejects OIDC discovery fetches targeting loopback or link-local addresses by default
/// (each independently opt-out-able per provider), and optionally RFC1918/ULA private-network
/// addresses too, when the admin has turned on that stricter global setting.
/// </summary>
public static class AuthorityGuard
{
    /// <summary>
    /// Returns null when the Authority is allowed, or a human-readable rejection reason otherwise.
    /// </summary>
    public static async Task<string?> ValidateAsync(
        string authority,
        bool allowLoopback,
        bool allowLinkLocal,
        bool blockPrivateNetworks)
    {
        var (blockReason, _) = await ResolveAndCheckAsync(authority, allowLoopback, allowLinkLocal, blockPrivateNetworks)
            .ConfigureAwait(false);
        return blockReason;
    }

    /// <summary>
    /// Same guard as <see cref="ValidateAsync"/>, but also returns the exact address that was
    /// resolved and checked, so a caller can pin the subsequent HTTP connection to it instead of
    /// re-resolving DNS — closing the TOCTOU window a DNS-rebinding attacker could otherwise use
    /// to pass the guard with one address and connect to a different (internal) one.
    /// </summary>
    public static async Task<(string? BlockReason, IPAddress? PinnedAddress)> ValidateAndResolveAsync(
        string authority,
        bool allowLoopback,
        bool allowLinkLocal,
        bool blockPrivateNetworks)
    {
        var (blockReason, addresses) = await ResolveAndCheckAsync(authority, allowLoopback, allowLinkLocal, blockPrivateNetworks)
            .ConfigureAwait(false);
        return (blockReason, addresses.Length > 0 ? addresses[0] : null);
    }

    /// <summary>
    /// Builds a one-off <see cref="HttpClient"/> whose connections are pinned to
    /// <paramref name="pinnedAddress"/> instead of re-resolving the request's hostname via DNS.
    /// TLS SNI/certificate hostname validation is unaffected — <see cref="SocketsHttpHandler"/>
    /// negotiates TLS against the original request hostname regardless of the connect callback;
    /// only the transport-level socket destination changes.
    /// </summary>
    /// <param name="pinnedAddress">The address to connect to, in place of a fresh DNS lookup.</param>
    /// <param name="allowAutoRedirect">
    /// A redirect target is a different, unvalidated destination — following it automatically
    /// would silently defeat the pin. Discovery-document fetches keep the default (some real IdPs
    /// legitimately redirect during discovery); callers with a stricter trust boundary, like the
    /// profile-picture fetch, should pass <see langword="false"/>.
    /// </param>
    public static HttpClient CreatePinnedHttpClient(IPAddress pinnedAddress, bool allowAutoRedirect = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var endpoint = new IPEndPoint(pinnedAddress, context.DnsEndPoint.Port);
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        return new HttpClient(handler);
    }

    private static async Task<(string? BlockReason, IPAddress[] Addresses)> ResolveAndCheckAsync(
        string authority,
        bool allowLoopback,
        bool allowLinkLocal,
        bool blockPrivateNetworks)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri))
        {
            // Malformed URL — let the discovery fetch fail naturally and report its own generic error.
            return (null, Array.Empty<IPAddress>());
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host).ConfigureAwait(false);
            }
            catch
            {
                // DNS resolution failure isn't a guard concern — the discovery fetch will
                // attempt (and normally fail) its own resolution and report it generically.
                return (null, Array.Empty<IPAddress>());
            }
        }

        foreach (var address in addresses)
        {
            if (!allowLoopback && IsLoopback(address))
            {
                return ($"Authority '{uri.Host}' resolves to a loopback address ({address}), which is blocked by " +
                       "default. Enable \"Allow loopback Authority\" for this provider to override.", addresses);
            }

            if (!allowLinkLocal && IsLinkLocal(address))
            {
                return ($"Authority '{uri.Host}' resolves to a link-local address ({address}), which is blocked by " +
                       "default. Enable \"Allow link-local Authority\" for this provider to override.", addresses);
            }

            if (blockPrivateNetworks && IsPrivateNetworkOrUla(address))
            {
                return ($"Authority '{uri.Host}' resolves to a private-network address ({address}), which is " +
                       "blocked by the \"Block RFC1918/ULA Authorities\" plugin setting.", addresses);
            }
        }

        return (null, addresses);
    }

    public static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(address);

    public static bool IsLinkLocal(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    /// <summary>RFC1918 (10/8, 172.16/12, 192.168/16) or IPv6 ULA (fc00::/7).</summary>
    public static bool IsPrivateNetworkOrUla(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6Bytes = address.GetAddressBytes();
            return (v6Bytes[0] & 0xFE) == 0xFC;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        if (bytes[0] == 10)
        {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        return bytes[0] == 192 && bytes[1] == 168;
    }
}
