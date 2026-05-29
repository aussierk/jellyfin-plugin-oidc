# Authentik + Jellyfin OIDC RBAC Setup

## Overview

This guide configures Authentik as the OIDC identity provider for Jellyfin,
with group-based role mapping to control library access.

**Architecture:**
```
User → Jellyfin login page → "Sign in with Authentik" button
     → Authentik login/consent → callback to Jellyfin
     → Plugin reads groups claim → maps to Jellyfin libraries/permissions
```

---

## 1. Start the stack

```bash
# Generate a real secret key
echo "AUTHENTIK_SECRET_KEY=$(openssl rand -hex 32)" > .env
echo "PG_PASS=$(openssl rand -hex 16)" >> .env

docker compose up -d
```

- Jellyfin: http://localhost:8096
- Authentik: http://localhost:9000

Complete the Jellyfin initial setup wizard first (create an admin account).

For Authentik, create the admin account at http://localhost:9000/if/flow/initial-setup/

---

## 2. Configure Authentik

### 2.1 Create Groups

Go to **Directory > Groups** and create the following groups:

| Group Name           | Purpose                                    |
|----------------------|--------------------------------------------|
| `jellyfin-admins`    | Full admin access, all libraries           |
| `jellyfin-users`     | Standard access, selected libraries        |
| `jellyfin-kids`      | Restricted access, kids library only       |

Assign your test users to the appropriate groups.

### 2.2 Create the OIDC Provider

Go to **Applications > Applications > Create with Provider**

**Application settings:**
- Name: `Jellyfin`
- Slug: `jellyfin`

