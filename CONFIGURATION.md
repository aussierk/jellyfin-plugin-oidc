# Configuration Reference

For the minimal setup, see the [README Quick Start](README.md#quick-start). This is the
detailed reference for everything under it.

## Provider fields

Go to **Admin Dashboard → Plugins → SSO-OIDC-Authentication → Providers tab**.

| Field              | Example (Authentik)                                 |
|--------------------|------------------------------------------------------|
| Provider ID        | `authentik`                                          |
| Display Name       | `Authentik`                                          |
| Issuer URL         | `https://auth.example.com/application/o/jellyfin/`  |
| Client ID          | *(from your IdP)*                                    |
| Client Secret      | *(from your IdP)*                                    |
| Client Secret File | *(optional - see below)*                             |
| Scopes             | `openid profile email`                               |
| Role Claim Path    | `groups`                                             |
| Username Claim     | `preferred_username`                                 |
| Display Name Claim | `name`                                               |
| Email Claim        | `email` *(falls back to `emails` for Entra)*         |
| Email Verified Claim | `email_verified`                                   |
| Picture Claim      | `picture`                                            |
| Sync profile image | *(checkbox, on by default)*                          |
| Trusted for email-based account linking | *(checkbox, off by default - see [Account matching](#account-matching-and-linking))* |
| Server Base URL    | *(optional, e.g. `https://jellyfin.example.com`)*    |
| Additional Parameters | *(optional, e.g. `prompt=consent&ui_locales=en`)* |

> **Server Base URL** is only needed if Jellyfin can't resolve its public URL on its own
> (e.g. behind a reverse proxy whose `X-Forwarded-*` headers aren't trusted). See
> [Reverse proxy / redirect_uri](#reverse-proxy--redirecturi).

> **Additional Parameters** are appended to the authorization request as a query string, so
> they follow query-string rules: `&`-separated `key=value` pairs, `%XX` is percent-decoded,
> `+` decodes to a space, and surrounding whitespace is trimmed. A token with no `=` is
> ignored (and logged). To send a literal `+` in a value, write it as `%2B`.

After filling in the fields, click **Test Connection**. This validates the Issuer URL, fetches
the discovery document, and pins the endpoints it returns (authorization, token, JWKS,
userinfo) against that exact issuer. Once pinned, any unexpected change to those endpoints in
a future discovery fetch blocks logins and alerts you in the logs.

The pinned values are only ever written by a successful Test Connection (or a Trust-On-First-Use
pin at the first login), so they don't drift from what the IdP actually returned. If you edit
the Issuer URL on a provider that's already pinned, the config page makes you re-run **Test
Connection** before it will save - and the plugin rejects the same change if it arrives through
the plugin-configuration API instead, so a stale pin can't be persisted for an unverified issuer
by either route. To move a provider to a new issuer without re-testing, clear its `Pinned*`
fields; it will re-pin via TOFU on the next login.

> **For maximum security:** click **Test Connection** immediately after entering the Issuer
> URL, before saving, so the endpoints are pinned from the very first discovery fetch rather
> than left to Trust On First Use at the first real login. If the browser you're using to
> configure the plugin can't reach the IdP (e.g. it's on a different network than your admin
> workstation), Test Connection will fail there too - in that case TOFU-on-first-login is
> unavoidable with the current fields.

## Keeping the client secret out of the config

The plaintext **Client Secret** field still works and is the default, but you don't have to
use it:

- **Client Secret File** - point it at a file (e.g. a Docker/Kubernetes-mounted secret) and
  the plugin reads the secret from there instead. Takes priority over Client Secret when set
  and readable.
- **Environment variable** - set **Client Secret** to `${VAR_NAME}` and the plugin resolves
  it from that environment variable at login time. The literal `${VAR_NAME}` - not the real
  secret - is what ends up on disk in the plugin's config.

**With more than one provider, give each one its own file/variable** - e.g.
`${KEYCLOAK_CLIENT_SECRET}` and `${AUTHENTIK_CLIENT_SECRET}`, or
`/run/secrets/keycloak_client_secret` and `/run/secrets/authentik_client_secret`. Two
providers pointed at the same one authenticate with the same secret; since each provider is
registered with its own client ID at its own IdP, the IdP rejects the token exchange for
whichever provider it doesn't actually belong to (a loud `invalid_client` failure, not a
silent security issue, but still a config mistake worth avoiding). The config page suggests a
provider-specific variable name for exactly this reason.

Either way, the actual secret is only ever read at the moment it's needed and is never
written back into the plugin config.

## Profile image sync

When **Sync profile image** is enabled, on every login the plugin reads the **Picture Claim**
(default `picture`, the standard OIDC avatar claim) and sets it as the user's Jellyfin avatar,
overwriting any existing one. It looks in the ID token, then the access token, then the
provider's **userinfo** endpoint. Failures never block login. Leave the claim blank or
uncheck the box to disable it for a provider.

> The provider must actually emit the claim. Many IdPs do not include `picture` by default:
> - **Authentik** - its default `profile` scope omits `picture`. Add a Scope Mapping (scope
>   name `profile`) with expression `return {"picture": request.user.avatar}`.
> - **Keycloak** - add a "User Attribute"/hardcoded mapper that puts a `picture` claim in
>   the ID token or userinfo.
> - **Google** - includes `picture` in the ID token by default.

## Role Mappings

Go to the **Role Mappings** tab and create mappings:

**Example - Admin role:**
- Role Name: `jellyfin-admins`
- Administrator: checked
- All Libraries: checked

**Example - Standard user:**
- Role Name: `jellyfin-users`
- Libraries: select specific libraries
- Playback, Remote Access, Transcoding: checked

**Example - Kids:**
- Role Name: `jellyfin-kids`
- Libraries: Kids only
- Max Parental Rating: pick `PG` (or your country's equivalent) from the dropdown

> **Multiple providers configured?** Use the **Provider Filter** dropdown on each role
> mapping to restrict it to a specific provider. Without a filter, a role mapping applies to
> users from *all* providers - see [Multi-provider role isolation](#multi-provider-role-isolation).

### Role merging

When a user matches multiple role mappings, permissions are **merged (union)**:
- Boolean permissions: `true` if **any** matched role has it enabled
- Libraries: union of all matched roles' library sets
- `EnableAllLibraries`: `true` if any role enables it
- **Max Parental Rating: strictest (lowest) wins** across matched roles. Picked by name
  (`PG-13`, `TV-14`, …) from the list your Jellyfin server recognises for its metadata
  country, and resolved to the same numeric score Jellyfin's own user screen uses.

> Older configs stored a raw parental-rating number; that value is still honoured until you
> re-pick the rating by name and save.

Every user-policy field the plugin does **not** manage - access schedules, blocked/allowed
tags, unrated-item blocks, bitrate and session caps, SyncPlay level, per-channel/device
lists - is carried through from the user's current Jellyfin settings on each OIDC login, not
reset.

### Fallback role

If no role mappings match a user's IdP roles, the **Fallback role** (a dropdown of your
defined role names, at the top of the Role Mappings tab) is applied instead. If neither a
role mapping nor a valid fallback matches, login is denied - the plugin never falls back to
Jellyfin's stock default permissions or lets a user keep a policy from a previous login. This
is deliberate: it stops a role removed at the IdP or in plugin config from silently leaving a
user with access they should no longer have.

> Fail-closed only applies while **Manage user policy** (below) is on. With it off, RBAC and
> its fail-closed denial are both disabled - any authenticated user who passes the admission
> gate signs in with whatever policy Jellyfin already has for them.

### Turning off policy management

**Manage user policy** (Role Mappings tab, default on) is the plugin's one RBAC opt-out. Off:
- The plugin never calls `UpdatePolicyAsync` - permissions, admin status, and libraries are
  whatever Jellyfin already has for the account, and stay that way.
- Fail-closed no longer applies: an authenticated user who matches no role mapping still
  signs in, instead of being denied.
- A brand-new SSO user is auto-created with Jellyfin's stock default policy; the admin grants
  access by hand afterward.
- The admission gate (allowed groups, require-verified-email) is unaffected - it's a
  separate, always-on check.

Use this if you want SSO for authentication only and prefer to manage every user's
permissions and libraries directly in Jellyfin.

For a middle ground, leave **Manage user policy** on and uncheck **Manage library access
from role mappings**: RBAC still sets admin status and playback/management permissions from
role mappings, but never touches library assignments - assign those by hand in Jellyfin.

### Admission allowlist

Separate from RBAC, the **Access** section (top of the Role Mappings tab) gates *who may
sign in at all*: allowed groups (matched against the role claim), plus an optional "require
verified email" (`email_verified`) hard gate. Leave *Allowed groups* empty to admit everyone
who authenticates.

Checking **Require a verified email** also activates two more lists: *Allowed email domains*
and *Allowed emails*. A login is admitted if it matches any allowed group, domain, or exact
email - the lists are additive, not all required. Both lists stay empty and inert unless
verified email is required, since an unverified email can't be trusted as an admission
signal; the fields grey out in the UI to make that dependency visible.

### Multi-provider role isolation

Role mappings are **global by default** - they apply to users from every configured
provider. If two providers both issue a role with the same name (e.g. `admin`), users from
either provider get the same Jellyfin permissions.

Use the **Provider Filter** field on each role mapping to restrict it to one provider:

| Provider  | Role name in IdP | Role Mapping name | Provider Filter |
|-----------|-----------------|-------------------|-----------------|
| Keycloak  | `admin`         | `admin`           | `keycloak`      |
| Okta      | `admin`         | `admin`           | `okta`          |

Without a filter the mapping is global. A filter of `keycloak` means only users
authenticated via the `keycloak` provider will match that mapping, even if an Okta user also
has a role named `admin`.

### Supported claim paths

The **Role Claim Path** supports:

| Path                   | Token Structure                                  | Provider     |
|------------------------|--------------------------------------------------|--------------|
| `groups`               | `{"groups": ["admin", "users"]}`                 | Authentik, Okta |
| `realm_access.roles`   | `{"realm_access": {"roles": ["admin"]}}`         | Keycloak     |
| `roles`                | `{"roles": ["admin"]}`                           | Custom/Entra |

The plugin resolves the role claim from the **ID token** first, then the **access token**,
then the **`userinfo` endpoint** - stopping at the first source that yields a value. The
`userinfo` lookup covers IdPs (Entra ID, some Okta configs) that only expose group
membership there; it also uses the same nested-path syntax. A single `userinfo` request is
shared with the profile-image lookup.

## General settings

Go to the **General** tab and configure:

| Setting                            | Default | Description |
|-------------------------------------|---------|-------------|
| Auto-create users                   | On      | Create a Jellyfin account on first SSO login |
| Fallback role (Role Mappings tab)   | -       | Role applied when no IdP role matches a mapping; login is denied if neither a role nor a valid fallback matches |
| Migrate local users to SSO          | Off     | Switch existing password accounts to SSO auth on first SSO login. Matched by username - see [Account matching](#account-matching-and-linking) |
| Link existing users by verified email | Off   | On first login from a **Trusted for email-based account linking** provider, bind to an existing account whose stored verified email matches. See [Account matching](#account-matching-and-linking) |
| Sync display name (per provider)    | Off     | Rename the Jellyfin account to match the Display Name Claim on each login (sanitised; identity is keyed on the OIDC subject so this is safe) |
| Access allowlist (Role Mappings)    | empty   | Groups permitted to sign in at all (matched against the role claim); empty admits everyone who authenticates |
| Require a verified email (Role Mappings) | Off | Reject logins without `email_verified`; also activates the two allowlists below |
| Allowed email domains / emails (Role Mappings) | empty | Additional admission match by verified email domain or exact address; inert unless "Require a verified email" is on |
| Manage user policy (Role Mappings)  | On      | When off, the plugin never touches a user's Jellyfin policy - no RBAC, no fail-closed denial. Manage permissions by hand in Jellyfin. |
| Manage library access (Role Mappings) | On    | Uncheck to keep RBAC managing permissions/admin status while you assign libraries manually. Only meaningful while Manage user policy is on. |

## Account matching and linking

On each login the plugin resolves the Jellyfin account in this order:

1. **OIDC subject** (`sub`) - the stable identity key. A returning user always matches here.
   This is the only path that may resolve to an **administrator** account.
2. **Verified email** - only if *Link existing users by verified email* is on **and** the
   login comes from a provider marked **Trusted for email-based account linking**. Both the
   incoming `email_verified` claim and the account's stored email must be verified. Never
   binds to an administrator, and never repoints an account already bound to a different
   OIDC identity onto a different Jellyfin user.
3. **Username** - a plain `preferred_username` match against an existing account. If that
   account is local, it is only adopted when *Migrate local users to SSO* is on. Never binds
   to an administrator (admins must match by subject).
4. Otherwise a new account is created (if *Auto-create users* is on), else the login is denied.

**A verified email is only as trustworthy as the IdP asserting it.** Many IdPs let a user set
or change their own email address, so `email_verified: true` from such a provider does not
prove ownership. Enable **Trusted for email-based account linking** only for an IdP you fully
control (a corporate directory, your own Keycloak/Authentik). Leave it off for social logins
and any provider with self-service email. With it off, *Link existing users by verified email*
simply has no effect for that provider - logins still work, they just won't auto-link.

The flag is checked on the provider the login is **coming from**, not the one that originally
recorded the stored email - so a login can still link after you retire the old IdP. The
administrator-only-by-subject and no-repoint-to-another-user rules above are the backstop if a
trusted provider turns out not to deserve it.

## Add the login button

By default the plugin does this for you: with **Manage the login button in Branding
automatically** ticked (plugin config → **General** tab), clicking **Save** with at least one
enabled provider writes a marked block into **Admin Dashboard → General → Branding** - the
button markup into *Login disclaimer* and its styling into *Custom CSS*. Nothing else in
those fields is touched, and unticking + saving offers to remove the block. The button
renders full-width just below the native **Sign In** button (above Quick Connect / Forgot
Password), matching the active theme/skin (dark and community themes included); a provider's
**Button Color**, when changed from the default, colours just that button's background.

Per provider you can also set a **Button Icon** - a bundled glyph (Authentik, Keycloak,
Google, Microsoft, Okta, Auth0, Discord, GitHub) or a **Custom (image)**: paste `<svg>`
markup or a `data:image/…` URI, or pick a file (SVG / PNG / JPEG / GIF / WebP). Use this for
any provider not in the list.

When **Quick Connect** is enabled on the server (Dashboard → General), each SSO button is
followed by a small "Sign in a device with … (Quick Connect)" link. It runs the same OIDC
login in the browser and then authorizes a device that's displaying a Quick Connect code -
handy for signing a TV in from your computer. The link is omitted while Quick Connect is off.

**Hide the username/password form** (General tab) hides the web password form and Forgot
Password (Quick Connect stays) and shows a configurable **Login page heading** - plus an
optional smaller **Sign-in instructions** line (e.g. TV / Quick Connect guidance) - above the
SSO button(s). The button block itself carries Jellyfin's `.readOnlyContent` class and the
text lines use `.sectionTitle` / `.fieldDescription`, so custom themes style the whole thing
automatically.

> **Web client only.** `Login disclaimer` / `Custom CSS` are rendered solely by the Jellyfin
> web UI. Android, Android TV, Swiftfin/iOS and Kodi show their own login screens - those
> users sign in with [Quick Connect](QUICK-CONNECT.md).
>
> Jellyfin's disclaimer sanitizer forces links to open in a new tab, so clicking an SSO
> button may open the provider flow in a new tab; you end up signed in there.

**Manual install.** If you turn the setting off (e.g. you inject buttons another way), grab
the snippet yourself - plugin config → General → *Manual install (copy / paste)*, or:

```bash
curl -s https://jellyfin.example.com/sso/OIDC/LoginButtonSnippet | jq -r '.Html'   # → Login disclaimer
curl -s https://jellyfin.example.com/sso/OIDC/LoginButtonSnippet | jq -r '.Css'    # → Custom CSS
```

> The generated links honor a Jellyfin **base URL** (Admin Dashboard → Networking → Base URL)
> automatically. If you hand-write an `<a>` snippet and run under a base path, prefix the
> href, e.g. `href="/base_url/sso/OIDC/Start/authentik"`.

## Reverse proxy / redirect_uri

The plugin builds the OIDC `redirect_uri` from Jellyfin's published URL via
`IServerApplicationHost.GetSmartApiUrl()`. This honours Jellyfin's **Published Server URLs**
field (Admin Dashboard → Networking) and any trusted `X-Forwarded-*` headers from a proxy
listed under **Known proxies**.

If your IdP rejects the callback with `Invalid redirect_uri` (or you see `127.0.0.1:8096` in
the URL), pick one of these:

- **Recommended:** set **Published Server URL** in Jellyfin → Networking and/or add your
  proxy to **Known proxies** so Jellyfin trusts the forwarded host.
- **Or:** set the per-provider **Server Base URL** field to the exact origin your IdP has
  registered (e.g. `https://jellyfin.example.com`). It overrides auto-detection.

The path is always appended as `/sso/OIDC/Callback/{providerId}`, so make sure the IdP's
allowed redirect URI matches that suffix.

The plugin composes the `redirect_uri` with `System.Uri`, which applies RFC 3986
normalization: the host is lower-cased and a default port (`:443` for https, `:80` for http)
is dropped. Every mainstream IdP normalizes the registered value the same way before
comparing, so this is transparent - but if you register the redirect URI with an
upper-case host or an explicit `:443`, enter it normalized (lower-case host, no default
port) to be safe.

## Keycloak (quick reference)

Keycloak doesn't have a full walkthrough under [examples/](examples/) - this is enough to
get started:

1. Create a new Client (Client type: OpenID Connect, Client authentication: On).
2. Set Valid Redirect URIs: `https://jellyfin.example.com/sso/OIDC/Callback/keycloak`.
3. Roles are in `realm_access.roles` by default.
4. Plugin config: Authority = `https://keycloak.example.com/realms/myrealm`, Role Claim
   Path = `realm_access.roles`.
