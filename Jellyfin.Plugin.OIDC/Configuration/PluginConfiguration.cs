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
