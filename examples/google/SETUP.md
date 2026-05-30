# Google + Jellyfin OIDC RBAC Setup

> **Role/group limitation:** Standard Google consumer accounts do not include group memberships in OIDC tokens. Role-based library access works best with **Google Workspace** (paid), which can emit group claims. For personal Google accounts, use the **Default Role** in the plugin to grant a baseline level of access to all authenticated users.

## 1. Create OAuth 2.0 Credentials

1. Go to [console.cloud.google.com](https://console.cloud.google.com) → **APIs & Services** → **Credentials**
2. Click **Create Credentials** → **OAuth client ID**
3. Application type: **Web application**
4. Name: `Jellyfin`
5. Authorised redirect URIs: `https://jellyfin.example.com/sso/OIDC/Callback/google`
6. Click **Create** and copy the **Client ID** and **Client secret**

> If prompted, configure the OAuth consent screen first. Set User type to **Internal** (Google Workspace) or **External** (personal accounts, requires verification for >100 users).

## 2. Configure the Plugin

| Field              | Value                                      |
|--------------------|--------------------------------------------|
| Provider ID        | `google`                                   |
| Display Name       | `Google`                                   |
| Authority URL      | `https://accounts.google.com`              |
| Client ID          | *(OAuth client ID)*                        |
| Client Secret      | *(OAuth client secret)*                    |
| Scopes             | `openid profile email`                     |
| Role Claim Path    | `groups` (Workspace only — see below)      |
| Username Claim     | `email`                                    |

## 3. Role Mapping

### Personal Google accounts (no group claims)

Set a **Default Role** in the plugin's General tab. All users who successfully authenticate with Google will receive that role's permissions.

### Google Workspace

Google Workspace can include group memberships in tokens via a **custom attribute** or the **Admin SDK**. The most practical approach is a Cloud Run function or similar that adds group info to the token — this is complex and outside the scope of this guide.

A simpler alternative: create one role mapping per user email using the `email` claim as the Role Claim Path:
- Role Claim Path: `email`
- Role Name: `admin@yourdomain.com` → admin mapping
- Role Name: `alice@yourdomain.com` → standard user mapping

## Troubleshooting

### "Access blocked: This app's request is invalid"
- The redirect URI in Google Cloud Console must exactly match what the plugin generates: `https://jellyfin.example.com/sso/OIDC/Callback/google`

### Users from any Google account can log in
- Set the OAuth consent screen to **Internal** to restrict to your Workspace organisation
- Or set a **Default Role** with minimal permissions and control access via IdP-level login restrictions

### `preferred_username` claim is missing
Google does not emit `preferred_username`. Use `email` as the Username Claim — Jellyfin usernames will be set to the user's email address.
