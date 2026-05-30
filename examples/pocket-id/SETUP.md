# Pocket ID + Jellyfin OIDC RBAC Setup

[Pocket ID](https://github.com/pocket-id/pocket-id) is a self-hosted OIDC provider focused on passkey authentication.

## 1. Create an OIDC Client

1. Log in to your Pocket ID admin panel
2. Go to **OIDC Clients** → **Create client**
3. Name: `Jellyfin`
4. Callback URLs: `https://jellyfin.example.com/sso/OIDC/Callback/pocketid`
5. Save and copy the **Client ID** and **Client Secret**

## 2. Create User Groups

Go to **User Groups** → **Create group** and create groups matching your intended role mappings:

| Group Name       | Purpose                        |
|------------------|--------------------------------|
| `jellyfin-admin` | Full admin access              |
| `jellyfin-users` | Standard library access        |

Assign users to groups via **Users** → select user → **Groups**.

## 3. Configure the Plugin

| Field              | Value                                                    |
|--------------------|----------------------------------------------------------|
| Provider ID        | `pocketid`                                               |
| Display Name       | `Pocket ID`                                              |
| Authority URL      | `https://your-pocket-id-instance.com`                    |
| Client ID          | *(from Pocket ID)*                                       |
| Client Secret      | *(from Pocket ID)*                                       |
| Scopes             | `openid profile email`                                   |
| Role Claim Path    | `groups`                                                 |
| Username Claim     | `preferred_username`                                     |

## 4. Role Mapping

Role Names in the plugin match the Pocket ID group names exactly:

| Plugin Role Name   | Pocket ID Group    |
|--------------------|--------------------|
| `jellyfin-admin`   | `jellyfin-admin`   |
| `jellyfin-users`   | `jellyfin-users`   |

## Troubleshooting

### Groups not in the token
- Pocket ID includes groups in the `groups` claim by default when users are assigned to groups
- Verify by decoding the ID token at [jwt.io](https://jwt.io)

### Discovery endpoint unreachable
- Ensure Jellyfin can reach `https://your-pocket-id-instance.com/.well-known/openid-configuration`
- If running in Docker, use the internal network hostname rather than the external URL for the Authority
