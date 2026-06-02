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

    public string DefaultRoleName { get; set; } = string.Empty;
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

    public bool Enabled { get; set; } = true;

    public string ButtonColor { get; set; } = "#4285F4";

    public string ButtonIcon { get; set; } = string.Empty;

    public string AdditionalParameters { get; set; } = string.Empty;

    /// <summary>
    /// When true, a failure to validate the access token's signature is treated as a fatal
    /// authentication error. Set to false only for IdPs that issue unsigned or opaque access tokens.
    /// </summary>
    public bool StrictAccessTokenValidation { get; set; } = true;

    /// <summary>
    /// When true, endpoint URLs in the OIDC discovery document must share the same base address
    /// as the issuer. Set to false for IdPs like Authentik whose endpoints are on different paths
    /// than the issuer (e.g. /application/o/authorize/ vs /o/jellyfin).
    /// </summary>
    public bool ValidateDiscoveryEndpoints { get; set; } = false;
}

public class RoleMapping
{
    public string RoleName { get; set; } = string.Empty;

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
