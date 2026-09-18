// Provider-card rendering (Connection / Claim mapping / Appearance / Security) and its
// round-trip back into config objects.
import { el, esc, emptyState, gval, gchk } from './dom.js';
import { fld, chkWithDesc } from './fields.js';
import { cfg } from './state.js';
import { STRINGS } from './strings.js';

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

var ICON_LABELS = STRINGS.iconLabels;

// One-shot "prefill for <IdP>" helper on the provider card. Sets claim paths / scopes /
// icon only - never Authority or client credentials. Values follow each IdP's common
// convention; some (Google/Okta groups, Auth0 roles) still need IdP-side config.
export var PROVIDER_PRESETS = {
    keycloak:  { label: STRINGS.presetLabels.keycloak, roleClaim: 'realm_access.roles', usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'keycloak'  },
    authentik: { label: STRINGS.presetLabels.authentik, roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'authentik' },
    authelia:  { label: STRINGS.presetLabels.authelia, roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: ''          },
    pocketid:  { label: STRINGS.presetLabels.pocketid, roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: ''          },
    entra:     { label: STRINGS.presetLabels.entra, roleClaim: 'roles',             usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'microsoft' },
    google:    { label: STRINGS.presetLabels.google, roleClaim: 'groups',             usernameClaim: 'email',              scopes: 'openid profile email',        icon: 'google'    },
    okta:      { label: STRINGS.presetLabels.okta, roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: 'okta'      },
    auth0:     { label: STRINGS.presetLabels.auth0, roleClaim: '',                  usernameClaim: 'nickname',           scopes: 'openid profile email',        icon: 'auth0'     }
};

export function presetField(idx) {
    var opts = el('option', { value: '' }, STRINGS.provider.prefillChooseOption);
    Object.keys(PROVIDER_PRESETS).forEach(function (k) {
        opts += el('option', { value: k }, esc(PROVIDER_PRESETS[k].label));
    });
    return el('div', { class: 'selectContainer full' },
        el('label', { for: 'prov_preset_' + idx }, STRINGS.provider.prefillLabelPrefix +
            el('span', { class: 'fieldDescription' }, STRINGS.provider.prefillHintHtml)) +
        el('select', { is: 'emby-select', id: 'prov_preset_' + idx }, opts));
}

export function iconField(idx, cur) {
    var custom = iconIsCustom(cur);
    var opts = el('option', { value: 'none', selected: !cur }, STRINGS.provider.iconNone);
    ICON_KEYS.forEach(function (k) {
        var label = ICON_LABELS[k] || (k.charAt(0).toUpperCase() + k.slice(1));
        opts += el('option', { value: k, selected: cur === k }, label);
    });
    opts += el('option', { value: 'custom', selected: custom }, STRINGS.provider.iconCustom);
    return el('div', { class: 'selectContainer full' },
        el('label', { for: 'prov_icon_' + idx }, STRINGS.provider.buttonIconLabel) +
        el('select', { is: 'emby-select', id: 'prov_icon_' + idx }, opts) +
        el('input', { type: 'hidden', id: 'prov_icon_svg_' + idx, value: custom ? cur : '' }) +
        el('input', {
            type: 'file', id: 'prov_icon_file_' + idx,
            accept: '.svg,.png,.jpg,.jpeg,.gif,.webp,image/svg+xml,image/png,image/jpeg,image/gif,image/webp',
            class: 'oidc-mt-sm' + (custom ? '' : ' oidc-hidden')
        }) +
        el('span', { class: 'fieldDescription', 'data-icon-status': idx }, custom && cur ? STRINGS.provider.iconCustomSet : ''));
}

