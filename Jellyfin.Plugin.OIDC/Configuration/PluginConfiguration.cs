using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OIDC.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public List<OidcProviderConfig> Providers { get; set; } = new();

    public List<RoleMapping> RoleMappings { get; set; } = new();

    /// <summary>The provider with this id, matched case-insensitively, or null.</summary>
    public OidcProviderConfig? FindProvider(string? providerId)
        => Providers.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Enabled providers, in configuration order.</summary>
    public IEnumerable<OidcProviderConfig> EnabledProviders => Providers.Where(p => p.Enabled);

    public bool AutoCreateUsers { get; set; } = true;

    /// Opt-in: migrates a local user's AuthenticationProviderId to OIDC on their first SSO login.
    public bool MigrateLocalUsers { get; set; } = false;

    /// <summary>
    /// Rejects login unless the token asserts <c>email_verified</c>. Also gates
    /// <see cref="AllowedEmails"/>/<see cref="AllowedEmailDomains"/>, which stay inert while
    /// this is off - an unverified email can't be trusted as an admission signal.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; } = false;

    /// <summary>
    /// Links a login with no matching account to an existing one seen before with the same
    /// verified email, across providers - lets a user keep their account when an admin
    /// switches IdPs. Requires <see cref="RequireVerifiedEmail"/> on both sides of the match.
    /// </summary>
    public bool LinkExistingUsersByEmail { get; set; } = false;

    /// Groups/roles allowed to sign in at all (separate from RBAC). Empty ⇒ everyone who authenticates.
    public List<string> AllowedGroups { get; set; } = new();

    /// Email domains allowed to sign in. Only enforced with <see cref="RequireVerifiedEmail"/>. Empty ⇒ no restriction.
    public List<string> AllowedEmailDomains { get; set; } = new();

    /// Exact emails allowed to sign in. Only enforced with <see cref="RequireVerifiedEmail"/>. Empty ⇒ no restriction.
    public List<string> AllowedEmails { get; set; } = new();

    /// Blocks an Authority resolving to an RFC1918/ULA private range, on top of the always-on loopback/link-local guard.
    public bool BlockPrivateNetworkAuthorities { get; set; } = false;

    public string DefaultRoleName { get; set; } = string.Empty;

    /// <summary>
    /// When false, the plugin never touches the user's Jellyfin policy - no RBAC, no
    /// fail-closed denial on an unmapped role. Admission (<see cref="AllowedGroups"/>,
    /// <see cref="RequireVerifiedEmail"/>) still applies either way.
    /// </summary>
    public bool ManageUserPolicy { get; set; } = true;

    /// When false (and <see cref="ManageUserPolicy"/> is on), RBAC manages permissions/admin status but leaves library access untouched.
    public bool EnableLibraryAccessManagement { get; set; } = true;

    /// Keeps a marker-fenced SSO login-button block synced into Branding (web client only) on every config save.
    public bool ManageLoginButtonBranding { get; set; } = true;

    /// Hides the web login form/Forgot Password (Quick Connect stays) in favor of <see cref="LoginTitle"/>. Requires <see cref="ManageLoginButtonBranding"/>.
    public bool HideManualLogin { get; set; } = false;

    /// Heading shown above the SSO button(s) when <see cref="HideManualLogin"/> is set.
    public string LoginTitle { get; set; } = "Please sign in";

    /// Optional smaller line under <see cref="LoginTitle"/>.
    public string LoginSubtitle { get; set; } = string.Empty;

    /// <summary>
    /// Legacy home of the identity → Jellyfin account map. The live map now lives in its own file
    /// (<see cref="Services.UserProviderMapStore"/>); this list is only read once on upgrade to
    /// migrate old rows out, then left empty. Kept for that migration and for config back-compat.
    /// </summary>
    public List<UserProviderEntry> UserProviderMap { get; set; } = new();
}

