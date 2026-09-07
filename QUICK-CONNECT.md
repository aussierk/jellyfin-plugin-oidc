# Mobile & Native Apps (Quick Connect)

**The SSO login button only works in the browser-based Jellyfin Web client.** The button is
injected into the web login page and the flow finishes by writing credentials into the
browser's `localStorage`. Native apps (Android, iOS/Swiftfin, Android TV, etc.) render their
own login screen and keep credentials in native storage, so they never see the button and
can't consume that web session. This is a limitation of how Jellyfin exposes login to
plugins, not a bug.

To sign a native app in via SSO, the plugin bridges to Jellyfin's built-in **Quick Connect**:

```
Native app                 Browser                    Jellyfin Plugin           Identity Provider
   |                          |                            |                          |
   |-- tap Quick Connect      |                            |                          |
   |   (shows 6-digit code)   |                            |                          |
   |   ...polling...          |                            |                          |
   |                          |-- open QuickConnect link ->|-- OIDC authorize ------->|
   |                          |<-- login at IdP -----------|<------ callback + code --|
   |                          |                            |-- sync user + RBAC       |
   |                          |-- enter 6-digit code ----->|-- AuthorizeRequest ----->|
   |<-- authenticated, signed in --------------------------|                          |
```

## Setup

1. Enable **Quick Connect** in Jellyfin: *Admin Dashboard → General → Quick Connect →
   Enable*.
2. On the mobile/native app, open the login screen and choose **Quick Connect** - it shows a
   6-digit code and starts polling.
3. In any browser (on the phone or another device), open
   `https://jellyfin.example.com/sso/OIDC/QuickConnect/<providerId>`
   (the injected login page also shows a small *"Sign in a device … (Quick Connect)"* link
   for this).
4. Authenticate at your IdP as usual.
5. Enter the 6-digit code from step 2 and click **Authorize**.
6. The native app's poll completes and it signs in.

> Quick Connect codes are short-lived. Start the flow on the app first, then enter the code
> promptly. A mistyped code can be re-entered without repeating the IdP login.
