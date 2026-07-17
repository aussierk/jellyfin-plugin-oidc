using System;
using System.Net;
using System.Net.Sockets;
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
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri))
        {
            // Malformed URL — let the discovery fetch fail naturally and report its own generic error.
            return null;
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
                return null;
            }
        }

        foreach (var address in addresses)
        {
            if (!allowLoopback && IsLoopback(address))
            {
                return $"Authority '{uri.Host}' resolves to a loopback address ({address}), which is blocked by " +
                       "default. Enable \"Allow loopback Authority\" for this provider to override.";
            }

            if (!allowLinkLocal && IsLinkLocal(address))
            {
                return $"Authority '{uri.Host}' resolves to a link-local address ({address}), which is blocked by " +
                       "default. Enable \"Allow link-local Authority\" for this provider to override.";
            }

            if (blockPrivateNetworks && IsPrivateNetworkOrUla(address))
            {
                return $"Authority '{uri.Host}' resolves to a private-network address ({address}), which is " +
                       "blocked by the \"Block RFC1918/ULA Authorities\" plugin setting.";
            }
        }

        return null;
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
