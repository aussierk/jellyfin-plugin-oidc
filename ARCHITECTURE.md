# Architecture

## How it works

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

1. User clicks the SSO login button on the Jellyfin login page.
2. Plugin redirects to the IdP's authorization endpoint (with PKCE).
3. User authenticates at the IdP.
4. IdP redirects back with an authorization code.
5. Plugin exchanges the code for tokens and reads roles from the configured claim path -
   trying the ID token, then the access token, then the `userinfo` endpoint.
6. Plugin syncs the Jellyfin user (creates or updates) and applies role-based permissions
   via `UpdatePolicyAsync`.
7. Plugin issues a Jellyfin session token and redirects to the dashboard.

See [Mobile & native apps](QUICK-CONNECT.md) for the parallel Quick Connect flow used by
native clients.

## API endpoints

| Method | Endpoint                          | Description                        |
|--------|-----------------------------------|-------------------------------------|
| GET    | `/sso/OIDC/Start/{providerId}`    | Initiate OIDC flow (web client)    |
| GET    | `/sso/OIDC/Callback/{providerId}` | OIDC callback (handles code exchange) |
| POST   | `/sso/OIDC/Auth/{providerId}`     | Complete authentication (web client) |
| GET    | `/sso/OIDC/QuickConnect/{providerId}` | Initiate OIDC flow for a native app via Quick Connect |
| POST   | `/sso/OIDC/QuickConnect/Authorize/{providerId}` | Authorize a Quick Connect code after OIDC login |
| POST   | `/sso/OIDC/BackchannelLogout/{providerId}` | OIDC Back-Channel Logout 1.0 - IdP posts a signed `logout_token`; revokes the matching Jellyfin session(s) |
| GET    | `/sso/OIDC/Providers`             | List enabled providers             |
| GET    | `/sso/OIDC/LoginButtonSnippet`    | `{ Html, Css }` for Login Disclaimer + Custom CSS |
| GET    | `/sso/OIDC/Config/Libraries`      | List available libraries (admin)   |
| GET    | `/sso/OIDC/Config/Ratings`        | List parental ratings for this server (admin) |
| GET    | `/sso/OIDC/Config/Status`         | Plugin status (admin)              |
| POST   | `/sso/OIDC/Config/TestProvider`   | Validate a provider's discovery document (admin) |

## Project structure

```
Jellyfin.Plugin.OIDC/
  OidcPlugin.cs                  # Plugin entry point
  meta.json                      # Plugin manifest (bundled in zip)
  Configuration/
    PluginConfiguration.cs       # Provider + role mapping config DTOs
    configPage.html              # Admin UI (embedded resource)
    oidcrbac.js                  # Admin UI logic (embedded resource; generated - see src/)
    src/                         # ES modules oidcrbac.js is bundled from; edit these, not the bundle
  Api/
    OidcController.cs            # OIDC authorization code flow, back-channel logout
    ConfigController.cs          # Admin config API (libraries, ratings, test connection)
    LoginButtonController.cs     # Serves the login-button snippet (HTML/CSS for Jellyfin Branding)
  Auth/
    OidcAuthProvider.cs          # Blocks password login for SSO users
  Services/
    StateManager.cs              # Thread-safe OIDC state, session tracking, jti replay guard
    ClaimParser.cs                # JWT/JSON claim extraction (nested paths, userinfo)
    ClientSecretResolver.cs       # Resolves ClientSecretFile / ${ENV_VAR} / plaintext
    RbacService.cs                # Role-to-permission mapping engine
    UserSyncService.cs            # User provisioning, sync, and cross-provider reclaim
    UserProviderMapStore.cs      # Identity→account map in its own JSON file (migrated out of the plugin config); debounced writes
    LoginButtonSnippetBuilder.cs  # Login-button HTML/CSS generation
    ProviderButtonAssets.cs       # Sanitizes provider button colour / icon (shared by the snippet + GetProviders)
    KnownProviderIcons.cs         # Bundled provider glyphs
    ProfileImageService.cs        # Avatar sync from the picture claim
    AuthorityGuard.cs             # SSRF classification + DNS-rebinding pinned HttpClient
    GuardedHttpClientFactory.cs  # One entry point for every outbound IdP call - runs AuthorityGuard, returns a pinned client or a block reason
    RateLimitAttribute.cs        # Per-IP rate limiting for auth endpoints (`[RateLimit]` action filter)
    OidcSessionEndedConsumer.cs   # Prunes back-channel-logout tracking on session end
    OidcUserDeletedConsumer.cs    # Prunes the identity map when a Jellyfin user is deleted
    ServiceRegistrator.cs         # DI registration
```

For the release/build pipeline, see [DEVELOPMENT.md](DEVELOPMENT.md) and
[RELEASING.md](RELEASING.md).