/// XML-serializable OIDC identity → Jellyfin account mapping entry.
public class UserProviderEntry
{
    public string Username { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    /// The OIDC <c>sub</c> claim. Empty on legacy rows; back-filled on next login.
    public string Subject { get; set; } = string.Empty;

    /// Jellyfin user id. Empty on legacy rows; back-filled on next login.
    public string UserId { get; set; } = string.Empty;

    /// Last-seen email for <see cref="PluginConfiguration.LinkExistingUsersByEmail"/>. Only set when <c>email_verified</c> was true.
    public string Email { get; set; } = string.Empty;

    /// Whether <see cref="Email"/> was verified when stored - link matches require this true.
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Sid from the most recent login, so a sid-only back-channel logout after a server
    /// restart (in-memory session table gone) can still resolve the user.
    /// </summary>
    public string LogoutSid { get; set; } = string.Empty;
}

public class OidcProviderConfig
{
    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// Path to a file holding the client secret (e.g. a mounted Docker/K8s secret); overrides <see cref="ClientSecret"/> when set. See <see cref="Services.ClientSecretResolver"/>.
    public string ClientSecretFile { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid profile email";

    // Default matches the config page's fallback; Keycloak users set "realm_access.roles".
    public string RoleClaim { get; set; } = "groups";

    public string UsernameClaim { get; set; } = "preferred_username";

    public string DisplayNameClaim { get; set; } = "name";

    public string EmailClaim { get; set; } = "email";

    /// Claim asserting the email is verified. Empty ⇒ <c>email_verified</c>. Some IdPs name it differently or omit it (e.g. Entra).
    public string EmailVerifiedClaim { get; set; } = "email_verified";

    /// <see cref="EmailClaim"/> with the spec default filled in when an admin has blanked the field.
    [System.Xml.Serialization.XmlIgnore]
    public string EmailClaimOrDefault => string.IsNullOrWhiteSpace(EmailClaim) ? "email" : EmailClaim;

    /// <see cref="EmailVerifiedClaim"/> with the spec default filled in when an admin has blanked the field.
    [System.Xml.Serialization.XmlIgnore]
    public string EmailVerifiedClaimOrDefault =>
        string.IsNullOrWhiteSpace(EmailVerifiedClaim) ? "email_verified" : EmailVerifiedClaim;

    /// Renames the Jellyfin account to the display-name claim on every login (Jellyfin has no separate display name).
    public bool SyncDisplayName { get; set; } = false;

    public string PictureClaim { get; set; } = "picture";

    public bool SyncProfileImage { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public string ButtonColor { get; set; } = "#4285F4";

    public string ButtonIcon { get; set; } = string.Empty;

    public string AdditionalParameters { get; set; } = string.Empty;

    public string ServerBaseUrl { get; set; } = string.Empty;

    /// Set false only for IdPs issuing unsigned/opaque access tokens.
    public bool StrictAccessTokenValidation { get; set; } = true;

    public bool AllowLoopbackAuthority { get; set; } = false;

    public bool AllowLinkLocalAuthority { get; set; } = false;

    /// <summary>
    /// Opt in for this provider to act as a source for <see cref="PluginConfiguration.LinkExistingUsersByEmail"/>:
    /// a login here may be linked to an existing account by a matching verified email. Leave off for
    /// any IdP where a user can set or self-assert their own email address - a verified email is only
    /// as trustworthy as the IdP asserting it.
    /// </summary>
    public bool TrustedForEmailLinking { get; set; } = false;

    /// Authority URL as of the last pin. A mismatch here means pins are stale and get re-pinned on next auth.
    public string PinnedAuthority { get; set; } = string.Empty;

    /// Discovery endpoints pinned TOFU (or via Test Connection). An unexpected change blocks login.
    public string PinnedIssuer { get; set; } = string.Empty;
    public string PinnedTokenEndpoint { get; set; } = string.Empty;
    public string PinnedJwksUri { get; set; } = string.Empty;
    public string PinnedUserInfoEndpoint { get; set; } = string.Empty;
    public string PinnedAuthorizeEndpoint { get; set; } = string.Empty;
}

public class RoleMapping
{
    public string RoleName { get; set; } = string.Empty;

    /// Restricts this mapping to one provider. Empty applies it globally.
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

    /// Parental-rating name (e.g. "PG-13"), resolved to a score via <c>ILocalizationManager</c>. Empty ⇒ unrestricted.
    public string MaxParentalRatingName { get; set; } = string.Empty;

    /// Legacy numeric score, still honoured when <see cref="MaxParentalRatingName"/> is empty.
    public int? MaxParentalRating { get; set; }
}
