# Azure AD (Microsoft Entra ID) + Jellyfin OIDC RBAC Setup

## 1. Create an App Registration

1. Go to [portal.azure.com](https://portal.azure.com) → **Azure Active Directory** → **App registrations** → **New registration**
2. Name: `Jellyfin`
3. Supported account types: **Accounts in this organizational directory only** (single tenant)
4. Redirect URI: **Web** → `https://jellyfin.example.com/sso/OIDC/Callback/azure`
5. Click **Register** and copy the **Application (client) ID** and **Directory (tenant) ID**

## 2. Create a Client Secret

Go to **Certificates & secrets** → **New client secret** → copy the **Value** (shown once only).

## 3. Configure Group or Role Claims

### Option A — App Roles (recommended)

Go to **App roles** → **Create app role**:

| Field        | Value                    |
|--------------|--------------------------|
| Display name | `Jellyfin Admin`         |
| Allowed member types | Users/Groups   |
| Value        | `jellyfin-admin`         |
| Description  | `Jellyfin administrator` |

Repeat for each role (`jellyfin-users`, `jellyfin-kids`, etc.).

Then assign users: **Enterprise applications** → `Jellyfin` → **Users and groups** → **Add user/group** → select user → select role.

App roles appear in the token as the `roles` claim.

### Option B — Group Claims

Go to **Token configuration** → **Add groups claim** → select **Security groups** → Save.

> **Note:** Azure sends group **object IDs** (GUIDs), not display names. Use the group's object ID as the `RoleName` in the plugin's role mappings.

## 4. Configure the Plugin

| Field              | Value                                                                        |
|--------------------|------------------------------------------------------------------------------|
| Provider ID        | `azure`                                                                      |
| Display Name       | `Microsoft`                                                                  |
| Authority URL      | `https://login.microsoftonline.com/{tenant-id}/v2.0`                        |
| Client ID          | *(Application (client) ID)*                                                  |
| Client Secret      | *(client secret value)*                                                      |
| Scopes             | `openid profile email`                                                       |
| Role Claim Path    | `roles` (App Roles) or `groups` (Group Claims)                              |
| Username Claim     | `preferred_username`                                                         |

## 5. Role Mapping Examples

**With App Roles** — Role Names match the `Value` field set in the App role:
- Role Name: `jellyfin-admin`
- Role Name: `jellyfin-users`

**With Group Claims** — Role Names are the group object ID GUIDs from Azure:
- Role Name: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`

## Troubleshooting

### Groups not appearing in the token
- Token configuration → Groups claim must be enabled
- For App Roles: ensure the user has been assigned the role under Enterprise applications

### `preferred_username` is an email address
Azure AD's `preferred_username` is typically `user@domain.com`. If you want to use just the username part, switch the **Username Claim** to `unique_name` or keep the email and ensure Jellyfin usernames match.

### Multi-tenant applications
Change the Authority URL to `https://login.microsoftonline.com/common/v2.0` and set Supported account types to **Accounts in any organizational directory**.
