# SSO-OIDC RBAC — Jellyfin Plugin

A Jellyfin plugin providing **OpenID Connect authentication** with **role-based library access control**.

Authenticate users via any OIDC-compatible identity provider (Authentik, Keycloak, Azure AD, Okta, etc.) and automatically assign Jellyfin permissions and library access based on IdP group/role claims.

## Features

- **OIDC Authentication** with PKCE (Authorization Code flow)
- **Multi-provider support** — configure multiple IdPs simultaneously with branded login buttons
- **Role-based access control** — map IdP roles/groups to Jellyfin permissions and specific libraries
- **Auto-provisioning** — create Jellyfin users on first SSO login
- **Flexible claim parsing** — extract roles from nested JWT claims (e.g. `realm_access.roles`, `groups`)
- **Merge semantics** — users with multiple roles get the union of all permissions (most permissive wins)
- **Default role fallback** — assign a baseline role to users with no matching IdP roles
- **Admin UI** — full configuration from the Jellyfin dashboard (Providers, Role Mappings, General settings)
- **Login button injection** — paste one HTML snippet into Jellyfin's Login Disclaimer; buttons appear automatically
- **Opt-in local user migration** — switch existing password accounts to SSO on first login
- **Opt-in display name sync** — keep Jellyfin account names in sync with the IdP
- **Disabled user enforcement** — disabled Jellyfin accounts are blocked from SSO login

## Installation

### Add repository to Jellyfin

```
https://raw.githubusercontent.com/aussierk/jellyfin-plugin-oidc/main/manifest.json
```

1. Go to **Admin Dashboard → Plugins → Repositories**
2. Click **Add repository** and paste the URL above (Repository Name: `SSO-OIDC RBAC`)
3. Go to **Catalog → Authentication**
4. Install **SSO-OIDC RBAC**
5. Restart Jellyfin

### Manual installation