**Provider settings (OAuth2/OpenID Connect):**
- Name: `Jellyfin OIDC Provider`
- Authentication flow: `default-authentication-flow`
- Authorization flow: `default-provider-authorization-implicit-consent`
- Client type: **Confidential**
- Client ID: (copy this, you'll need it)
- Client Secret: (copy this, you'll need it)
- Redirect URIs:
  ```
  http://localhost:8096/sso/OIDC/Callback/authentik
  ```
  (adjust host/port for your setup)
- Signing Key: `authentik Self-signed Certificate`

**Advanced Protocol Settings:**
- Scopes: `openid`, `email`, `profile` (all three selected)
- Subject mode: `Based on the User's username`
- **Include claims in ID token: ENABLED** (critical — the plugin reads claims from the ID token)

### 2.3 Verify the Discovery Endpoint

```bash
curl -s http://localhost:9000/application/o/jellyfin/.well-known/openid-configuration | jq .
```

You should see all the OIDC endpoints.

### 2.4 (Optional) Custom Property Mapping for Roles

By default, Authentik's `profile` scope includes a `groups` claim with all group names.
This works out of the box — set `RoleClaim` to `groups` in the plugin config.

If you want a filtered `roles` claim instead (only jellyfin-related groups):

Go to **Customization > Property Mappings > Create > Scope Mapping**

- Name: `Jellyfin Roles`
- Scope name: `jellyfin-roles`
- Expression:

```python
JELLYFIN_PREFIX = "jellyfin-"

return {
    "roles": [
        g.name.removeprefix(JELLYFIN_PREFIX)
        for g in request.user.groups.all()
        if g.name.startswith(JELLYFIN_PREFIX)
    ]
}
```

Then add this scope to your provider (edit the provider, add `Jellyfin Roles` to Scopes).

With this mapping, a user in `jellyfin-admins` and `jellyfin-users` gets:
```json
{ "roles": ["admins", "users"] }
```

Update the plugin config:
- Scopes: `openid profile email jellyfin-roles`
- Role Claim Path: `roles`

---

## 3. Configure the Jellyfin Plugin

### 3.1 Build and Install

```bash
# From the repo root
make docker-build

# Copy DLLs into the example plugin mount
cp dist/*.dll examples/authentik/plugin/

# Restart Jellyfin to pick up the plugin
docker compose restart jellyfin
```

### 3.2 Plugin Configuration

Go to **Jellyfin Admin Dashboard > Plugins > OIDC RBAC**

#### Providers Tab

Click **+ Add Provider** and fill in:

| Field              | Value                                                                 |
|--------------------|-----------------------------------------------------------------------|
| Provider ID        | `authentik`                                                           |
| Display Name       | `Authentik`                                                           |
| Authority URL      | `http://authentik-server:9000/application/o/jellyfin/`                |
| Client ID          | (from Authentik provider)                                             |
| Client Secret      | (from Authentik provider)                                             |
| Scopes             | `openid profile email`                                                |
| Role Claim Path    | `groups`                                                              |
| Username Claim     | `preferred_username`                                                  |
| Display Name Claim | `name`                                                                |
| Button Color       | `#fd4b2d` (Authentik brand color)                                     |
| Enabled            | checked                                                               |

> **Note on Authority URL:** If Jellyfin and Authentik are in the same Docker network,
> use the internal hostname `http://authentik-server:9000/application/o/jellyfin/`.
> The browser redirect still goes through `http://localhost:9000` because the authorization
> endpoint is resolved from the discovery doc which uses the external URL.
>
> If you have issues, set the Authority URL to the external URL:
> `http://localhost:9000/application/o/jellyfin/`

If you created the custom `jellyfin-roles` property mapping, use instead:
- Scopes: `openid profile email jellyfin-roles`
- Role Claim Path: `roles`

#### Role Mappings Tab

Click **+ Add Role Mapping** for each role:

**Admin role:**

| Field               | Value              |
|----------------------|--------------------|
| Role Name            | `jellyfin-admins`  |
| Priority             | `100`              |
| Administrator        | checked            |
| All Libraries        | checked            |
| Playback             | checked            |
| Remote Access        | checked            |
| Transcoding          | checked            |
| Live TV              | checked            |
| Delete Content       | checked            |
| Collections          | checked            |
| Subtitles            | checked            |

**Standard user role:**

| Field               | Value                                         |
|----------------------|-----------------------------------------------|
| Role Name            | `jellyfin-users`                              |
| Priority             | `50`                                          |
| Administrator        | unchecked                                     |
| All Libraries        | unchecked                                     |
| Libraries            | Select: Movies, TV Shows, Music (your choice) |
| Playback             | checked                                       |
| Remote Access        | checked                                       |
| Transcoding          | checked                                       |

**Kids role:**

| Field               | Value                        |
|----------------------|------------------------------|
| Role Name            | `jellyfin-kids`              |
| Priority             | `10`                         |
| Administrator        | unchecked                    |
| All Libraries        | unchecked                    |
| Libraries            | Select: Kids                 |
| Playback             | checked                      |
| Remote Access        | unchecked                    |
| Transcoding          | checked                      |
| Max Parental Rating  | `7` (PG)                    |

> If you used the custom property mapping, use `admins`, `users`, `kids` as role names
> (without the `jellyfin-` prefix, since the mapping strips it).

#### General Tab

| Field                             | Value            |
|-----------------------------------|------------------|
| Default Provider                  | `authentik`      |
| Default Role                      | `jellyfin-users` |
| Auto-create users                 | checked          |
| Migrate local users to SSO        | unchecked (opt-in — enable if you want existing local accounts switched to SSO auth on first login) |
| Sync display name from OIDC token | unchecked (opt-in — enable to keep Jellyfin account names in sync with Authentik display names) |

Click **Save Configuration**.

### 3.3 Add Login Button

Go to **Jellyfin Admin Dashboard → General → Branding**

Retrieve the HTML snippet from the plugin API:
```bash
curl http://localhost:8096/sso/OIDC/BrandingSnippet
```

Copy the value of the `Html` field from the response and paste it into the **Login disclaimer** field. It contains a styled `<a>` link for each enabled provider — no JavaScript or CSP issues.

> **Note:** Do not paste a `<script>` tag — browsers block script injection via Login Disclaimer due to Content Security Policy. Use the static HTML snippet from `BrandingSnippet` instead.

---

## 4. Test the Flow

1. Open Jellyfin in a private browser window: http://localhost:8096
2. You should see a **"Sign in with Authentik"** button above the login form
3. Click it — you're redirected to Authentik
4. Log in with an Authentik user that belongs to one of the `jellyfin-*` groups
5. After consent, you're redirected back to Jellyfin and logged in
6. Check the user's library access in **Admin Dashboard > Users** — it should match the role mapping

---

## 5. Troubleshooting

### "Failed to contact identity provider"
- Verify the Authority URL is reachable from the Jellyfin container
- Test: `docker exec jellyfin curl -s <authority-url>/.well-known/openid-configuration`

### User created but no library access
- Check the Jellyfin logs for `Applied RBAC for user` messages — they show the matched roles and whether admin was set
- Verify the role claim path matches your Authentik setup
- Test the token content: decode the ID token at jwt.io and check the `groups` claim

### Admin flag not appearing after SSO login
- The plugin uses `UpdatePolicyAsync` to set permissions before the session is created — check logs for `Applied RBAC for user ...: admin=True`
- If you see the log but admin still doesn't show, restart Jellyfin to clear any stale cache

### Disabled user can still log in
- As of v1.0.5, disabled Jellyfin accounts are blocked at SSO login with a `403 Forbidden` response
- Check the Jellyfin logs for `User '...' is disabled in Jellyfin`

### "Invalid or expired authentication state"
- The OIDC state has a 10-minute TTL — try again
- Make sure the redirect URI in Authentik exactly matches what the plugin generates

### Redirect URI mismatch
- The plugin generates: `{scheme}://{host}/sso/OIDC/Callback/{providerId}`
- Add the exact URL to Authentik's provider Redirect URIs field
- If behind a reverse proxy, ensure the `X-Forwarded-Proto` and `Host` headers are passed

### Groups not in the ID token
- Edit the Authentik provider and enable **"Include claims in ID token"**
- Without this, claims only appear in the access token and userinfo endpoint