// One field group inside a provider card, rendered as a <details>. `open` decides the
// initial state (Connection opens only when the provider isn't configured yet); every
// field stays in the DOM either way, so collectProviders() is unaffected.
export function provGroup(title, hint, inner, open) {
    var head = esc(title) + (hint ? ' ' + el('span', { class: 'fieldDescription' }, esc(hint)) : '');
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
export function backchannelLogoutUrl(p, serverBaseUrl) {
    var base = (serverBaseUrl || '').replace(/\/+$/, '');
    if (!base) {
        try { base = ApiClient.serverAddress().replace(/\/+$/, ''); } catch (e) { base = ''; }
    }
    return (base || '(your server URL)') + '/sso/OIDC/BackchannelLogout/' + encodeURIComponent(p.ProviderId || '');
}

export function renderProviders(view) {
    var container = view.querySelector('#providerList');
    container.innerHTML = '';
    if (!cfg.Providers.length) {
        container.innerHTML = emptyState(STRINGS.providersTab.emptyState);
        return;
    }
    cfg.Providers.forEach(function (p, idx) {
        var card = document.createElement('div');
        card.className = 'oidc-item-card';

        // A configured provider hides the prefill and opens collapsed; a fresh one opens on Connection.
        var configured = !!(p.ProviderId && (p.PinnedIssuer || p.Authority) && p.ClientId);
        if (p.Enabled === false) card.className += ' oidc-disabled';

        var connection =
            (configured ? '' : presetField(idx)) +
            fld(STRINGS.provider.providerIdLabel, 'text', 'prov_id_' + idx, p.ProviderId, STRINGS.provider.providerIdPlaceholder) +
            fld(STRINGS.provider.displayNameLabel, 'text', 'prov_name_' + idx, p.DisplayName, STRINGS.provider.displayNamePlaceholder) +
            el('div', { class: 'inputContainer full' },
                el('input', {
                    is: 'emby-input', type: 'text', id: 'prov_pinnedissuer_' + idx,
                    value: p.PinnedIssuer || p.Authority || '',
                    label: STRINGS.provider.issuerUrlLabel,
                    placeholder: STRINGS.provider.issuerUrlPlaceholder,
                    'data-verified': p.PinnedIssuer || '',
                    autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false'
                }) +
                el('span', { class: 'fieldDescription' }, STRINGS.provider.issuerHintPrefix +
                    (p.PinnedIssuer
                        ? STRINGS.provider.issuerHintPinned
                        : STRINGS.provider.issuerHintUnpinned))) +
            fld(STRINGS.provider.clientIdLabel, 'text', 'prov_clientid_' + idx, p.ClientId, '') +
            fld(STRINGS.provider.clientSecretLabel, 'password', 'prov_secret_' + idx, p.ClientSecret,
                STRINGS.provider.clientSecretPlaceholderPrefix + envVarSuggestion(p) + '}') +
            fld(STRINGS.provider.clientSecretFileLabel, 'text', 'prov_secretfile_' + idx, p.ClientSecretFile,
                STRINGS.provider.clientSecretFilePlaceholder) +
            fld(STRINGS.provider.scopesLabel, 'text', 'prov_scopes_' + idx, p.Scopes || 'openid profile email', '') +
            fld(STRINGS.provider.additionalParamsLabel, 'text', 'prov_params_' + idx, p.AdditionalParameters || '', STRINGS.provider.additionalParamsPlaceholder, true) +
            (p.ProviderId
                ? el('div', { class: 'inputContainer full' },
                    el('input', {
                        is: 'emby-input', type: 'text', id: 'prov_bclogout_' + idx, readonly: true,
                        value: backchannelLogoutUrl(p, cfg.ServerBaseUrl),
                        label: STRINGS.provider.backchannelLogoutLabel, class: 'oidc-mono-flex'
                    }) +
                    el('button', {
                        is: 'emby-button', type: 'button', class: 'oidc-btn-secondary oidc-mt-sm', 'data-copy': 'prov_bclogout_' + idx
                    }, STRINGS.provider.copyBtn) +
                    el('span', { class: 'fieldDescription' }, STRINGS.provider.backchannelLogoutHintHtml))
                : '');

        var claims =
            fld(STRINGS.provider.roleClaimLabel, 'text', 'prov_roleclaim_' + idx, p.RoleClaim || 'groups', STRINGS.provider.roleClaimPlaceholder) +
            fld(STRINGS.provider.usernameClaimLabel, 'text', 'prov_userclaim_' + idx, p.UsernameClaim || 'preferred_username', '') +
            fld(STRINGS.provider.displayNameClaimLabel, 'text', 'prov_displayclaim_' + idx, p.DisplayNameClaim || 'name', '') +
            fld(STRINGS.provider.emailClaimLabel, 'text', 'prov_emailclaim_' + idx, p.EmailClaim || 'email', '') +
            fld(STRINGS.provider.pictureClaimLabel, 'text', 'prov_pictureclaim_' + idx, p.PictureClaim || 'picture', STRINGS.provider.pictureClaimPlaceholder) +
            el('div', { class: 'checkboxContainer full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_syncimage_' + idx, is: 'emby-checkbox', checked: p.SyncProfileImage !== false }) +
                    ' ' + el('span', null, STRINGS.provider.syncProfileImage))) +
            el('div', { class: 'checkboxContainer checkboxContainer-withDescription full' },
                el('label', null,
                    el('input', { type: 'checkbox', id: 'prov_syncdisplay_' + idx, is: 'emby-checkbox', checked: p.SyncDisplayName === true }) +
                    ' ' + el('span', null, STRINGS.provider.syncDisplayName)) +
                el('div', { class: 'fieldDescription' }, STRINGS.provider.syncDisplayNameDescHtml));

        var appearance =
            el('div', { class: 'inputContainer' },
                el('label', { for: 'prov_color_' + idx }, STRINGS.provider.buttonColorLabel) +
                el('div', { class: 'oidc-inline-row' },
                    el('input', { type: 'color', id: 'prov_color_' + idx, value: p.ButtonColor || DEFAULT_BUTTON_COLOR }) +
                    el('button', { is: 'emby-button', type: 'button', class: 'oidc-btn-secondary', 'data-action': 'reset-color', 'data-idx': idx }, STRINGS.provider.resetToDefaultBtn))) +
            iconField(idx, p.ButtonIcon || '');

        var securityToggles = [
            { id: 'prov_strict_access_', checked: p.StrictAccessTokenValidation !== false, label: STRINGS.provider.strictAccessValidation, desc: STRINGS.provider.strictAccessValidationDesc },
            { id: 'prov_allow_loopback_', checked: p.AllowLoopbackAuthority === true, label: STRINGS.provider.allowLoopback, desc: STRINGS.provider.allowLoopbackDesc },
            { id: 'prov_allow_linklocal_', checked: p.AllowLinkLocalAuthority === true, label: STRINGS.provider.allowLinkLocal, desc: STRINGS.provider.allowLinkLocalDesc },
            { id: 'prov_trusted_email_link_', checked: p.TrustedForEmailLinking === true, label: STRINGS.provider.trustedEmailLink, desc: STRINGS.provider.trustedEmailLinkDesc }
        ];
        var security =
            securityToggles.map(function (t) {
                return chkWithDesc(t.id + idx, t.label, t.desc, t.checked);
            }).join('') +
            el('input', { type: 'hidden', id: 'prov_discovery_' + idx, value: p.Authority || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedauthority_' + idx, value: p.PinnedAuthority || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedtoken_' + idx, value: p.PinnedTokenEndpoint || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedjwks_' + idx, value: p.PinnedJwksUri || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinneduserinfo_' + idx, value: p.PinnedUserInfoEndpoint || '' }) +
            el('input', { type: 'hidden', id: 'prov_pinnedauthorize_' + idx, value: p.PinnedAuthorizeEndpoint || '' });

        var host = authorityHost(p.PinnedIssuer || p.Authority);
        card.innerHTML = el('div', { class: 'oidc-card-head' },
            el('h4', null, esc(p.DisplayName || STRINGS.provider.newProviderName)) +
            (host ? el('span', { class: 'oidc-card-sub' }, esc(host)) : '') +
            el('label', { class: 'oidc-enable-toggle' },
                el('span', null, STRINGS.provider.enabledLabel) +
                el('input', { type: 'checkbox', id: 'prov_enabled_' + idx, checked: p.Enabled !== false }))) +
            provGroup(STRINGS.provider.connectionTitle, STRINGS.provider.connectionHint, connection, !configured) +
            provGroup(STRINGS.provider.claimMappingTitle, STRINGS.provider.claimMappingHint, claims, false) +
            provGroup(STRINGS.provider.appearanceTitle, STRINGS.provider.appearanceHint, appearance, false) +
            provGroup(STRINGS.provider.securityTitle, STRINGS.provider.securityHint, security, false) +
            el('div', { class: 'oidc-row-actions' },
                el('button', {
                    is: 'emby-button', type: 'button', class: 'oidc-btn-secondary oidc-btn-icon', 'data-action': 'move-provider',
                    'data-dir': '-1', 'data-idx': idx, title: STRINGS.provider.moveUpTitle, disabled: idx === 0
                }, '&#8593;') +
                el('button', {
                    is: 'emby-button', type: 'button', class: 'oidc-btn-secondary oidc-btn-icon', 'data-action': 'move-provider',
                    'data-dir': '1', 'data-idx': idx, title: STRINGS.provider.moveDownTitle,
                    disabled: idx === cfg.Providers.length - 1
                }, '&#8595;') +
                el('button', { is: 'emby-button', type: 'button', class: 'oidc-btn-secondary', 'data-action': 'test-provider', 'data-idx': idx }, STRINGS.provider.testConnectionBtn) +
                el('button', { is: 'emby-button', type: 'button', class: 'oidc-btn-remove', 'data-action': 'remove-provider', 'data-idx': idx }, STRINGS.common.removeBtn) +
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
    view.querySelectorAll('#providerList .oidc-item-card').forEach(function (card, idx) {
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