1. Download `oidc-rbac.zip` from the [latest release](https://github.com/aussierk/jellyfin-plugin-oidc/releases/latest)
2. On your server, create a folder named `SSO-OIDC RBAC_1.0.5.0` inside your Jellyfin plugins directory (e.g. `/config/plugins/`)
3. Extract the contents of the zip into that folder
4. Restart Jellyfin

## Quick Start

### 1. Configure a Provider

Go to **Admin Dashboard → Plugins → SSO-OIDC RBAC → Providers tab**

| Field              | Example (Authentik)                                        |
|--------------------|------------------------------------------------------------|
| Provider ID        | `authentik`                                                |
| Display Name       | `Authentik`                                                |
| Authority URL      | `https://auth.example.com/application/o/jellyfin/`        |
| Client ID          | *(from your IdP)*                                          |
| Client Secret      | *(from your IdP)*                                          |
| Scopes             | `openid profile email`                                     |
| Role Claim Path    | `groups`                                                   |
| Username Claim     | `preferred_username`                                       |
| Display Name Claim | `name`                                                     |
| Server Base URL    | *(optional, e.g. `https://jellyfin.example.com`)*          |

> **Server Base URL** is only needed if Jellyfin can't resolve its public URL on its own (e.g. behind a reverse proxy whose `X-Forwarded-*` headers aren't trusted). See [Reverse proxy / redirect_uri](#reverse-proxy--redirect_uri).

### 2. Create Role Mappings

Go to **Role Mappings tab** and create mappings:

**Example — Admin role:**
- Role Name: `jellyfin-admins`
- Administrator: checked
- All Libraries: checked

**Example — Standard user:**
- Role Name: `jellyfin-users`
- Libraries: select specific libraries
- Playback, Remote Access, Transcoding: checked

**Example — Kids:**
- Role Name: `jellyfin-kids`
- Libraries: Kids only
- Max Parental Rating: 7

### 3. General Settings

Go to **General tab** and configure:

| Setting                           | Default | Description |
|-----------------------------------|---------|-------------|
| Auto-create users                 | On      | Create a Jellyfin account on first SSO login |
| Default Role                      | —       | Fallback role when no IdP role matches a mapping |
| Migrate local users to SSO        | Off     | Switch existing password accounts to SSO auth on first SSO login |
| Sync display name from OIDC token | Off     | Rename the Jellyfin account to match the IdP display name on each login |

### 4. Add the Login Button

Go to **Admin Dashboard → General → Branding → Login disclaimer** and paste the HTML from:

```
GET /sso/OIDC/BrandingSnippet
```

Or use the Jellyfin API to retrieve it:
```bash
curl https://jellyfin.example.com/sso/OIDC/BrandingSnippet
```

Copy the `Html` field from the response and paste it into the Login Disclaimer field. The snippet contains styled `<a>` links for each enabled provider — no JavaScript required.

## Migrating Existing Users

Already have Jellyfin users you want to move to SSO without losing watch history? See [MIGRATION.md](MIGRATION.md) — username-match is automatic, with opt-in migration and display name sync available in the General settings tab.

## How It Works

```
Browser                    Jellyfin Plugin              Identity Provider
   |                            |                            |
   |--- Click SSO button ------>|                            |
   |                            |--- OIDC authorize -------->|
   |<---------------------------|    (with PKCE)             |
   |                            |                            |
   |--- Login at IdP -----------|--------------------------->|
   |<---------------------------|------- callback + code ----|
   |                            |                            |
   |                            |--- exchange code --------->|
   |                            |<------ ID token + roles ---|
   |                            |                            |
   |                            |--- sync user + RBAC        |
   |                            |--- issue Jellyfin session  |
   |<--- authenticated ---------|                            |
```

1. User clicks the SSO login button on the Jellyfin login page
2. Plugin redirects to the IdP's authorization endpoint (with PKCE)
3. User authenticates at the IdP
4. IdP redirects back with an authorization code
5. Plugin exchanges the code for tokens, extracts roles from the configured claim path
6. Plugin syncs the Jellyfin user (creates or updates) and applies role-based permissions via `UpdatePolicyAsync`
7. Plugin issues a Jellyfin session token and redirects to the dashboard

## RBAC Details

### Role Merging

When a user matches multiple role mappings, permissions are **merged (union)**:
- Boolean permissions: `true` if **any** matched role has it enabled
- Libraries: union of all matched roles' library sets
- `EnableAllLibraries`: `true` if any role enables it
- `MaxParentalRating`: highest value across all matched roles

### Priority

Each role mapping has a priority field. Higher priority roles take precedence in ordering, though merge semantics still apply.

### Default Role

If no role mappings match a user's IdP roles, the **Default Role** (configured in the General tab) is used as a fallback.

### Supported Claim Paths

The **Role Claim Path** supports:

| Path                   | Token Structure                                  | Provider     |
|------------------------|--------------------------------------------------|--------------|
| `groups`               | `{"groups": ["admin", "users"]}`                 | Authentik    |
| `realm_access.roles`   | `{"realm_access": {"roles": ["admin"]}}`         | Keycloak     |
| `roles`                | `{"roles": ["admin"]}`                           | Custom/Azure |

The plugin checks both the ID token and access token for role claims.

## Reverse proxy / redirect_uri

The plugin builds the OIDC `redirect_uri` from Jellyfin's published URL via `IServerApplicationHost.GetSmartApiUrl()`. This honours Jellyfin's **Published Server URLs** field (Admin Dashboard → Networking) and any trusted `X-Forwarded-*` headers from a proxy listed under **Known proxies**.

If your IdP rejects the callback with `Invalid redirect_uri` (or you see `127.0.0.1:8096` in the URL), pick one of these:

- **Recommended:** set **Published Server URL** in Jellyfin → Networking and/or add your proxy to **Known proxies** so Jellyfin trusts the forwarded host.
- **Or:** set the per-provider **Server Base URL** field to the exact origin your IdP has registered (e.g. `https://jellyfin.example.com`). It overrides auto-detection.

The path is always appended as `/sso/OIDC/Callback/{providerId}`, so make sure the IdP's allowed redirect URI matches that suffix.

## Identity Provider Guides

| Provider | Guide | Role Claim |
|----------|-------|------------|
| Authentik | [examples/authentik/SETUP.md](examples/authentik/SETUP.md) | `groups` |
| Azure AD / Entra ID | [examples/azure-ad/SETUP.md](examples/azure-ad/SETUP.md) | `roles` or `groups` |
| Google | [examples/google/SETUP.md](examples/google/SETUP.md) | `email` (groups require Workspace) |
| Okta | [examples/okta/SETUP.md](examples/okta/SETUP.md) | `groups` |
| Pocket ID | [examples/pocket-id/SETUP.md](examples/pocket-id/SETUP.md) | `groups` |
| Authelia | [examples/authelia/SETUP.md](examples/authelia/SETUP.md) | `groups` |
| Tinyauth | [examples/tinyauth/SETUP.md](examples/tinyauth/SETUP.md) | `groups` |

### Keycloak (quick reference)

1. Create a new Client (Client type: OpenID Connect, Client authentication: On)
2. Set Valid Redirect URIs: `https://jellyfin.example.com/sso/OIDC/Callback/keycloak`
3. Roles are in `realm_access.roles` by default
4. Plugin config: Authority = `https://keycloak.example.com/realms/myrealm`, Role Claim Path = `realm_access.roles`

## API Endpoints

| Method | Endpoint                          | Description                        |
|--------|-----------------------------------|------------------------------------|
| GET    | `/sso/OIDC/Start/{providerId}`    | Initiate OIDC flow                 |
| GET    | `/sso/OIDC/Callback/{providerId}` | OIDC callback (handles code exchange) |
| POST   | `/sso/OIDC/Auth/{providerId}`     | Complete authentication            |
| GET    | `/sso/OIDC/Providers`             | List enabled providers             |
| GET    | `/sso/OIDC/LoginButtons`          | JS snippet for login button auto-injection |
| GET    | `/sso/OIDC/BrandingSnippet`       | HTML snippet for Login Disclaimer  |
| GET    | `/sso/OIDC/Config/Libraries`      | List available libraries (admin)   |
| GET    | `/sso/OIDC/Config/Status`         | Plugin status (admin)              |

## Building

### Requirements

- .NET 9.0 SDK

### Build and package

```bash
dotnet publish Jellyfin.Plugin.OIDC -c Release -o publish/

# Zip the output (all DLLs + meta.json)
cd publish && zip -j ../dist/oidc-rbac.zip *.dll meta.json
```

## Project Structure

```
Jellyfin.Plugin.OIDC/
  OidcPlugin.cs                  # Plugin entry point
  meta.json                      # Plugin manifest (bundled in zip)
  Configuration/
    PluginConfiguration.cs       # Provider + role mapping config DTOs
    configPage.html              # Admin UI (embedded resource)
    oidcrbac.js                  # Admin UI logic (embedded resource)
  Api/
    OidcController.cs            # OIDC authorization code flow
    ConfigController.cs          # Admin config API
    LoginButtonController.cs     # Login button injection + BrandingSnippet
  Auth/
    OidcAuthProvider.cs          # Blocks password login for SSO users
  Services/
    StateManager.cs              # Thread-safe OIDC state with TTL
    ClaimParser.cs               # JWT claim extraction (nested paths)
    RbacService.cs               # Role-to-permission mapping engine
    UserSyncService.cs           # User provisioning, sync, and migration
    ServiceRegistrator.cs        # DI registration
```

## License

GPLv3 (required by linking against Jellyfin's GPLv3 libraries)
