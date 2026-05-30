# Authelia + Jellyfin OIDC RBAC Setup

[Authelia](https://www.authelia.com) is a self-hosted authentication and authorization server with full OIDC support.

## 1. Register the Jellyfin Client in Authelia

Add the following to your Authelia `configuration.yml` under `identity_providers.oidc.clients`:

```yaml
identity_providers:
  oidc:
    clients:
      - client_id: jellyfin
        client_name: Jellyfin
        client_secret: '$pbkdf2-sha512$310000$...'  # use authelia crypto hash generate
        public: false
        authorization_policy: two_factor  # or one_factor
        redirect_uris:
          - https://jellyfin.example.com/sso/OIDC/Callback/authelia
        scopes:
          - openid
          - profile
          - email
          - groups
        userinfo_signed_response_alg: none
        token_endpoint_auth_method: client_secret_post
```

Generate the hashed client secret:
```bash
authelia crypto hash generate pbkdf2 --variant sha512 --random --random.length 72
```

Use the **plaintext** value in the plugin config and the **hashed** value in `configuration.yml`.

## 2. Configure Group Claims

Authelia includes group memberships in the `groups` claim when the `groups` scope is requested. Groups are sourced from your configured user database (LDAP, file, etc.).

**File-based users** (`users_database.yml` example):
```yaml
users:
  alice:
    password: '...'
    groups:
      - jellyfin-admin
      - jellyfin-users
  bob:
    password: '...'
    groups:
      - jellyfin-users
```

**LDAP:** Groups are pulled from your LDAP directory automatically based on your `authentication_backend.ldap` configuration.

## 3. Configure the Plugin

| Field              | Value                                                          |
|--------------------|----------------------------------------------------------------|
| Provider ID        | `authelia`                                                     |
| Display Name       | `Authelia`                                                     |
| Authority URL      | `https://auth.example.com`                                     |
| Client ID          | `jellyfin`                                                     |
| Client Secret      | *(plaintext secret from step 1)*                               |
| Scopes             | `openid profile email groups`                                  |
| Role Claim Path    | `groups`                                                       |
| Username Claim     | `preferred_username`                                           |

## 4. Role Mapping Examples

| Plugin Role Name   | Authelia Group     |
|--------------------|--------------------|
| `jellyfin-admin`   | `jellyfin-admin`   |
| `jellyfin-users`   | `jellyfin-users`   |

## Troubleshooting

### "Token exchange failed"
- Verify `token_endpoint_auth_method: client_secret_post` is set — Authelia defaults to `client_secret_basic` in some versions
- Check Authelia logs for OIDC errors

### Groups claim is missing
- Ensure `groups` is listed in both the client's `scopes` and the plugin's Scopes field
- Check that the authenticated user belongs to at least one group in Authelia's user database

### Two-factor requirement
- Setting `authorization_policy: two_factor` requires users to complete 2FA at Authelia before being redirected back to Jellyfin
- Set to `one_factor` if you want password-only authentication

### Redirect URI mismatch
- The URI in `configuration.yml` must exactly match `{scheme}://{host}/sso/OIDC/Callback/authelia`
- Behind a reverse proxy, ensure `X-Forwarded-Proto` and `X-Forwarded-Host` headers are passed to Jellyfin
