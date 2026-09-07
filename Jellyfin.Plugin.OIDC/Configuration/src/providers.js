// Provider-card rendering (Connection / Claim mapping / Appearance / Advanced & security) and
// its round-trip back into config objects.
import { el, esc, emptyState, gval, gchk } from './dom.js';
import { fld } from './fields.js';
import { cfg } from './state.js';

// Matches OidcProviderConfig.ButtonColor / ProviderButtonAssets.DefaultButtonColor.
export var DEFAULT_BUTTON_COLOR = '#4285F4';

// Suggests a per-provider env var name so the placeholder never nudges two providers toward
// reusing the same variable (each provider must resolve to its own secret).
export function envVarSuggestion(p) {
    var slug = (p.ProviderId || 'PROVIDER').toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
    return (slug || 'PROVIDER') + '_CLIENT_SECRET';
}

// Bundled Button Icon keys - must match Services/KnownProviderIcons.Keys on the server.
export var ICON_KEYS = ['authentik', 'keycloak', 'google', 'microsoft', 'okta', 'auth0', 'discord', 'github'];

export function iconIsCustom(v) {
    return !!v && ICON_KEYS.indexOf(v) === -1;
}

var ICON_LABELS = { auth0: 'Auth0', github: 'GitHub' };

// One-shot "prefill for <IdP>" helper on the provider card. Sets claim paths / scopes /
// icon only - never Authority or client credentials. Values follow each IdP's common
// convention; some (Google/Okta groups, Auth0 roles) still need IdP-side config.
export var PROVIDER_PRESETS = {
    keycloak:  { label: 'Keycloak',            roleClaim: 'realm_access.roles', usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'keycloak'  },
    authentik: { label: 'Authentik',           roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'authentik' },
    authelia:  { label: 'Authelia',            roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: ''          },
    entra:     { label: 'Microsoft Entra ID',  roleClaim: 'roles',             usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'microsoft' },
    google:    { label: 'Google Workspace',    roleClaim: 'groups',             usernameClaim: 'email',              scopes: 'openid profile email',        icon: 'google'    },
    okta:      { label: 'Okta',                roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: 'okta'      },
    auth0:     { label: 'Auth0',               roleClaim: '',                  usernameClaim: 'nickname',           scopes: 'openid profile email',        icon: 'auth0'     }
};

export function presetField(idx) {
    var opts = el('option', { value: '' }, '- choose an IdP -');
    Object.keys(PROVIDER_PRESETS).forEach(function (k) {
        opts += el('option', { value: k }, esc(PROVIDER_PRESETS[k].label));
    });
    return el('div', { class: 'oidc-field full' },
        el('label', { for: 'prov_preset_' + idx }, 'Prefill for ' +
            el('span', { class: 'oidc-hint' }, '(sets claims / scopes / icon - you still enter the Issuer URL &amp; client credentials)')) +
        el('select', { id: 'prov_preset_' + idx }, opts));
}

export function iconField(idx, cur) {
    var custom = iconIsCustom(cur);
    var opts = el('option', { value: 'none', selected: !cur }, 'None');
    ICON_KEYS.forEach(function (k) {
        var label = ICON_LABELS[k] || (k.charAt(0).toUpperCase() + k.slice(1));
        opts += el('option', { value: k, selected: cur === k }, label);
    });
    opts += el('option', { value: 'custom', selected: custom }, 'Custom (image)');
    return el('div', { class: 'oidc-field full' },
        el('label', { for: 'prov_icon_' + idx }, 'Button Icon') +
        el('select', { is: 'emby-select', id: 'prov_icon_' + idx }, opts) +
        el('textarea', {
            id: 'prov_icon_svg_' + idx,
            placeholder: 'Paste <svg>…</svg> or a data:image/… URI, or pick a file below',
            class: 'oidc-mono-box oidc-mt-sm' + (custom ? '' : ' oidc-hidden')
        }, esc(custom ? cur : '')) +
        el('input', {
            type: 'file', id: 'prov_icon_file_' + idx,
            accept: '.svg,.png,.jpg,.jpeg,.gif,.webp,image/svg+xml,image/png,image/jpeg,image/gif,image/webp',
            class: 'oidc-mt-sm' + (custom ? '' : ' oidc-hidden')
        }));
}

// One field group inside a provider card, rendered as a <details>. `open` decides the
// initial state (Connection opens only when the provider isn't configured yet); every
// field stays in the DOM either way, so collectProviders() is unaffected.
export function provGroup(title, hint, inner, open) {
    var head = esc(title) + (hint ? ' ' + el('span', { class: 'oidc-hint' }, esc(hint)) : '');
    return el('details', { class: 'oidc-section', open: !!open },
        el('summary', null, head) + el('div', { class: 'oidc-grid' }, inner));
}

