# Tinyauth + Jellyfin OIDC RBAC Setup

[Tinyauth](https://github.com/steveiliop56/tinyauth) is a lightweight self-hosted authentication proxy with OIDC provider support.

## 1. Configure Tinyauth as an OIDC Provider

Add the Jellyfin app to your Tinyauth configuration. In your `docker-compose.yml` or Tinyauth settings, add an OAuth2/OIDC app entry:

```yaml
# In tinyauth environment or config
OAUTH_CLIENTS: |
  [
    {
      "id": "jellyfin",
      "secret": "your-client-secret",
      "redirectUris": ["https://jellyfin.example.com/sso/OIDC/Callback/tinyauth"]
    }
  ]
```

Refer to the [Tinyauth documentation](https://github.com/steveiliop56/tinyauth) for the exact configuration format for your version, as it evolves quickly.

## 2. User and Group Setup

Tinyauth manages users via its own user store. Assign users to groups within Tinyauth — group memberships are emitted as the `groups` claim.

## 3. Configure the Plugin

| Field              | Value                                                    |
|--------------------|----------------------------------------------------------|
| Provider ID        | `tinyauth`                                               |
| Display Name       | `Tinyauth`                                               |
| Authority URL      | `https://your-tinyauth-instance.com`                     |
| Client ID          | `jellyfin`                                               |
| Client Secret      | *(from Tinyauth config)*                                 |
| Scopes             | `openid profile email`                                   |
| Role Claim Path    | `groups`                                                 |
| Username Claim     | `preferred_username`                                     |

## 4. Role Mapping

Role Names in the plugin match the group names configured in Tinyauth.

## Notes

- Tinyauth's OIDC provider feature is relatively new — check the [GitHub releases](https://github.com/steveiliop56/tinyauth/releases) for the minimum version that supports OIDC provider mode
- If groups are not available as a claim, use the **Default Role** in the plugin's General tab to assign a baseline role to all Tinyauth-authenticated users

## Troubleshooting

### Discovery endpoint not found
- Verify Tinyauth is running with OIDC provider mode enabled
- Test: `curl https://your-tinyauth-instance.com/.well-known/openid-configuration`

### Claims missing from token
- Decode the ID token at [jwt.io](https://jwt.io) to inspect the available claims
- Adjust the plugin's Role Claim Path and Username Claim to match what Tinyauth actually emits
