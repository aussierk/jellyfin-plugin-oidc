using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OIDC.Services;

/// SSRF guard for outbound IdP URLs. Blocks non-http(s) schemes, unresolvable hosts, and the
/// unspecified/loopback/link-local ranges by default (RFC1918/ULA/CGNAT when opted in), after
/// normalising IPv4-mapped IPv6 so a mapped literal can't bypass the v4 checks.
public static class AuthorityGuard
{
    /// 
    /// Returns null when the Authority is allowed (with the resolved address to pin the HTTP
    /// connection to), or a rejection reason otherwise. Pinning the address closes the
    /// DNS-rebinding TOCTOU window between this check and the connection that follows it.
    /// 
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

    ///
    /// Builds an <see cref="HttpClient"/> pinned to <paramref name="pinnedAddress"/> instead of
    /// re-resolving DNS. TLS SNI/hostname validation is unaffected - only the socket destination
    /// changes. Auto-redirect is OFF by default: a redirect target is never re-validated by the
    /// guard, so following one would defeat the pin. A 15s timeout replaces HttpClient's 100s
    /// default so a slow or hostile endpoint can't tie up the request.
    ///
    public static HttpClient CreatePinnedHttpClient(IPAddress pinnedAddress, bool allowAutoRedirect = false)
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

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static async Task<(string? BlockReason, IPAddress[] Addresses)> ResolveAndCheckAsync(
        string authority,
        bool allowLoopback,
        bool allowLinkLocal,
        bool blockPrivateNetworks)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri))
        {
            return ("URL is not a valid absolute URI.", Array.Empty<IPAddress>());
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return ($"URL scheme '{uri.Scheme}' is not supported - use http or https.", Array.Empty<IPAddress>());
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
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                // Fail closed: an unresolvable name must not fall through to an unpinned client.
                return ($"Host '{uri.Host}' could not be resolved.", Array.Empty<IPAddress>());
            }

            if (addresses.Length == 0)
            {
                return ($"Host '{uri.Host}' did not resolve to any address.", Array.Empty<IPAddress>());
            }
        }

        foreach (var address in addresses)
        {
            if (IsUnspecified(address))
            {
                return ($"Authority '{uri.Host}' resolves to the unspecified address ({address}), which is blocked.", addresses);
            }

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

    // ::ffff:a.b.c.d - fold to the plain IPv4 form so a mapped literal can't slip past the v4 checks.
    private static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(Normalize(address));

    /// The IPv4/IPv6 "any" address (0.0.0.0 / ::), which routes to localhost on most stacks.
    public static bool IsUnspecified(IPAddress address)
    {
        var a = Normalize(address);
        return a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any);
    }

    public static bool IsLinkLocal(IPAddress address)
    {
        var a = Normalize(address);
        if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return a.IsIPv6LinkLocal;
        }

        var bytes = a.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    /// RFC1918 (10/8, 172.16/12, 192.168/16), CGNAT (100.64/10, RFC 6598), or IPv6 ULA (fc00::/7).
    public static bool IsPrivateNetworkOrUla(IPAddress address)
    {
        var a = Normalize(address);
        if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6Bytes = a.GetAddressBytes();
            return (v6Bytes[0] & 0xFE) == 0xFC;
        }

        var bytes = a.GetAddressBytes();
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

        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        // Carrier-grade NAT - not publicly routable.
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }
}
