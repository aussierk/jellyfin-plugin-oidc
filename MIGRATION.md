# Migrating Existing Jellyfin Users to OIDC

If you already have Jellyfin users with watch history, favorites, playlists, etc., you can move them onto OIDC SSO without losing data — as long as the OIDC username matches the existing Jellyfin username.

## How it works

When a user completes SSO for the first time, the plugin (`UserSyncService.SyncUserAsync`) calls `_userManager.GetUserByName(username)` using the value of the configured **Username Claim** (default: `preferred_username`).

- **Existing user found**: that user is reused. All their existing watch state, favorites, playlists, and per-user preferences are preserved.
- **No match**: a new user is created (when `AutoCreateUsers` is enabled).

The match is by **exact username**. Case sensitivity follows whatever `IUserManager.GetUserByName` does in your Jellyfin version (currently case-insensitive in 10.11.x).

## What gets preserved

- Watch history (resume positions, played status)
- Favorites
- Playlists
- Subtitle, audio language, and other per-user playback preferences
- Created watchlists / collections owned by the user

## What gets overwritten on every SSO login

The matched role mapping is re-applied each time the user logs in via SSO, so these fields are replaced by the role mapping values:

- `IsAdministrator`
- `EnableMediaPlayback`, `EnableRemoteAccess`, audio/video transcoding flags
- `EnableLiveTvAccess`, `EnableLiveTvManagement`
- `EnableContentDeletion`, `EnableCollectionManagement`, `EnableSubtitleManagement`
- `EnableAllFolders` and the list of enabled folders (library access)
- `MaxParentalRatingScore` (only when a role provides one)

Plan your role mappings to match the access you want users to have **before** they log in — otherwise their first SSO login can silently strip permissions or library access.

## Migration steps

1. **Identify the username mismatch (if any)**

   For each existing Jellyfin user, check what `preferred_username` (or whichever claim you configured) the IdP will emit for them. Authentik exposes this on the user's profile; Keycloak shows it under the user attributes.

2. **Align the names**

   If the OIDC username differs from the Jellyfin username, pick one to change:
   - **Rename the Jellyfin user** — Dashboard → Users → click the user → change Username → Save.
   - **Or change the IdP attribute** — set the user's `preferred_username` in the IdP to match the existing Jellyfin name. (Keycloak: under user attributes; Authentik: under the user's profile.)

3. **Create role mappings that grant the same access the user has today**

   Open Plugins → OIDC RBAC → Role Mappings. For each role/group used in your IdP, define a mapping that grants the same libraries and permissions the user has now. Use the **Test Connection** button on the provider card to confirm discovery succeeds before the first login.

4. **Add the user to the right IdP group(s)**

   Make sure each user is in the IdP group whose name matches a Role Mapping's `RoleName`. With Authentik this is Directory → Groups; with Keycloak it's Groups under the realm.

5. **Log in via SSO**

   The first SSO login finds the existing user by name, leaves their data alone, and applies the role mapping.

## Caveats

### Password login still works after migration

The plugin does **not** change the user's `AuthenticationProviderId` when an existing user logs in via SSO. The user can still sign in with their old Jellyfin password. If you want to lock that down, manually disable the user's password (Dashboard → Users → user → Password tab → set an empty / random password) or delete it via the API after migration.

### Duplicate accounts on username mismatch

If a user logs in via SSO before you've aligned usernames, the plugin will create a **second** Jellyfin user with the OIDC username, and the old account's data will not be visible to the new one. To recover, delete the new (empty) account and re-align the names before retrying.

### Display name claim is ignored on existing users

The configured **Display Name Claim** is read but never applied to existing users (or new ones — see `UserSyncService.cs:49-54`). The Jellyfin username is what's shown. If you need a different display name, set it manually in Jellyfin.

### Disabled users are re-enabled on SSO login

The plugin sets `IsDisabled = false` on every successful SSO login. If you've disabled a user in Jellyfin to lock them out, that won't survive an SSO login — remove them from the IdP group instead.
