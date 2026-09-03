using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OIDC.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public List<OidcProviderConfig> Providers { get; set; } = new();

    public List<RoleMapping> RoleMappings { get; set; } = new();

    public bool AutoCreateUsers { get; set; } = true;

    /// <summary>
    /// When true, existing local Jellyfin users who log in via OIDC will have their
    /// AuthenticationProviderId updated to OidcAuthProvider, migrating them to SSO.
    /// Defaults to false — opt-in only.
    /// </summary>
    public bool MigrateLocalUsers { get; set; } = false;

    /// <summary>
    /// When set, a login is rejected unless the token asserts <c>email_verified</c>.
    /// Off by default. Evaluated at the admission gate alongside <see cref="AllowedGroups"/> etc.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; } = false;

    /// <summary>
    /// When set and the token's email is verified, a login whose subject/username doesn't
    /// resolve is matched to an existing account previously seen with the same email (instead
    /// of creating a duplicate). Off by default.
    /// </summary>
    public bool LinkExistingUsersByEmail { get; set; } = false;

    /// <summary>Groups/roles allowed to sign in at all (separate from RBAC). Empty ⇒ everyone who authenticates.</summary>
    public List<string> AllowedGroups { get; set; } = new();

    /// <summary>Email domains allowed to sign in (e.g. "example.com"). Empty ⇒ no domain restriction.</summary>
    public List<string> AllowedEmailDomains { get; set; } = new();

    /// <summary>Exact emails allowed to sign in. Empty ⇒ no per-address restriction.</summary>
    public List<string> AllowedEmails { get; set; } = new();

    /// <summary>
    /// When true, an Authority resolving to an RFC1918 private range (10/8, 172.16/12, 192.168/16)
    /// or IPv6 ULA (fc00::/7) is blocked for every provider, in addition to the always-checked
    /// loopback/link-local guard. Defaults to false — opt-in only, for admins who want stricter
    /// network isolation. Self-hosted IdPs commonly live on RFC1918 addresses, so this is off by default.
    /// </summary>
    public bool BlockPrivateNetworkAuthorities { get; set; } = false;

    public string DefaultRoleName { get; set; } = string.Empty;

    /// <summary>
    /// When true, saving the plugin config also keeps a marker-fenced SSO login-button block
    /// in sync inside Jellyfin's Branding settings (Login Disclaimer + Custom CSS), matching
    /// the enabled providers. Web client only — native/TV apps use Quick Connect. The block
    /// is written and removed by the config page; this flag only records the admin's choice.
    /// </summary>
    public bool ManageLoginButtonBranding { get; set; } = true;

    /// <summary>
    /// When true, the managed Branding block also hides the web login's username/password
    /// form and the Forgot Password button (Quick Connect stays) and shows <see cref="LoginTitle"/>
    /// as a heading above the SSO button(s). Web client only. Requires
    /// <see cref="ManageLoginButtonBranding"/> (or a manual paste of the snippet).
    /// </summary>
    public bool HideManualLogin { get; set; } = false;

    /// <summary>Heading shown above the SSO button(s) when <see cref="HideManualLogin"/> is set.</summary>
    public string LoginTitle { get; set; } = "Please sign in";

    /// <summary>
    /// Optional smaller line under <see cref="LoginTitle"/> (e.g. "On a TV, use Quick Connect"),
    /// shown only when <see cref="HideManualLogin"/> is set and this is non-empty.
    /// </summary>
    public string LoginSubtitle { get; set; } = string.Empty;

    /// <summary>
    /// Maps each OIDC identity to the Jellyfin account that owns it. Keyed on the stable
    /// <c>sub</c> claim (<see cref="UserProviderEntry.Subject"/>) once known, so a Jellyfin
    /// rename (or a display-name sync) can't orphan the account; <see cref="UserProviderEntry.Username"/>
    /// is kept for legacy rows and cross-provider collision detection.
    /// List (not Dictionary) to remain XML-serializable.
    /// </summary>
    public List<UserProviderEntry> UserProviderMap { get; set; } = new();
}

/// <summary>XML-serializable OIDC identity → Jellyfin account mapping entry.</summary>
public class UserProviderEntry
{
    public string Username { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    /// <summary>The OIDC <c>sub</c> claim. Empty on rows written before sub-keying; back-filled on next login.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The Jellyfin user id (GUID as string). Empty on legacy rows; back-filled on next login.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Last-seen verified email, for <see cref="PluginConfiguration.LinkExistingUsersByEmail"/>. May be empty.</summary>
    public string Email { get; set; } = string.Empty;
}

public class OidcProviderConfig
{
    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid profile email";

    // "groups" is the most portable default (Authentik, Google, Okta, Auth0, generic OIDC).
    // Keycloak users set "realm_access.roles". Kept in sync with the config page's fallback.
    public string RoleClaim { get; set; } = "groups";

    public string UsernameClaim { get; set; } = "preferred_username";

    public string DisplayNameClaim { get; set; } = "name";

    public string EmailClaim { get; set; } = "email";

    /// <summary>
    /// When true, the Jellyfin account is renamed to match this provider's display-name claim
    /// on every login. Jellyfin has no separate display name — the username IS the display
    /// name — so this renames the account. Off by default. Safe now that identity is keyed on
    /// the OIDC <c>sub</c> (<see cref="UserProviderEntry.Subject"/>).
    /// </summary>
    public bool SyncDisplayName { get; set; } = false;

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
}
