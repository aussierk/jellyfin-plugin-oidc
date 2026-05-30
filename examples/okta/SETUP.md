# Okta + Jellyfin OIDC RBAC Setup

## 1. Create an OIDC Application

1. Go to your Okta Admin Console → **Applications** → **Create App Integration**
2. Sign-in method: **OIDC – OpenID Connect**
3. Application type: **Web Application**
4. App integration name: `Jellyfin`
5. Sign-in redirect URIs: `https://jellyfin.example.com/sso/OIDC/Callback/okta`
6. Sign-out redirect URIs: `https://jellyfin.example.com`
7. Assignments: select the groups or individuals who should have access
8. Click **Save** and copy the **Client ID** and **Client secret**

## 2. Add the Groups Claim to the Authorization Server

By default, Okta does not include group memberships in the token.

1. Go to **Security** → **API** → **Authorization Servers** → select `default`
2. Click the **Claims** tab → **Add Claim**

| Field         | Value              |
|---------------|--------------------|
| Name          | `groups`           |
| Include in    | ID Token           |
| Value type    | Groups             |
| Filter        | Matches regex `.*` (or restrict to specific groups) |
| Include in    | Any scope          |

3. Save.

> **Tip:** Filter the regex to only Jellyfin-related groups (e.g. `jellyfin-.*`) to keep the token small.

## 3. Add `groups` to the Scopes (optional)

If you want to request groups explicitly via scope:

1. **Authorization Servers** → `default` → **Scopes** → **Add Scope**
2. Name: `groups`, Display phrase: `Group memberships`

Then add `groups` to the plugin's Scopes field.

## 4. Configure the Plugin

| Field              | Value                                                          |
|--------------------|----------------------------------------------------------------|
| Provider ID        | `okta`                                                         |
| Display Name       | `Okta`                                                         |
| Authority URL      | `https://{your-okta-domain}/oauth2/default`                   |
| Client ID          | *(from Okta app)*                                              |
| Client Secret      | *(from Okta app)*                                              |
| Scopes             | `openid profile email groups`                                  |
| Role Claim Path    | `groups`                                                       |
| Username Claim     | `preferred_username`                                           |

Replace `{your-okta-domain}` with your Okta org domain (e.g. `dev-12345678.okta.com`).

If using a custom authorization server, replace `default` with its ID.

## 5. Role Mapping Examples

Create Okta groups that match your Role Mapping names:

| Okta Group       | Plugin Role Name   |
|------------------|--------------------|
| `jellyfin-admin` | `jellyfin-admin`   |
| `jellyfin-users` | `jellyfin-users`   |
| `jellyfin-kids`  | `jellyfin-kids`    |

Assign users to these groups in Okta: **Directory** → **Groups** → group → **Manage People**.

## Troubleshooting

### Groups claim is missing from the token
- Verify the claim was added to the correct authorization server
- Decode the ID token at [jwt.io](https://jwt.io) and check for the `groups` array

### Authority URL discovery fails
- Make sure the URL ends with `/oauth2/default` (or your custom server ID), not just the Okta domain
- Test: `curl https://{domain}/oauth2/default/.well-known/openid-configuration`

### Users can authenticate but have no library access
- Ensure the user is a member of a group that matches a plugin Role Mapping
- Check the Jellyfin logs for `Applied RBAC for user` and the matched roles list