// Host portion of an Authority URL, for the provider card header. Falls back to a
// scheme/path strip when the value isn't yet a valid absolute URL.
export function authorityHost(url) {
    if (!url) return '';
    try {
        return new URL(url).host;
    } catch (e) {
        return String(url).replace(/^[a-z][a-z0-9+.-]*:\/\//i, '').split('/')[0];
    }
}

// The URL to register at the IdP as backchannel_logout_uri for this provider.
export function backchannelLogoutUrl(p) {
    var base = (p.ServerBaseUrl || '').replace(/\/+$/, '');
    if (!base) {
        try { base = ApiClient.serverAddress().replace(/\/+$/, ''); } catch (e) { base = ''; }
    }
    return (base || '(your server URL)') + '/sso/OIDC/BackchannelLogout/' + encodeURIComponent(p.ProviderId || '');
}

export function renderProviders(view) {
    var container = view.querySelector('#providerList');
    container.innerHTML = '';
    if (!cfg.Providers.length) {
        container.innerHTML = emptyState(
            "No providers configured yet - users can't sign in with SSO until you add one.");
        return;
    }
    cfg.Providers.forEach(function (p, idx) {
        var card = document.createElement('div');
        card.className = 'oidc-card';

        // A configured provider hides the prefill and opens collapsed; a fresh one opens on Connection.
        var configured = !!(p.ProviderId && (p.PinnedIssuer || p.Authority) && p.ClientId);
        if (p.Enabled === false) card.className += ' oidc-disabled';

        var connection =
            (configured ? '' : presetField(idx)) +
            fld('Provider ID', 'text', 'prov_id_' + idx, p.ProviderId, 'Unique identifier (e.g. keycloak)') +
            fld('Display Name', 'text', 'prov_name_' + idx, p.DisplayName, 'Shown on login button') +
            el('div', { class: 'oidc-field full' },
                el('label', { for: 'prov_pinnedissuer_' + idx }, 'Issuer URL') +
                el('input', {
                    is: 'emby-input', type: 'text', id: 'prov_pinnedissuer_' + idx,
                    value: p.PinnedIssuer || p.Authority || '',
                    placeholder: 'https://idp.example.com/realms/myrealm',
                    'data-verified': p.PinnedIssuer || '',
                    autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false'
                }) +
                el('span', { class: 'oidc-hint' }, 'Must exactly match the issuer your IdP returns from discovery. ' +
                    (p.PinnedIssuer
                        ? 'This value is pinned - editing it requires re-running Test Connection before you can save.'
                        : 'Run Test Connection to pin it.'))) +
            fld('Client ID', 'text', 'prov_clientid_' + idx, p.ClientId, '') +
            fld('Client Secret', 'password', 'prov_secret_' + idx, p.ClientSecret,
                'Or reference an env var unique to THIS provider, e.g. ${' + envVarSuggestion(p) + '}') +
            fld('Client Secret File', 'text', 'prov_secretfile_' + idx, p.ClientSecretFile,
                'Optional: path to a file unique to THIS provider (e.g. a mounted Docker/K8s secret) - overrides Client Secret above') +
            fld('Scopes', 'text', 'prov_scopes_' + idx, p.Scopes || 'openid profile email', '');

        var claims =
            fld('Role Claim Path', 'text', 'prov_roleclaim_' + idx, p.RoleClaim || 'groups', 'e.g. groups or realm_access.roles') +
            fld('Username Claim', 'text', 'prov_userclaim_' + idx, p.UsernameClaim || 'preferred_username', '') +
            fld('Display Name Claim', 'text', 'prov_displayclaim_' + idx, p.DisplayNameClaim || 'name', '') +
            fld('Email Claim', 'text', 'prov_emailclaim_' + idx, p.EmailClaim || 'email', '') +
            fld('Picture Claim', 'text', 'prov_pictureclaim_' + idx, p.PictureClaim || 'picture', 'e.g. picture') +
            el('div', { class: 'oidc-field full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_syncimage_' + idx, is: 'emby-checkbox', checked: p.SyncProfileImage !== false }) +
                    ' Sync profile image')) +
            el('div', { class: 'oidc-field full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_syncdisplay_' + idx, is: 'emby-checkbox', checked: p.SyncDisplayName === true }) +
                    ' Sync display name on login') +
                el('span', { class: 'oidc-hint oidc-ml-lg' }, 'This <strong>renames the Jellyfin account</strong> to match the Display Name Claim on every login.'));

        var appearance =
            el('div', { class: 'oidc-field' },
                el('label', { for: 'prov_color_' + idx }, 'Button Color') +
                el('div', { class: 'oidc-inline-row' },
                    el('input', { type: 'color', id: 'prov_color_' + idx, value: p.ButtonColor || DEFAULT_BUTTON_COLOR }) +
                    el('button', { type: 'button', class: 'oidc-btn-secondary', 'data-action': 'reset-color', 'data-idx': idx }, 'Reset to default'))) +
            iconField(idx, p.ButtonIcon || '');

        var advanced =
            fld('Additional Params', 'text', 'prov_params_' + idx, p.AdditionalParameters || '', 'key=val&key2=val2', true) +
            fld('Server Base URL (override)', 'text', 'prov_baseurl_' + idx, p.ServerBaseUrl || '', 'Optional: https://jellyfin.example.com - overrides auto-detected redirect_uri host', true) +
            el('div', { class: 'oidc-field full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_strict_access_' + idx, is: 'emby-checkbox', checked: p.StrictAccessTokenValidation !== false }) +
                    ' Strict access token validation') +
                el('span', { class: 'oidc-hint oidc-ml-lg' }, 'Only applies when the IdP issues JWT access tokens (e.g. Keycloak). Opaque access tokens (Google, default Authelia) are skipped automatically and unaffected by this setting. Uncheck if your IdP signs access tokens with a different key than the JWKS endpoint advertises.')) +
            el('div', { class: 'oidc-field full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_allow_loopback_' + idx, is: 'emby-checkbox', checked: p.AllowLoopbackAuthority === true }) +
                    ' Allow loopback Authority') +
                el('span', { class: 'oidc-hint oidc-ml-lg' }, 'By default, an Authority resolving to a loopback address (127.0.0.1, ::1) is blocked. Enable this only if your IdP is intentionally hosted at loopback.')) +
            el('div', { class: 'oidc-field full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_allow_linklocal_' + idx, is: 'emby-checkbox', checked: p.AllowLinkLocalAuthority === true }) +
                    ' Allow link-local Authority') +
                el('span', { class: 'oidc-hint oidc-ml-lg' }, 'By default, an Authority resolving to a link-local address (169.254.x.x, fe80::) is blocked. Enable this only if your IdP is intentionally hosted at a link-local address.')) +
            el('div', { class: 'oidc-field full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_trusted_email_link_' + idx, is: 'emby-checkbox', checked: p.TrustedForEmailLinking === true }) +
                    ' Trusted for email-based account linking') +
                el('span', { class: 'oidc-hint oidc-ml-lg' }, 'Only matters when "Link existing users by verified email" is on (General tab). Enable only for an IdP you fully control - a verified email from here will be trusted to link a login to an existing account. Leave off for any provider where a user can set their own email address. Never links to an administrator account.')) +
            el('input', { type: 'hidden', id: 'prov_discovery_' + idx, value: p.Authority || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedauthority_' + idx, value: p.PinnedAuthority || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedtoken_' + idx, value: p.PinnedTokenEndpoint || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedjwks_' + idx, value: p.PinnedJwksUri || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinneduserinfo_' + idx, value: p.PinnedUserInfoEndpoint || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedauthorize_' + idx, value: p.PinnedAuthorizeEndpoint || '' }) +
            el('div', { class: 'oidc-field full oidc-mt-md' },
                el('label', { class: 'oidc-label-strong' }, 'Endpoint Pins') +
                el('div', { class: 'oidc-hint', 'data-pin-status': idx },
                    p.PinnedIssuer
                        ? 'Pinned via Test Connection - token endpoint, JWKS URI &amp; userinfo endpoint are locked to the values returned for this issuer.'
                        : 'Not yet pinned - endpoints will be trusted on first login (TOFU) unless you run Test Connection first.')) +
            (p.ProviderId
                ? el('div', { class: 'oidc-field full oidc-mt-md' },
                    el('label', { class: 'oidc-label-strong' }, 'Back-channel logout URL ' +
                        el('span', { class: 'oidc-hint' }, '- register as the client\'s <code>backchannel_logout_uri</code> so the IdP can revoke Jellyfin sessions')) +
                    el('div', { class: 'oidc-inline-row oidc-mt-sm' },
                        el('input', {
                            is: 'emby-input', type: 'text', id: 'prov_bclogout_' + idx, readonly: true,
                            value: backchannelLogoutUrl(p), class: 'oidc-mono-flex'
                        }) +
                        el('button', { type: 'button', class: 'oidc-btn-secondary', 'data-copy': 'prov_bclogout_' + idx }, 'Copy')))
                : '');

        var host = authorityHost(p.PinnedIssuer || p.Authority);
        card.innerHTML = el('div', { class: 'oidc-card-head' },
            el('h4', null, esc(p.DisplayName || 'New Provider')) +
            (host ? el('span', { class: 'oidc-card-sub' }, esc(host)) : '') +
            el('label', { class: 'oidc-enable-toggle' },
                el('span', null, 'Enabled') +
                el('input', { type: 'checkbox', id: 'prov_enabled_' + idx, checked: p.Enabled !== false }))) +
            provGroup('Connection', 'provider id, endpoint & client credentials', connection, !configured) +
            provGroup('Claim mapping', 'role, username, display name & avatar', claims, false) +
            provGroup('Appearance', 'login button colour & icon', appearance, false) +
            provGroup('Advanced & security', 'redirect host, token validation, network guards, endpoint pins', advanced, false) +
            el('div', { class: 'oidc-row-actions' },
                el('button', {
                    type: 'button', class: 'oidc-btn-secondary oidc-btn-icon', 'data-action': 'move-provider',
                    'data-dir': '-1', 'data-idx': idx, title: 'Move up (changes login-button order)', disabled: idx === 0
                }, '&#8593;') +
                el('button', {
                    type: 'button', class: 'oidc-btn-secondary oidc-btn-icon', 'data-action': 'move-provider',
                    'data-dir': '1', 'data-idx': idx, title: 'Move down (changes login-button order)',
                    disabled: idx === cfg.Providers.length - 1
                }, '&#8595;') +
                el('button', { type: 'button', class: 'oidc-btn-secondary', 'data-action': 'test-provider', 'data-idx': idx }, 'Test Connection') +
                el('button', { type: 'button', class: 'oidc-btn-remove', 'data-action': 'remove-provider', 'data-idx': idx }, 'Remove') +
                el('span', { class: 'oidc-test-result', 'data-idx': idx }));
        container.appendChild(card);
    });
}

