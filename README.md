# SSO-OIDC-Authentication - Jellyfin Plugin

A security-hardened Jellyfin plugin providing **OpenID Connect authentication** with
**role-based library access control**.

Authenticate users via any OIDC-compatible identity provider (Authentik, Keycloak, Azure AD,
Okta, etc.) and automatically assign Jellyfin permissions and library access based on IdP
group/role claims.


## Features

- **OIDC Authentication** with PKCE (Authorization Code flow)
- **Multi-provider support** - configure multiple IdPs simultaneously with branded login buttons
- **Provider isolation** - each Jellyfin account is bound to the provider that created it; cross-provider impersonation is blocked
- **Role-based access control** - map IdP roles/groups to Jellyfin permissions and specific libraries, with an opt-out if you'd rather manage policy by hand ([details](CONFIGURATION.md#turning-off-policy-management))
- **Fail-closed by default** - deny login when no IdP role or fallback matches, preventing stale permissions from surviving role removal
- **Back-channel logout** - a signed `logout_token` from the IdP revokes the matching Jellyfin session(s)
- **Subject-keyed identity** - accounts are bound to the OIDC `sub`, so a rename (at the IdP or in Jellyfin) can't orphan the account, and an IdP switch can reclaim the account by verified email ([details](MIGRATION.md#reclaim-accounts-across-an-idp-switch-linkexistingusersbyemail))
- **Endpoint pinning** - TOFU pins discovery endpoints on first use, or pre-set them from your IdP docs to skip the trust window
- **Client secret out of the config file** - a Docker/Kubernetes secret file or an environment variable reference, instead of plaintext ([details](CONFIGURATION.md#keeping-the-client-secret-out-of-the-config))
- **Auto-provisioning**, **profile image sync**, **opt-in local-account migration**, and **native/mobile login** via [Quick Connect](QUICK-CONNECT.md)
- **Admin UI** - full configuration from the Jellyfin dashboard, reachable directly from the left nav under **Plugins**
- **Login button injection** - one click writes the button into Jellyfin's Branding settings; no manual HTML editing

See [Configuration Reference](CONFIGURATION.md) for the full set of options and
[SECURITY.md](SECURITY.md) for the hardening details and known limitations.

## Installation

### Add repository to Jellyfin

```
https://raw.githubusercontent.com/aussierk/jellyfin-plugin-oidc/main/manifest.json
```

1. Go to **Admin Dashboard → Plugins → Repositories**
2. Click **Add repository** and paste the URL above (Repository Name: `SSO-OIDC-Authentication`)
3. Go to **Catalog → Authentication**
4. Install **SSO-OIDC-Authentication**
5. Restart Jellyfin

### Release channels

| Channel | Repository URL | Contents |
|---|---|---|
| Stable | `https://raw.githubusercontent.com/aussierk/jellyfin-plugin-oidc/main/manifest.json` | Full releases only (e.g. `1.0.6.0`) |

Add the Testing URL as a second repository (same steps as above) if you want early access to RC builds. Stick with Stable for normal use.

### Manual installation

1. Download `oidc-rbac.zip` from the [latest release](https://github.com/aussierk/jellyfin-plugin-oidc/releases/latest)
2. On your server, create a folder named `SSO-OIDC-Authentication_2.1.0.0` inside your Jellyfin plugins directory (e.g. `/config/plugins/`)
3. Extract the contents of the zip into that folder
4. Restart Jellyfin

> **Upgrading from a previous version?** Stop Jellyfin, delete the old plugin folder entirely, create a fresh folder with the new version number, extract the zip, then start Jellyfin. Jellyfin must be fully restarted (not just the browser) for the new DLL to load.

## Quick Start

1. **Add a provider** - Admin Dashboard → Plugins → SSO-OIDC-Authentication → Providers tab.
   Set Issuer URL, Client ID, Client Secret, and the Role Claim Path your IdP uses (e.g.
   `groups`, or `realm_access.roles` for Keycloak), then click **Test Connection**.
2. **Create role mappings** - Role Mappings tab. Map each IdP group/role to Jellyfin
   permissions and libraries (admin, standard user, kids, etc.).
3. **Save** - with a provider enabled, the login button is added to Jellyfin's login page
   automatically.

That's the minimum to get signed in. For the full field reference - profile image sync,
keeping the client secret out of the config, multi-provider role isolation, the policy
management opt-out, and more - see **[Configuration Reference](CONFIGURATION.md)**.

Picking an IdP? Jump straight to its guide:

| Provider | Guide | Role Claim |
|----------|-------|------------|
| Authentik | [examples/authentik/SETUP.md](examples/authentik/SETUP.md) | `groups` |
| Azure AD / Entra ID | [examples/azure-ad/SETUP.md](examples/azure-ad/SETUP.md) | `roles` or `groups` |
| Google | [examples/google/SETUP.md](examples/google/SETUP.md) | `email` (groups require Workspace) |
| Okta | [examples/okta/SETUP.md](examples/okta/SETUP.md) | `groups` |
| Pocket ID | [examples/pocket-id/SETUP.md](examples/pocket-id/SETUP.md) | `groups` |
| Authelia | [examples/authelia/SETUP.md](examples/authelia/SETUP.md) | `groups` |
| Tinyauth | [examples/tinyauth/SETUP.md](examples/tinyauth/SETUP.md) | `groups` |
| Keycloak | [Quick reference](CONFIGURATION.md#keycloak-quick-reference) | `realm_access.roles` |

## Already have Jellyfin users?

Moving existing local accounts to SSO, or switching from one IdP to another? See
**[MIGRATION.md](MIGRATION.md)** - username-match is automatic, watch history and favorites
are preserved, and an opt-in verified-email link lets users reclaim their account across an
IdP switch.

## Documentation

| Doc | Covers |
|---|---|
| [CONFIGURATION.md](CONFIGURATION.md) | Full provider/role-mapping reference, client secret files/env vars, RBAC merge rules, reverse proxies |
| [SECURITY.md](SECURITY.md) | Hardening vs. upstream, avoiding admin lockout, known limitations |
| [MIGRATION.md](MIGRATION.md) | Moving local users to SSO, switching identity providers |
| [QUICK-CONNECT.md](QUICK-CONNECT.md) | Signing in native/mobile apps that can't use the web login button |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Login flow, API endpoints, project layout |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Building from source, running tests |
| [RELEASING.md](RELEASING.md) | CI release pipeline |

## License

GPLv3 (required by linking against Jellyfin's GPLv3 libraries)
