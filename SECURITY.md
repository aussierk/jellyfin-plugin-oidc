# Security

## What changed vs. the upstream plugin

This is a fork of [Ezeqielle/jellyfin-plugin-oidc](https://github.com/Ezeqielle/jellyfin-plugin-oidc)
with significant hardening. None of the items below are present upstream.

| Area | Upstream | This fork |
|------|----------|-----------|
| JWT validation | `ReadJwtToken()` - parses only, no cryptographic verification | `ValidateToken()` against the IdP's JWKS endpoint - verifies signature, issuer, audience, and lifetime |
| Nonce enforcement | Optional guard that could pass on a missing nonce claim | Always enforced - a missing nonce claim is a hard rejection |
| XSS in login button | Provider values string-interpolated into generated JavaScript | Provider data JSON-serialized; values injected via DOM APIs |
| Access token validation | No signature check on the access token used for role extraction | Signature-validated; per-provider toggle for non-JWT access tokens |
| Discovery endpoint hijacking | Not validated | TOFU endpoint pinning; **Test Connection** verifies and pins the expected endpoints before first login, eliminating the first-use trust window |
| Cross-provider account takeover | No isolation | Each account is bound to the provider that created it; other providers cannot authenticate as that user |
| Local account takeover | OIDC silently takes over local accounts | Blocked by default; opt-in migration required |
| `redirect_uri` behind reverse proxies | `Request.Host` - fails behind proxies | `IServerApplicationHost.GetSmartApiUrl()` - honours Jellyfin's Published Server URLs |
| Callback page framing | No protection | `X-Frame-Options: DENY`, `Content-Security-Policy: frame-ancestors 'none'` |
| Memory exhaustion DoS | Unbounded pending state store | Hard cap (500 pending states, 200 sessions, 5000 tracked sessions); returns 503 when full |
| Cross-provider role escalation | Role mappings are global | Optional per-mapping `Provider Filter` restricts a mapping to one provider |
| Auth endpoint flooding | Unauthenticated `/sso/OIDC/*` endpoints take load | Built-in per-IP rate limiting on every auth endpoint (`Start`, `Callback`, `Auth`, `BackchannelLogout`) |
| IdP-driven logout | No way for the IdP to end a Jellyfin session | [OIDC Back-Channel Logout 1.0](https://openid.net/specs/openid-connect-backchannel-1_0.html) endpoint with signature, audience, `events`, and `jti` replay validation |
| Account orphaned by a rename | Identity keyed on the mutable username | Identity keyed on the OIDC `sub`; legacy rows self-heal on next login |
| Verified-email account linking | n/a | Off by default; when on, only accepts a login from a per-provider **Trusted for email-based account linking** source, never binds an administrator by email or username, and never repoints an account onto a different Jellyfin user - see [Account matching](CONFIGURATION.md#account-matching-and-linking) |

## Avoiding admin lockout

> **Keep at least one local Jellyfin admin account with password authentication.** This is
> your recovery path if SSO becomes unavailable.

### Why this matters

The plugin pins the OIDC discovery endpoints (issuer, authorization endpoint, token endpoint,
JWKS URI, userinfo endpoint) the first
time a provider is used. If those endpoints change - which can happen when you upgrade your
IdP (Authentik, Keycloak, and others occasionally restructure their OIDC paths between
versions) - all SSO logins are blocked until an admin re-runs **Test Connection** in the
plugin config.

If every admin account is an SSO account, you cannot reach the admin UI to fix it. You are
locked out of your own server.

### How to maintain a local fallback account

1. In Jellyfin, go to **Admin Dashboard → Users → Add User**.
2. Create a user (e.g. `jellyfin-local-admin`) with a strong password.
3. Grant it Administrator permissions.
4. Set its Authentication Provider to **Default** (not OIDC) - this ensures it always logs
   in with a local password regardless of SSO state.
5. Store the credentials somewhere safe (password manager, etc.).

> If **Migrate local users to SSO** is enabled, this account will be migrated to SSO if it
> ever logs in via the SSO flow. **Do not use this account to log in via SSO** - use it only
> as a break-glass fallback via the standard Jellyfin login form.

### Recovery: re-pinning after an IdP update

If SSO logins start failing after an IdP upgrade:

1. Log in with your local fallback account.
2. Go to **Admin Dashboard → Plugins → SSO-OIDC-Authentication**.
3. Find the affected provider and click **Test Connection**.
4. If the test succeeds, the endpoints are re-pinned and SSO logins resume immediately.
5. If the test fails, the IdP is unreachable or misconfigured - check the Jellyfin logs for
   the exact mismatch.

## Known limitations

These are architectural constraints rather than bugs.

| Limitation | Impact | Mitigation |
|---|---|---|
| **Single-node only** | Pending auth state, one-time sessions, and the back-channel-logout correlation table are all in-memory. The OIDC callback must reach the same Jellyfin instance that started the login, and a restart drops that in-memory state: a login in progress must be retried. A back-channel logout received after a restart no longer targets a single device - it resolves the user from the persisted `UserProviderMap` (by `sub`, by the account's last-login `sid`, or by a legacy row's username claim) and revokes **all** that user's tokens. A `sid`-only logout for an older concurrent session, or one that matches no persisted row, is a logged no-op that still returns 200 per spec. | Single instance, or sticky sessions at the load balancer. Restart impact is transient. |
| Client secret in the plugin config, by default | The **Client Secret** field is plaintext in the plugin config (this is how all Jellyfin plugin configs work) | Use **Client Secret File** (a mounted Docker/Kubernetes secret) or set Client Secret to `${ENV_VAR_NAME}` - see [Configuration](CONFIGURATION.md#keeping-the-client-secret-out-of-the-config) - so the real secret never has to live in the config; failing that, restrict permissions on the Jellyfin data directory |
| No refresh tokens | Role/permission changes at the IdP apply on the user's next login, not mid-session. RBAC is re-evaluated on every login. | Users re-authenticate to pick up changes; a back-channel logout or an admin disable forces that immediately |
| Session lifetime is Jellyfin's, not the IdP's | A Jellyfin session minted via OIDC is not capped at the `id_token` expiry | Use back-channel logout for IdP-driven revocation; set a shorter Jellyfin session timeout |
| Group claim must be reachable | Role claims are read from the ID token, then the access token, then the `userinfo` endpoint - in that order. An IdP that exposes groups by none of those routes (e.g. only via a separate directory API) is not supported. | Configure the IdP to emit the group/role claim in a token or in `userinfo` (a Keycloak client-scope mapper, an Entra groups claim, an Okta groups claim) |
| Per-IP rate limiting needs Jellyfin's proxy config | The rate limiter on the auth endpoints keys on the connection's remote IP. Behind a reverse proxy, that is the proxy's IP unless Jellyfin's **Networking → Known proxies** is configured, so every SSO user shares one bucket and one abandoned-login flood can 429 all of them. | Set **Known proxies** (and known networks) in Jellyfin's network settings so `X-Forwarded-For` is honoured and each client is limited independently |

## Reporting a vulnerability

Open a [GitHub issue](https://github.com/aussierk/jellyfin-plugin-oidc/issues) or, for
anything sensitive, reach out to the maintainer directly rather than filing publicly.
