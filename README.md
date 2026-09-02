# SSO-OIDC Authentication — Jellyfin Plugin

A security-hardened Jellyfin plugin providing **OpenID Connect authentication** with **role-based library access control**.

Authenticate users via any OIDC-compatible identity provider (Authentik, Keycloak, Azure AD, Okta, etc.) and automatically assign Jellyfin permissions and library access based on IdP group/role claims.

> **Forked from [Ezeqielle/jellyfin-plugin-oidc](https://github.com/Ezeqielle/jellyfin-plugin-oidc)** with significant security hardening. See [Security improvements](#security-improvements) for what was changed and why.

## Security improvements

These changes are not present in the upstream plugin:

| Area | Upstream | This fork |
|------|----------|-----------|
| JWT validation | `ReadJwtToken()` — parses only, no cryptographic verification | `ValidateToken()` against IdP's JWKS endpoint — verifies signature, issuer, audience, and lifetime |
| Nonce enforcement | Optional guard that could pass on a missing nonce claim | Always enforced — missing nonce claim is a hard rejection |
| XSS in login button | Provider values string-interpolated into generated JavaScript | Provider data JSON-serialized; values injected via DOM APIs |
| Access token validation | No signature check on access token used for role extraction | Signature-validated; per-provider toggle for non-JWT access tokens |
| Discovery endpoint hijacking | Not validated | TOFU endpoint pinning; editable pin fields let admins pre-set expected endpoints from IdP docs, eliminating first-use trust window |
| Cross-provider account takeover | No isolation | Each account is bound to the provider that created it; other providers cannot authenticate as that user |
| Local account takeover | OIDC silently takes over local accounts | Blocked by default; opt-in migration required |
| redirect_uri behind reverse proxies | `Request.Host` — fails behind proxies | `IServerApplicationHost.GetSmartApiUrl()` — honours Jellyfin's Published Server URLs |
| Callback page framing | No protection | `X-Frame-Options: DENY`, `Content-Security-Policy: frame-ancestors 'none'` |
| Memory exhaustion DoS | Unbounded pending state store | Hard cap (500 pending states, 200 sessions); returns 503 when full |
| Cross-provider role escalation | Role mappings are global | Optional per-mapping `Provider Filter` restricts a mapping to one provider |

## Features

- **OIDC Authentication** with PKCE (Authorization Code flow)
- **Multi-provider support** — configure multiple IdPs simultaneously with branded login buttons
- **Provider isolation** — each Jellyfin account is bound to the provider that created it; cross-provider impersonation is blocked
- **Role-based access control** — map IdP roles/groups to Jellyfin permissions and specific libraries
- **Per-provider role filter** — restrict a role mapping to one specific provider to prevent cross-provider privilege grants
- **Endpoint pinning** — TOFU pins discovery endpoints on first use; editable pin fields let you pre-set expected values from your IdP docs to eliminate the first-use trust window
- **Auto-provisioning** — create Jellyfin users on first SSO login
- **Flexible claim parsing** — extract roles from nested JWT claims (e.g. `realm_access.roles`, `groups`)
- **Merge semantics** — users with multiple roles get the union of all permissions (most permissive wins)
- **Default role fallback** — assign a baseline role to users with no matching IdP roles
- **Fail-closed RBAC** — deny login when no IdP role or configured default role matches, preventing stale permissions from surviving role removal
- **Admin UI** — full configuration from the Jellyfin dashboard (Providers, Role Mappings, General settings), reachable directly from the dashboard's left nav under **Plugins**
- **Login button injection** — paste one HTML snippet into Jellyfin's Login Disclaimer; buttons appear automatically
- **Native/mobile app login** — sign in Android, iOS, and TV apps via Jellyfin [Quick Connect](#mobile--native-apps-quick-connect)
- **Profile image sync** — set the Jellyfin avatar from the IdP's `picture` claim on each login
- **Opt-in local user migration** — switch existing password accounts to SSO on first login
- **Opt-in display name sync** — keep Jellyfin account names in sync with the IdP
- **Disabled user enforcement** — disabled Jellyfin accounts are blocked from SSO login

## Installation

### Add repository to Jellyfin

```
https://raw.githubusercontent.com/aussierk/jellyfin-plugin-oidc/main/manifest.json
```

1. Go to **Admin Dashboard → Plugins → Repositories**
2. Click **Add repository** and paste the URL above (Repository Name: `SSO-OIDC Authentication`)
3. Go to **Catalog → Authentication**
4. Install **SSO-OIDC Authentication**
5. Restart Jellyfin

### Release channels

| Channel | Repository URL | Contents |
|---|---|---|
| Stable | `https://raw.githubusercontent.com/aussierk/jellyfin-plugin-oidc/main/manifest.json` | Full releases only (e.g. `1.0.6.0`) |
| Testing | `https://raw.githubusercontent.com/aussierk/jellyfin-plugin-oidc/dev/manifest.json` | Release-candidate builds, may be unstable |

Add the Testing URL as a second repository (same steps as above) if you want early access to RC builds. Stick with Stable for normal use.

### Manual installation

1. Download `oidc-rbac.zip` from the [latest release](https://github.com/aussierk/jellyfin-plugin-oidc/releases/latest)
2. On your server, create a folder named `SSO-OIDC Authentication_1.0.5.1` inside your Jellyfin plugins directory (e.g. `/config/plugins/`)
3. Extract the contents of the zip into that folder
4. Restart Jellyfin

> **Upgrading from a previous version?** Stop Jellyfin, delete the old plugin folder entirely, create a fresh folder with the new version number, extract the zip, then start Jellyfin. Jellyfin must be fully restarted (not just the browser) for the new DLL to load.

## Quick Start

### 1. Configure a Provider

Go to **Admin Dashboard → Plugins → SSO-OIDC Authentication → Providers tab**

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
| Picture Claim      | `picture`                                                  |
| Sync profile image | *(checkbox, on by default)*                                |
| Server Base URL    | *(optional, e.g. `https://jellyfin.example.com`)*          |

> **Server Base URL** is only needed if Jellyfin can't resolve its public URL on its own (e.g. behind a reverse proxy whose `X-Forwarded-*` headers aren't trusted). See [Reverse proxy / redirect_uri](#reverse-proxy--redirect_uri).

### Profile image sync

When **Sync profile image** is enabled, on every login the plugin reads the **Picture Claim**
(default `picture`, the standard OIDC avatar claim) and sets it as the user's Jellyfin avatar,
overwriting any existing one. It looks in the ID token, then the access token, then the
provider's **userinfo** endpoint. Failures never block login. Leave the claim blank or uncheck
the box to disable it for a provider.

> The provider must actually emit the claim. Many IdPs do not include `picture` by default:
> - **Authentik** — its default `profile` scope omits `picture`. Add a Scope Mapping (scope
>   name `profile`) with expression `return {"picture": request.user.avatar}`.
> - **Keycloak** — add a "User Attribute"/hardcoded mapper that puts a `picture` claim in the
>   ID token or userinfo.
> - **Google** — includes `picture` in the ID token by default.

After filling in the fields, click **Test Connection**. This validates the authority URL, fetches the discovery document, and automatically fills in the **Endpoint Pins** section (Issuer, Token Endpoint, JWKS URI). Once pinned, any unexpected change to those endpoints in a future discovery fetch will block logins and alert you in the logs.

> **For maximum security:** copy the Issuer, Token Endpoint, and JWKS URI values directly from your IdP's documentation and paste them into the Endpoint Pins fields *before* clicking Test Connection. Test Connection will then verify the live discovery document matches your expected values — eliminating any window where a MITM could intercept the first-use trust. If you leave the pins empty, Test Connection fills them in automatically (Trust On First Use).

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

> **Multiple providers configured?** Use the **Provider Filter** dropdown on each role mapping to restrict it to a specific provider. Without a filter, a role mapping applies to users from *all* providers — so if two providers both issue a role named `admin`, users from either will get admin access. See [Multi-provider role isolation](#multi-provider-role-isolation).

### 3. General Settings

Go to **General tab** and configure:

| Setting                           | Default | Description |
|-----------------------------------|---------|-------------|
| Auto-create users                 | On      | Create a Jellyfin account on first SSO login |
| Default Role                      | —       | Fallback role when no IdP role matches a mapping; login is denied if neither a role nor a valid default matches |
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

Copy the `Html` field from the response and paste it into the Login Disclaimer field. The snippet contains one `<a>` link per enabled provider with no JavaScript required. The links use Jellyfin's native button classes, so they match whatever theme/skin the server is using (including dark and community themes). A provider's **Button Color** setting, when changed from the default, is applied as a scoped background-color override on top of the themed button.

> The auto-injected buttons (`/sso/OIDC/LoginButtons`) and the `BrandingSnippet` links automatically
> honor a Jellyfin **base URL** (Admin Dashboard → Networking → Base URL). If you hand-write an
> `<a>` snippet yourself and run Jellyfin under a base path, prefix the href, e.g.
> `href="/base_url/sso/OIDC/Start/authentik"`.

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

## Mobile & native apps (Quick Connect)

**The SSO login button only works in the browser-based Jellyfin Web client.** The button is
injected into the web login page and the flow finishes by writing credentials into the browser's
`localStorage`. Native apps (Android, iOS/Swiftfin, Android TV, etc.) render their own login
screen and keep credentials in native storage, so they never see the button and can't consume that
web session. This is a limitation of how Jellyfin exposes login to plugins, not a bug.

To sign a native app in via SSO, the plugin bridges to Jellyfin's built-in **Quick Connect**:

```
Native app                 Browser                    Jellyfin Plugin           Identity Provider
   |                          |                            |                          |
   |-- tap Quick Connect      |                            |                          |
   |   (shows 6-digit code)   |                            |                          |
   |   ...polling...          |                            |                          |
   |                          |-- open QuickConnect link ->|-- OIDC authorize ------->|
   |                          |<-- login at IdP -----------|<------ callback + code --|
   |                          |                            |-- sync user + RBAC       |
   |                          |-- enter 6-digit code ----->|-- AuthorizeRequest ----->|
   |<-- authenticated, signed in --------------------------|                          |
```

**Setup:**

1. Enable **Quick Connect** in Jellyfin: *Admin Dashboard → General → Quick Connect → Enable*.
2. On the mobile/native app, open the login screen and choose **Quick Connect** — it shows a
   6-digit code and starts polling.
3. In any browser (on the phone or another device), open
   `https://jellyfin.example.com/sso/OIDC/QuickConnect/<providerId>`
   (the injected login page also shows a small *"Sign in a device … (Quick Connect)"* link for this).
4. Authenticate at your IdP as usual.
5. Enter the 6-digit code from step 2 and click **Authorize**.
6. The native app's poll completes and it signs in.

> Quick Connect codes are short-lived. Start the flow on the app first, then enter the code
> promptly. A mistyped code can be re-entered without repeating the IdP login.

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

If no role mappings match a user's IdP roles, the **Default Role** (configured in the General tab) is used as a fallback. If neither a role mapping nor a valid Default Role matches, login is denied — the plugin never falls back to Jellyfin's stock default permissions or lets a user keep a policy from a previous login. This is deliberate: it stops a role or role mapping removed at the IdP or in plugin config from silently leaving a user with access they should no longer have.

### Multi-provider role isolation

Role mappings are **global by default** — they apply to users from every configured provider. If two providers both issue a role with the same name (e.g. `admin`), users from either provider get the same Jellyfin permissions.

Use the **Provider Filter** field on each role mapping to restrict it to one provider:

| Provider  | Role name in IdP | Role Mapping name | Provider Filter |
|-----------|-----------------|-------------------|-----------------|
| Keycloak  | `admin`         | `admin`           | `keycloak`      |
| Okta      | `admin`         | `admin`           | `okta`          |

Without a filter the mapping is global. A filter of `keycloak` means only users authenticated via the `keycloak` provider will match that mapping, even if an Okta user also has a role named `admin`.

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

## Avoiding admin lockout

> **Keep at least one local Jellyfin admin account with password authentication.** This is your recovery path if SSO becomes unavailable.

### Why this matters

This plugin pins the OIDC discovery endpoints (issuer, token endpoint, JWKS URI) the first time a provider is used. If those endpoints change — which can happen when you upgrade your IdP (Authentik, Keycloak, and others occasionally restructure their OIDC paths between versions) — all SSO logins will be blocked until an admin re-runs **Test Connection** in the plugin config.

If every admin account is an SSO account, you cannot reach the admin UI to fix it. You are locked out of your own server.

### How to maintain a local fallback account

1. In Jellyfin, go to **Admin Dashboard → Users → Add User**
2. Create a user (e.g. `jellyfin-local-admin`) with a strong password
3. Grant it Administrator permissions
4. Set its Authentication Provider to **Default** (not OIDC) — this ensures it always logs in with a local password regardless of SSO state
5. Store the credentials somewhere safe (password manager, etc.)

> If `MigrateLocalUsers` is enabled in the plugin's General settings, this account will be migrated to SSO if it ever logs in via the SSO flow. **Do not use this account to log in via SSO** — use it only as a break-glass fallback via the standard Jellyfin login form.

### Recovery: re-pinning after an IdP update

If SSO logins start failing after an IdP upgrade:

1. Log in with your local fallback account
2. Go to **Admin Dashboard → Plugins → SSO-OIDC Authentication**
3. Find the affected provider and click **Test Connection**
4. If the test succeeds, the endpoints are re-pinned and SSO logins resume immediately
5. If the test fails, the IdP is unreachable or misconfigured — check the Jellyfin logs for the exact mismatch

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
| GET    | `/sso/OIDC/Start/{providerId}`    | Initiate OIDC flow (web client)    |
| GET    | `/sso/OIDC/Callback/{providerId}` | OIDC callback (handles code exchange) |
| POST   | `/sso/OIDC/Auth/{providerId}`     | Complete authentication (web client) |
| GET    | `/sso/OIDC/QuickConnect/{providerId}` | Initiate OIDC flow for a native app via Quick Connect |
| POST   | `/sso/OIDC/QuickConnect/Authorize/{providerId}` | Authorize a Quick Connect code after OIDC login |
| GET    | `/sso/OIDC/Providers`             | List enabled providers             |
| GET    | `/sso/OIDC/LoginButtons`          | JS snippet for login button auto-injection |
| GET    | `/sso/OIDC/BrandingSnippet`       | HTML snippet for Login Disclaimer  |
| GET    | `/sso/OIDC/Config/Libraries`      | List available libraries (admin)   |
| GET    | `/sso/OIDC/Config/Status`         | Plugin status (admin)              |

## Known limitations

These are architectural constraints rather than bugs. They are documented here so you can plan your deployment accordingly.

| Limitation | Impact | Mitigation |
|---|---|---|
| Client secret stored as plaintext | Anyone with filesystem read access to the Jellyfin data directory can extract OIDC client secrets | Restrict filesystem permissions on the Jellyfin data directory; use a dedicated service account |
| No rate limiting on auth endpoints | `/sso/OIDC/Start` is unauthenticated and could be used to generate load | Place a rate-limiting reverse proxy (nginx, Traefik, Caddy) in front of Jellyfin |
| No back-channel logout | If a user is disabled at the IdP, their active Jellyfin session remains valid until it expires | Use Jellyfin's built-in user disable to immediately block access; set a shorter session timeout |
| No refresh token support | OIDC role changes at the IdP are not reflected until the user logs in again | Users must re-authenticate to pick up permission changes |
| In-memory state only | Pending auth states and sessions are lost on server restart; any login in progress at restart time must be retried | Transient; retry is seamless |
| Single-node only | The in-memory state store means the OIDC callback must reach the same Jellyfin instance that initiated the login | If running multiple Jellyfin nodes, ensure sticky sessions at the load balancer |

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