export function collectIcon(view, idx) {
    var kind = gval(view, 'prov_icon_' + idx);
    if (kind === 'custom') return (gval(view, 'prov_icon_svg_' + idx) || '').trim();
    if (!kind || kind === 'none') return '';
    return kind;
}

export function collectProviders(view) {
    var result = [];
    view.querySelectorAll('#providerList .oidc-card').forEach(function (card, idx) {
        var issuerVal = gval(view, 'prov_pinnedissuer_' + idx);
        var issuerEl = view.querySelector('#prov_pinnedissuer_' + idx);
        // Set only by a successful Test Connection - the discovery document's own `issuer`.
        var verified = issuerEl ? (issuerEl.dataset.verified || '') : '';
        // Pinned: use the hidden discovery-URL mirror. Not-yet-pinned: the raw typed value, no pins (TOFU).
        var discoveryUrl = verified ? gval(view, 'prov_discovery_' + idx) : issuerVal;
        result.push({
            ProviderId: gval(view, 'prov_id_' + idx),
            DisplayName: gval(view, 'prov_name_' + idx),
            Authority: discoveryUrl,
            ClientId: gval(view, 'prov_clientid_' + idx),
            ClientSecret: gval(view, 'prov_secret_' + idx),
            ClientSecretFile: gval(view, 'prov_secretfile_' + idx),
            Scopes: gval(view, 'prov_scopes_' + idx),
            RoleClaim: gval(view, 'prov_roleclaim_' + idx),
            UsernameClaim: gval(view, 'prov_userclaim_' + idx),
            DisplayNameClaim: gval(view, 'prov_displayclaim_' + idx),
            EmailClaim: gval(view, 'prov_emailclaim_' + idx),
            PictureClaim: gval(view, 'prov_pictureclaim_' + idx),
            SyncProfileImage: gchk(view, 'prov_syncimage_' + idx),
            SyncDisplayName: gchk(view, 'prov_syncdisplay_' + idx),
            ButtonColor: gval(view, 'prov_color_' + idx),
            AdditionalParameters: gval(view, 'prov_params_' + idx),
            ServerBaseUrl: gval(view, 'prov_baseurl_' + idx),
            Enabled: gchk(view, 'prov_enabled_' + idx),
            StrictAccessTokenValidation: gchk(view, 'prov_strict_access_' + idx),
            AllowLoopbackAuthority: gchk(view, 'prov_allow_loopback_' + idx),
            AllowLinkLocalAuthority: gchk(view, 'prov_allow_linklocal_' + idx),
            TrustedForEmailLinking: gchk(view, 'prov_trusted_email_link_' + idx),
            // Only the Test-Connection-verified issuer, never the raw box.
            PinnedIssuer: verified,
            PinnedTokenEndpoint: gval(view, 'prov_pinnedtoken_' + idx),
            PinnedJwksUri: gval(view, 'prov_pinnedjwks_' + idx),
            PinnedUserInfoEndpoint: gval(view, 'prov_pinneduserinfo_' + idx),
            PinnedAuthorizeEndpoint: gval(view, 'prov_pinnedauthorize_' + idx),
            // Hidden, written only by a successful Test Connection, never from the live Issuer box.
            PinnedAuthority: gval(view, 'prov_pinnedauthority_' + idx),
            ButtonIcon: collectIcon(view, idx)
        });
    });
    return result;
}
