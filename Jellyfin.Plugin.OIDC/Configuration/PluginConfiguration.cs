using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OIDC.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public List<OidcProviderConfig> Providers { get; set; } = new();

    public List<RoleMapping> RoleMappings { get; set; } = new();

    public string DefaultProvider { get; set; } = string.Empty;

    public bool AutoCreateUsers { get; set; } = true;

    /// <summary>
    /// When true, existing local Jellyfin users who log in via OIDC will have their
    /// AuthenticationProviderId updated to OidcAuthProvider, migrating them to SSO.
    /// Defaults to false — opt-in only.
    /// </summary>
    public bool MigrateLocalUsers { get; set; } = false;

    /// <summary>
    /// When true, the user's Jellyfin display name (username) is updated to match
    /// the display name claim from the OIDC token on each login. Note: in Jellyfin,
    /// the username IS the display name — enabling this renames the account.
    /// Defaults to false — opt-in only.
    /// </summary>
    public bool SyncDisplayName { get; set; } = false;

    /// <summary>
    /// When true, an Authority resolving to an RFC1918 private range (10/8, 172.16/12, 192.168/16)
    /// or IPv6 ULA (fc00::/7) is blocked for every provider, in addition to the always-checked
    /// loopback/link-local guard. Defaults to false — opt-in only, for admins who want stricter
    /// network isolation. Self-hosted IdPs commonly live on RFC1918 addresses, so this is off by default.
    /// </summary>
    public bool BlockPrivateNetworkAuthorities { get; set; } = false;

    public string DefaultRoleName { get; set; } = string.Empty;

    /// <summary>
    /// Maps Jellyfin usernames to the OIDC provider that owns each account.
    /// Used to detect cross-provider username collisions.
    /// List of UserProviderEntry is used instead of Dictionary to remain XML-serializable.
    /// </summary>
    public List<UserProviderEntry> UserProviderMap { get; set; } = new();
}

/// <summary>XML-serializable username → provider ID mapping entry.</summary>
public class UserProviderEntry
{
    public string Username { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
}

public class OidcProviderConfig
{
    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid profile email";

    public string RoleClaim { get; set; } = "realm_access.roles";

    public string UsernameClaim { get; set; } = "preferred_username";

    public string DisplayNameClaim { get; set; } = "name";

    public string PictureClaim { get; set; } = "picture";

    /// <summary>
    /// When true, the user's Jellyfin avatar is synced from the picture claim on each login,
    /// overwriting any existing avatar. Defaults to true, matching upstream.
    /// </summary>
    public bool SyncProfileImage { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public string ButtonColor { get; set; } = "#4285F4";

    public string ButtonIcon { get; set; } = string.Empty;

    public string AdditionalParameters { get; set; } = string.Empty;

    public string ServerBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// When true, a failure to validate the access token's signature is treated as a fatal
    /// authentication error. Set to false only for IdPs that issue unsigned or opaque access tokens.
    /// </summary>
    public bool StrictAccessTokenValidation { get; set; } = true;

    /// <summary>
    /// When true, allows this provider's Authority to resolve to a loopback address (127.0.0.0/8, ::1).
    /// Defaults to false — opt-in only, for the rare case of a legitimately loopback-hosted IdP.
    /// </summary>
    public bool AllowLoopbackAuthority { get; set; } = false;

    /// <summary>
    /// When true, allows this provider's Authority to resolve to a link-local address
    /// (169.254.0.0/16, fe80::/10). Defaults to false — opt-in only.
    /// </summary>
    public bool AllowLinkLocalAuthority { get; set; } = false;

    /// <summary>
    /// The authority URL that was used when endpoints were last pinned.
    /// If this differs from Authority, pins are treated as stale and re-pinned on next auth.
    /// </summary>
    public string PinnedAuthority { get; set; } = string.Empty;

    /// <summary>
    /// Pinned OIDC discovery endpoints — set on first successful auth (TOFU) or via Test Connection.
    /// If these change unexpectedly in a later discovery document, login is blocked and pins are cleared.
    /// </summary>
    public string PinnedIssuer { get; set; } = string.Empty;
    public string PinnedTokenEndpoint { get; set; } = string.Empty;
    public string PinnedJwksUri { get; set; } = string.Empty;
}

public class RoleMapping
{
    public string RoleName { get; set; } = string.Empty;

    /// <summary>
    /// When set, this mapping only applies to users authenticated via the specified provider.
    /// Leave empty to apply to all providers (global mapping).
    /// </summary>
    public string ProviderFilter { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool EnableAllLibraries { get; set; }

    public List<string> LibraryIds { get; set; } = new();

    public List<string> LibraryNames { get; set; } = new();

    public bool EnableLiveTv { get; set; }

    public bool EnableLiveTvManagement { get; set; }

    public bool EnableMediaPlayback { get; set; } = true;

    public bool EnableRemoteAccess { get; set; } = true;

    public bool EnableTranscoding { get; set; } = true;

    public bool EnableContentDeletion { get; set; }

    public bool EnableCollectionManagement { get; set; }

    public bool EnableSubtitleManagement { get; set; }

    public int? MaxParentalRating { get; set; }

    public int Priority { get; set; }
}
