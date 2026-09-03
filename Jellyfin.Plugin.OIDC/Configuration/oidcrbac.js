var pluginId = 'e1c020c5-3972-4b7b-9538-ee4934cc902c';
var cfg = null;
var libs = {};

// Unsaved-changes tracking. `dirty` flips true on any edit and false after a load or a
// successful save; the sticky save bar and the beforeunload guard both read it.
var dirty = false;
var dirtyView = null;

function setDirty(v) {
    dirty = v;
    if (!dirtyView) return;
    var s = dirtyView.querySelector('#saveStatus');
    if (s) s.textContent = v ? '● Unsaved changes' : '';
    var btn = dirtyView.querySelector('#btnSave');
    if (btn) btn.classList.toggle('oidc-save-dirty', v);
}

function beforeUnloadGuard(e) {
    if (!dirty) return undefined;
    e.preventDefault();
    e.returnValue = '';
    return '';
}

// Matches OidcProviderConfig.ButtonColor / BrandingSnippetBuilder.DefaultButtonColor.
var DEFAULT_BUTTON_COLOR = '#4285F4';

// Markers fencing the plugin-managed block inside Branding (Login Disclaimer / Custom CSS).
// Kept in sync with BrandingSnippetBuilder on the server.
var HTML_START = '<!-- oidc-sso-buttons:start -->';
var HTML_END = '<!-- oidc-sso-buttons:end -->';
var CSS_START = '/* oidc-sso-buttons:start */';
var CSS_END = '/* oidc-sso-buttons:end */';

// Replaces the marked region in `text` with `block`. If the markers aren't present, appends
// `block`. An empty `block` removes the region. Returns `text` unchanged when there's nothing
// to do (no region and nothing to add).
function spliceRegion(text, startMarker, endMarker, block) {
    text = text || '';
    var s = text.indexOf(startMarker);
    var e = text.indexOf(endMarker);
    if (s !== -1 && e !== -1 && e > s) {
        var before = text.slice(0, s);
        var after = text.slice(e + endMarker.length);
        if (!block) {
            return (before + after).replace(/\n{3,}/g, '\n\n').trim();
        }
        return before + block + after;
    }
    if (!block) return text;
    return text.trim() ? text.trim() + '\n\n' + block : block;
}

function setBrandingStatus(view, installed) {
    var el = view.querySelector('#brandingStatus');
    if (el) el.textContent = installed ? 'Installed' : 'Not installed';
}

// Fetches the current snippet into the manual copy/paste boxes and reflects install status.
function loadBrandingSnippet(view) {
    ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/BrandingSnippet')).then(function (snip) {
        var h = view.querySelector('#brandingHtml');
        var c = view.querySelector('#brandingCss');
        if (h) h.value = (snip && snip.Html) || '';
        if (c) c.value = (snip && snip.Css) || '';
    }).catch(function () {});
    ApiClient.getNamedConfiguration('branding').then(function (b) {
        setBrandingStatus(view, ((b && b.LoginDisclaimer) || '').indexOf(HTML_START) !== -1);
    }).catch(function () {});
}

// Keeps the marked block in Branding in sync with the just-saved config. Called after the
// plugin configuration is persisted, so GET BrandingSnippet reflects the new provider list.
function syncBranding(view) {
    var manage = gchk(view, 'manageLoginButtonBranding');
    var enabledCount = (cfg.Providers || []).filter(function (p) { return p.Enabled !== false; }).length;

    return Promise.all([
        ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/BrandingSnippet')),
        ApiClient.getNamedConfiguration('branding')
    ]).then(function (res) {
        var snip = res[0] || {};
        var branding = res[1] || {};
        var present = ((branding.LoginDisclaimer) || '').indexOf(HTML_START) !== -1;

        var action;
        if (manage && enabledCount > 0) {
            action = 'install';
        } else if (manage) {
            action = present ? 'remove' : 'none'; // enabled toggled off entirely
        } else if (present) {
            action = window.confirm(
                'The plugin previously added an SSO login button to Branding '
                + '(Login Disclaimer + Custom CSS).\n\nRemove it now? Cancel leaves it in place.'
            ) ? 'remove' : 'none';
        } else {
            action = 'none';
        }

        if (action === 'none') {
            setBrandingStatus(view, present);
            return;
        }

        var html = action === 'install' ? (snip.Html || '') : '';
        var css = action === 'install' ? (snip.Css || '') : '';
        var newDisclaimer = spliceRegion(branding.LoginDisclaimer, HTML_START, HTML_END, html);
        var newCss = spliceRegion(branding.CustomCss, CSS_START, CSS_END, css);

        if (newDisclaimer === (branding.LoginDisclaimer || '') && newCss === (branding.CustomCss || '')) {
            setBrandingStatus(view, action === 'install');
            return;
        }

        branding.LoginDisclaimer = newDisclaimer;
        branding.CustomCss = newCss;
        return ApiClient.updateNamedConfiguration('branding', branding).then(function () {
            setBrandingStatus(view, action === 'install');
        });
    }).catch(function (err) {
        console.error('OIDC RBAC: branding sync failed', err);
    });
}

function esc(str) {
    var d = document.createElement('div');
    d.textContent = str;
    return d.innerHTML;
}

function gval(view, id) {
    var el = view.querySelector('#' + id);
    return el ? el.value : '';
}

function gchk(view, id) {
    var el = view.querySelector('#' + id);
    return el ? el.checked : false;
}

function fld(label, type, id, value, placeholder, full) {
    return '<div class="oidc-field' + (full ? ' full' : '') + '">' +
        '<label for="' + id + '">' + esc(label) + '</label>' +
        '<input is="emby-input" type="' + type + '" id="' + id + '" value="' + esc(String(value || '')) + '"' +
        (placeholder ? ' placeholder="' + esc(placeholder) + '"' : '') +
        ' autocomplete="off" autocapitalize="off" spellcheck="false" />' +
        '</div>';
}

function chk(id, label, checked) {
    return '<label><input type="checkbox" id="' + id + '"' + (checked ? ' checked' : '') + ' /> ' + esc(label) + '</label>';
}

// <textarea> ↔ string[] for the allowlist fields (one entry per line or comma-separated).
function listToText(arr) { return (arr || []).join('\n'); }
function textToList(str) {
    return (str || '').split(/[\n,]+/).map(function (s) { return s.trim(); }).filter(Boolean);
}

// A titled cluster of related permission checkboxes for the role card.
function permGroup(title, inner) {
    return '<div class="oidc-perm-group">' +
        '<div class="oidc-perm-title">' + esc(title) + '</div>' +
        '<div class="oidc-checkbox-row">' + inner + '</div></div>';
}

// Bundled Button Icon keys — must match Services/KnownProviderIcons.Keys on the server.
var ICON_KEYS = ['authentik', 'keycloak', 'google', 'microsoft', 'okta', 'auth0', 'discord', 'github'];

function iconIsCustom(v) {
    return !!v && ICON_KEYS.indexOf(v) === -1;
}

var ICON_LABELS = { auth0: 'Auth0', github: 'GitHub' };

// One-shot "prefill for <IdP>" helper on the provider card. Sets claim paths / scopes /
// icon only — never Authority or client credentials. Values follow each IdP's common
// convention; some (Google/Okta groups, Auth0 roles) still need IdP-side config.
var PROVIDER_PRESETS = {
    keycloak:  { label: 'Keycloak',            roleClaim: 'realm_access.roles', usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'keycloak'  },
    authentik: { label: 'Authentik',           roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'authentik' },
    authelia:  { label: 'Authelia',            roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: ''          },
    entra:     { label: 'Microsoft Entra ID',  roleClaim: 'roles',             usernameClaim: 'preferred_username', scopes: 'openid profile email',        icon: 'microsoft' },
    google:    { label: 'Google Workspace',    roleClaim: 'groups',             usernameClaim: 'email',              scopes: 'openid profile email',        icon: 'google'    },
    okta:      { label: 'Okta',                roleClaim: 'groups',             usernameClaim: 'preferred_username', scopes: 'openid profile email groups', icon: 'okta'      },
    auth0:     { label: 'Auth0',               roleClaim: '',                  usernameClaim: 'nickname',           scopes: 'openid profile email',        icon: 'auth0'     }
};

function presetField(idx) {
    var opts = '<option value="">— choose an IdP —</option>';
    Object.keys(PROVIDER_PRESETS).forEach(function (k) {
        opts += '<option value="' + k + '">' + esc(PROVIDER_PRESETS[k].label) + '</option>';
    });
    return '<div class="oidc-field full">' +
        '<label for="prov_preset_' + idx + '">Prefill for ' +
        '<span class="oidc-hint">(sets claims / scopes / icon — you still enter Authority &amp; client credentials)</span></label>' +
        '<select id="prov_preset_' + idx + '">' + opts + '</select></div>';
}

function iconField(idx, cur) {
    var custom = iconIsCustom(cur);
    var hidden = custom ? '' : 'display:none;';
    var opts = '<option value="none"' + (!cur ? ' selected' : '') + '>None</option>';
    ICON_KEYS.forEach(function (k) {
        var label = ICON_LABELS[k] || (k.charAt(0).toUpperCase() + k.slice(1));
        opts += '<option value="' + k + '"' + (cur === k ? ' selected' : '') + '>' + label + '</option>';
    });
    opts += '<option value="custom"' + (custom ? ' selected' : '') + '>Custom (image)</option>';
    return '<div class="oidc-field full">' +
        '<label for="prov_icon_' + idx + '">Button Icon</label>' +
        '<select is="emby-select" id="prov_icon_' + idx + '">' + opts + '</select>' +
        '<textarea id="prov_icon_svg_' + idx + '" placeholder="Paste &lt;svg&gt;…&lt;/svg&gt; or a data:image/… URI, or pick a file below" ' +
        'style="margin-top:0.3em;width:100%;font-family:monospace;font-size:0.8em;' + hidden + '">' +
        esc(custom ? cur : '') + '</textarea>' +
        '<input type="file" id="prov_icon_file_' + idx + '" accept=".svg,.png,.jpg,.jpeg,.gif,.webp,image/svg+xml,image/png,image/jpeg,image/gif,image/webp" style="margin-top:0.3em;' + hidden + '" />' +
        '</div>';
}

function addLibChip(container, libId) {
    var chip = document.createElement('span');
    chip.className = 'oidc-library-chip';
    chip.setAttribute('data-lib-id', libId);
    chip.innerHTML = esc(libs[libId] || libId) + ' <span class="remove">&times;</span>';
    container.appendChild(chip);
}

// One field group inside a provider card, rendered as a <details>. `open` decides the
// initial state (Connection opens only when the provider isn't configured yet); every
// field stays in the DOM either way, so collectProviders() is unaffected.
function provGroup(title, hint, inner, open) {
    var head = esc(title) + (hint ? ' <span class="oidc-hint">' + esc(hint) + '</span>' : '');
    return '<details class="oidc-section"' + (open ? ' open' : '') + '><summary>' + head + '</summary>' +
        '<div class="oidc-grid">' + inner + '</div></details>';
}

// Host portion of an Authority URL, for the provider card header. Falls back to a
// scheme/path strip when the value isn't yet a valid absolute URL.
function authorityHost(url) {
    if (!url) return '';
    try {
        return new URL(url).host;
    } catch (e) {
        return String(url).replace(/^[a-z][a-z0-9+.-]*:\/\//i, '').split('/')[0];
    }
}

function emptyState(msg) {
    return '<div class="oidc-empty">' + esc(msg) + '</div>';
}

// The URL to register at the IdP as backchannel_logout_uri for this provider.
function backchannelLogoutUrl(p) {
    var base = (p.ServerBaseUrl || '').replace(/\/+$/, '');
    if (!base) {
        try { base = ApiClient.serverAddress().replace(/\/+$/, ''); } catch (e) { base = ''; }
    }
    return (base || '(your server URL)') + '/sso/OIDC/BackchannelLogout/' + encodeURIComponent(p.ProviderId || '');
}

function renderProviders(view) {
    var container = view.querySelector('#providerList');
    container.innerHTML = '';
    if (!cfg.Providers.length) {
        container.innerHTML = emptyState(
            "No providers configured yet — users can't sign in with SSO until you add one.");
        return;
    }
    cfg.Providers.forEach(function (p, idx) {
        var card = document.createElement('div');
        card.className = 'oidc-card';

        var connection =
            presetField(idx) +
            fld('Provider ID', 'text', 'prov_id_' + idx, p.ProviderId, 'Unique identifier (e.g. keycloak)') +
            fld('Display Name', 'text', 'prov_name_' + idx, p.DisplayName, 'Shown on login button') +
            fld('Authority URL', 'text', 'prov_authority_' + idx, p.Authority, 'https://idp.example.com/realms/myrealm', true) +
            fld('Client ID', 'text', 'prov_clientid_' + idx, p.ClientId, '') +
            fld('Client Secret', 'password', 'prov_secret_' + idx, p.ClientSecret, '') +
            fld('Scopes', 'text', 'prov_scopes_' + idx, p.Scopes || 'openid profile email', '');

        var claims =
            fld('Role Claim Path', 'text', 'prov_roleclaim_' + idx, p.RoleClaim || 'groups', 'e.g. groups or realm_access.roles') +
            fld('Username Claim', 'text', 'prov_userclaim_' + idx, p.UsernameClaim || 'preferred_username', '') +
            fld('Display Name Claim', 'text', 'prov_displayclaim_' + idx, p.DisplayNameClaim || 'name', '') +
            fld('Email Claim', 'text', 'prov_emailclaim_' + idx, p.EmailClaim || 'email', '') +
            fld('Picture Claim', 'text', 'prov_pictureclaim_' + idx, p.PictureClaim || 'picture', 'e.g. picture') +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_syncimage_' + idx + '"' +
            (p.SyncProfileImage !== false ? ' checked' : '') + '/> Sync profile image</label></div>' +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_syncdisplay_' + idx + '"' +
            (p.SyncDisplayName === true ? ' checked' : '') + '/> Sync display name on login</label>' +
            '<span class="oidc-hint" style="margin-left:1.5em;">Jellyfin has no separate display name — this <strong>renames the Jellyfin account</strong> to match the Display Name Claim on every login. Safe now that identity is keyed on the OIDC subject.</span></div>';

        var appearance =
            '<div class="oidc-field">' +
            '<label for="prov_color_' + idx + '">Button Color</label>' +
            '<div style="display:flex;gap:0.4em;align-items:center;">' +
            '<input type="color" id="prov_color_' + idx + '" value="' + esc(p.ButtonColor || DEFAULT_BUTTON_COLOR) + '" />' +
            '<button type="button" class="oidc-btn-secondary" data-action="reset-color" data-idx="' + idx + '">Reset to default</button>' +
            '</div></div>' +
            iconField(idx, p.ButtonIcon || '');

        var advanced =
            fld('Additional Params', 'text', 'prov_params_' + idx, p.AdditionalParameters || '', 'key=val&key2=val2', true) +
            fld('Server Base URL (override)', 'text', 'prov_baseurl_' + idx, p.ServerBaseUrl || '', 'Optional: https://jellyfin.example.com — overrides auto-detected redirect_uri host', true) +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_strict_access_' + idx + '"' +
            (p.StrictAccessTokenValidation !== false ? ' checked' : '') + '/> Strict access token validation</label>' +
            '<span class="oidc-hint" style="margin-left:1.5em;">Only applies when the IdP issues JWT access tokens (e.g. Keycloak). Opaque access tokens (Google, default Authelia) are skipped automatically and unaffected by this setting. Uncheck if your IdP signs access tokens with a different key than the JWKS endpoint advertises.</span></div>' +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_allow_loopback_' + idx + '"' +
            (p.AllowLoopbackAuthority === true ? ' checked' : '') + '/> Allow loopback Authority</label>' +
            '<span class="oidc-hint" style="margin-left:1.5em;">By default, an Authority resolving to a loopback address (127.0.0.1, ::1) is blocked. Enable this only if your IdP is intentionally hosted at loopback.</span></div>' +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_allow_linklocal_' + idx + '"' +
            (p.AllowLinkLocalAuthority === true ? ' checked' : '') + '/> Allow link-local Authority</label>' +
            '<span class="oidc-hint" style="margin-left:1.5em;">By default, an Authority resolving to a link-local address (169.254.x.x, fe80::) is blocked. Enable this only if your IdP is intentionally hosted at a link-local address.</span></div>' +
            '<div class="oidc-field full" style="margin-top:0.5em;">' +
            '<label style="font-weight:600;font-size:0.9em;">Endpoint Pins ' +
            '<span class="oidc-hint">— pre-fill from your IdP docs to eliminate first-use trust, or click Test Connection to fill automatically</span></label>' +
            '<div class="oidc-grid" style="margin-top:0.4em;">' +
            fld('Issuer', 'text', 'prov_pinnedissuer_' + idx, p.PinnedIssuer || '', 'https://idp.example.com/realms/myrealm') +
            fld('Token Endpoint', 'text', 'prov_pinnedtoken_' + idx, p.PinnedTokenEndpoint || '', 'https://idp.example.com/.../token') +
            fld('JWKS URI', 'text', 'prov_pinnedjwks_' + idx, p.PinnedJwksUri || '', 'https://idp.example.com/.../certs') +
            '</div></div>' +
            (p.ProviderId ?
                '<div class="oidc-field full" style="margin-top:0.5em;">' +
                '<label style="font-weight:600;font-size:0.9em;">Back-channel logout URL ' +
                '<span class="oidc-hint">— register as the client\'s <code>backchannel_logout_uri</code> so the IdP can revoke Jellyfin sessions</span></label>' +
                '<div style="display:flex;gap:0.4em;align-items:center;margin-top:0.3em;">' +
                '<input is="emby-input" type="text" id="prov_bclogout_' + idx + '" readonly value="' +
                esc(backchannelLogoutUrl(p)) + '" style="flex:1;font-family:monospace;font-size:0.85em;" />' +
                '<button type="button" class="oidc-btn-secondary" data-copy="prov_bclogout_' + idx + '">Copy</button>' +
                '</div></div>'
                : '');

        var host = authorityHost(p.Authority);
        // "Configured" providers open fully collapsed; a fresh/incomplete one opens with
        // Connection expanded so the essentials are in front of you.
        var configured = !!(p.ProviderId && p.Authority && p.ClientId);
        if (p.Enabled === false) card.className += ' oidc-disabled';
        card.innerHTML = '<div class="oidc-card-head">' +
            '<h4>' + esc(p.DisplayName || 'New Provider') + '</h4>' +
            (host ? '<span class="oidc-card-sub">' + esc(host) + '</span>' : '') +
            '<label class="oidc-enable-toggle"><span>Enabled</span>' +
            '<input type="checkbox" id="prov_enabled_' + idx + '"' + (p.Enabled !== false ? ' checked' : '') + ' /></label>' +
            '</div>' +
            provGroup('Connection', 'provider id, endpoint & client credentials', connection, !configured) +
            provGroup('Claim mapping', 'role, username, display name & avatar', claims, false) +
            provGroup('Appearance', 'login button colour & icon', appearance, false) +
            provGroup('Advanced & security', 'redirect host, token validation, network guards, endpoint pins', advanced, false) +
            '<div style="margin-top:0.8em;display:flex;gap:0.5em;align-items:center;flex-wrap:wrap;">' +
            '<button type="button" class="oidc-btn-secondary oidc-btn-icon" data-action="move-provider" data-dir="-1" data-idx="' + idx + '" title="Move up (changes login-button order)"' + (idx === 0 ? ' disabled' : '') + '>&#8593;</button>' +
            '<button type="button" class="oidc-btn-secondary oidc-btn-icon" data-action="move-provider" data-dir="1" data-idx="' + idx + '" title="Move down (changes login-button order)"' + (idx === cfg.Providers.length - 1 ? ' disabled' : '') + '>&#8595;</button>' +
            '<button type="button" class="oidc-btn-secondary" data-action="test-provider" data-idx="' + idx + '">Test Connection</button>' +
            '<button type="button" class="oidc-btn-remove" data-action="remove-provider" data-idx="' + idx + '">Remove</button>' +
            '<span class="oidc-test-result" data-idx="' + idx + '" style="font-size:0.9em;"></span>' +
            '</div>';
        container.appendChild(card);
    });
}

// Fills the "Fallback role" <select> on the Role Mappings tab from the current role names.
// Reads live DOM values when cards are present (so it tracks unsaved renames), else cfg.
function renderDefaultRoleOptions(view) {
    var sel = view.querySelector('#defaultRoleName');
    if (!sel) return;
    var current = sel.value || cfg.DefaultRoleName || '';
    var source = view.querySelector('#roleMappingList .oidc-card')
        ? collectRoleMappings(view).map(function (m) { return m.RoleName; })
        : (cfg.RoleMappings || []).map(function (m) { return m.RoleName; });
    var names = [];
    source.forEach(function (n) {
        n = (n || '').trim();
        if (n && names.every(function (x) { return x.toLowerCase() !== n.toLowerCase(); })) names.push(n);
    });
    var opts = '<option value="">— none —</option>';
    if (current && names.every(function (x) { return x.toLowerCase() !== current.toLowerCase(); })) {
        opts += '<option value="' + esc(current) + '">' + esc(current) + ' (not a defined role)</option>';
    }
    names.forEach(function (n) { opts += '<option value="' + esc(n) + '">' + esc(n) + '</option>'; });
    sel.innerHTML = opts;
    sel.value = current;
}

function renderRoleMappings(view) {
    var container = view.querySelector('#roleMappingList');
    container.innerHTML = '';
    if (!cfg.RoleMappings.length) {
        container.innerHTML = emptyState(
            'No role mappings — signed-in users get the fallback role selected above, '
            + 'or no extra permissions if that is "— none —".');
        renderDefaultRoleOptions(view);
        return;
    }
    cfg.RoleMappings.forEach(function (m, idx) {
        var card = document.createElement('details');
        card.className = 'oidc-card oidc-role';
        var libOpts = Object.keys(libs).map(function (id) {
            return '<option value="' + esc(id) + '">' + esc(libs[id]) + '</option>';
        }).join('');
        var selectedLibs = (m.LibraryIds || []).concat(
            (m.LibraryNames || []).map(function (name) {
                var f = Object.keys(libs).find(function (id) {
                    return libs[id].toLowerCase() === name.toLowerCase();
                });
                return f || name;
            })
        );
        // Build provider filter dropdown: blank = applies to all providers
        var provOpts = '<option value=""' + (!m.ProviderFilter ? ' selected' : '') + '>All providers (global)</option>' +
            (cfg.Providers || []).map(function (p) {
                return '<option value="' + esc(p.ProviderId) + '"' +
                    (m.ProviderFilter === p.ProviderId ? ' selected' : '') + '>' +
                    esc(p.DisplayName || p.ProviderId) + '</option>';
            }).join('');
        // Collapsed summary: role name + Admin badge + a one-line scope (provider,
        // library access, priority). A role without a name opens expanded.
        var provLabel = m.ProviderFilter
            ? (((cfg.Providers || []).find(function (p) { return p.ProviderId === m.ProviderFilter; }) || {}).DisplayName || m.ProviderFilter)
            : 'all providers';
        var libLabel = m.EnableAllLibraries
            ? 'all libraries'
            : (selectedLibs.length ? selectedLibs.length + (selectedLibs.length === 1 ? ' library' : ' libraries') : 'no library access');
        var scopeParts = [provLabel, libLabel];

        card.innerHTML = '<summary class="oidc-role-summary">' +
            '<h4>Role: ' + esc(m.RoleName || 'New Role') + '</h4>' +
            (m.IsAdmin ? '<span class="oidc-badge">Admin</span>' : '') +
            '<span class="oidc-role-scope">' + esc(scopeParts.join('  ·  ')) + '</span>' +
            '</summary>' +
            fld('Role Name', 'text', 'role_name_' + idx, m.RoleName, 'Must match IdP role claim value', true) +
            '<div class="oidc-field full" style="margin-bottom:0.5em;">' +
            '<label>Provider Filter <span class="oidc-hint">(restrict to one provider — leave blank to apply to all)</span></label>' +
            '<select is="emby-select" id="role_provfilter_' + idx + '">' + provOpts + '</select>' +
            '</div>' +
            '<div class="oidc-field full" style="margin-top:0.3em;">' +
            '<label>Permissions <span class="oidc-hint">(Administrator grants everything below)</span></label>' +
            '<p class="oidc-hint" style="margin:0.1em 0 0;">When a user matches several roles, all their permissions are combined and the strictest parental rating wins.</p>' +
            '<div class="oidc-checkbox-row" style="margin-top:0.2em;">' +
            chk('role_admin_' + idx, 'Administrator', m.IsAdmin) +
            '</div>' +
            permGroup('Playback',
                chk('role_playback_' + idx, 'Playback', m.EnableMediaPlayback !== false) +
                chk('role_transcode_' + idx, 'Transcoding', m.EnableTranscoding !== false) +
                chk('role_remote_' + idx, 'Remote Access', m.EnableRemoteAccess !== false)) +
            permGroup('Live TV',
                chk('role_livetv_' + idx, 'Access', m.EnableLiveTv) +
                chk('role_livetvmgmt_' + idx, 'Recording management', m.EnableLiveTvManagement)) +
            permGroup('Content management',
                chk('role_collections_' + idx, 'Collections', m.EnableCollectionManagement) +
                chk('role_subtitles_' + idx, 'Subtitles', m.EnableSubtitleManagement) +
                chk('role_delete_' + idx, 'Delete content', m.EnableContentDeletion)) +
            '</div>' +
            '<div class="oidc-field full" style="margin-top:0.5em;">' +
            '<div class="oidc-perm-title">Library access</div>' +
            '<div class="oidc-checkbox-row">' +
            chk('role_alllibs_' + idx, 'All libraries', m.EnableAllLibraries) +
            '</div>' +
            '<label style="margin-top:0.4em;">Specific libraries <span class="oidc-hint">(used when "All libraries" is off)</span></label>' +
            '<select is="emby-select" id="role_libadd_' + idx + '"><option value="">-- Select library --</option>' + libOpts + '</select>' +
            '<button type="button" class="oidc-btn-secondary" style="margin-top:0.3em;width:fit-content;" data-action="add-lib" data-idx="' + idx + '">Add Library</button>' +
            '<div id="role_libs_' + idx + '" class="oidc-library-list"></div>' +
            '</div>' +
            '<div class="oidc-field" style="margin-top:0.5em;">' +
            '<label>Max Parental Rating (empty = unrestricted)</label>' +
            '<input is="emby-input" type="number" id="role_maxrating_' + idx + '" value="' + (m.MaxParentalRating != null ? m.MaxParentalRating : '') + '" />' +
            '</div>' +
            '<div style="margin-top:0.5em;">' +
            '<button type="button" class="oidc-btn-remove" data-action="remove-role" data-idx="' + idx + '">Remove</button>' +
            '</div>';
        card.open = !m.RoleName;
        container.appendChild(card);
        var libCont = view.querySelector('#role_libs_' + idx);
        selectedLibs.forEach(function (libId) { addLibChip(libCont, libId); });
    });
    renderDefaultRoleOptions(view);
}

function testProvider(view, idx) {
    var authority = gval(view, 'prov_authority_' + idx);
    var scopes = gval(view, 'prov_scopes_' + idx);
    var resultEl = view.querySelector('.oidc-test-result[data-idx="' + idx + '"]');
    if (!authority) {
        if (resultEl) { resultEl.style.color = '#e53935'; resultEl.textContent = 'Authority URL is required'; }
        return;
    }
    if (resultEl) { resultEl.style.color = '#888'; resultEl.textContent = 'Testing...'; }

    var allowLoopback = gchk(view, 'prov_allow_loopback_' + idx);
    var allowLinkLocal = gchk(view, 'prov_allow_linklocal_' + idx);

    ApiClient.ajax({
        type: 'POST',
        url: ApiClient.getUrl('sso/OIDC/Config/TestProvider'),
        data: JSON.stringify({
            Authority: authority,
            Scopes: scopes,
            AllowLoopbackAuthority: allowLoopback,
            AllowLinkLocalAuthority: allowLinkLocal
        }),
        contentType: 'application/json',
        dataType: 'json'
    }).then(function (result) {
        if (result.Success) {
            cfg.Providers[idx].PinnedAuthority = cfg.Providers[idx].Authority;
            cfg.Providers[idx].PinnedIssuer = result.Issuer || '';
            cfg.Providers[idx].PinnedTokenEndpoint = result.TokenEndpoint || '';
            cfg.Providers[idx].PinnedJwksUri = result.JwksUri || '';
            // Fill the editable pin fields so the admin can see and verify the values.
            var issuerEl = view.querySelector('#prov_pinnedissuer_' + idx);
            var tokenEl  = view.querySelector('#prov_pinnedtoken_'  + idx);
            var jwksEl   = view.querySelector('#prov_pinnedjwks_'   + idx);
            if (issuerEl) issuerEl.value = result.Issuer        || '';
            if (tokenEl)  tokenEl.value  = result.TokenEndpoint || '';
            if (jwksEl)   jwksEl.value   = result.JwksUri       || '';
            // Reveal the collapsed section so the just-filled pin fields are visible.
            var sec = issuerEl && issuerEl.closest('details.oidc-section');
            if (sec) sec.open = true;
            setDirty(true); // pins were written into the form; Save persists them
            if (resultEl) {
                resultEl.style.color = '#4caf50';
                var msg = 'OK — issuer ' + result.Issuer;
                if (result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0) {
                    msg += ' (warning: scopes not advertised: ' + result.UnsupportedRequestedScopes.join(', ') + ')';
                    resultEl.style.color = '#ff9800';
                }
                resultEl.textContent = msg;
            }
            Dashboard.alert({
                title: 'Provider OK',
                message:
                    'Issuer: ' + result.Issuer + '\n' +
                    'Authorize: ' + result.AuthorizationEndpoint + '\n' +
                    'Token: ' + result.TokenEndpoint + '\n' +
                    (result.UserInfoEndpoint ? 'UserInfo: ' + result.UserInfoEndpoint + '\n' : '') +
                    (result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0
                        ? '\nWarning: these requested scopes are not in scopes_supported:\n  ' + result.UnsupportedRequestedScopes.join(', ')
                        : '')
            });
        } else {
            if (resultEl) { resultEl.style.color = '#e53935'; resultEl.textContent = 'Failed: ' + result.Error; }
            Dashboard.alert({ title: 'Provider test failed', message: result.Error || 'Unknown error' });
        }
    }).catch(function (err) {
        var msg = (err && (err.statusText || err.message)) || 'Network error';
        if (resultEl) { resultEl.style.color = '#e53935'; resultEl.textContent = 'Failed: ' + msg; }
        Dashboard.alert({ title: 'Provider test failed', message: msg });
    });
}

function collectIcon(view, idx) {
    var kind = gval(view, 'prov_icon_' + idx);
    if (kind === 'custom') return (gval(view, 'prov_icon_svg_' + idx) || '').trim();
    if (!kind || kind === 'none') return '';
    return kind;
}

function collectProviders(view) {
    var result = [];
    view.querySelectorAll('#providerList .oidc-card').forEach(function (card, idx) {
        result.push({
            ProviderId: gval(view, 'prov_id_' + idx),
            DisplayName: gval(view, 'prov_name_' + idx),
            Authority: gval(view, 'prov_authority_' + idx),
            ClientId: gval(view, 'prov_clientid_' + idx),
            ClientSecret: gval(view, 'prov_secret_' + idx),
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
            PinnedIssuer: gval(view, 'prov_pinnedissuer_' + idx),
            PinnedTokenEndpoint: gval(view, 'prov_pinnedtoken_' + idx),
            PinnedJwksUri: gval(view, 'prov_pinnedjwks_' + idx),
            // If any pin is set, record the current authority so the mismatch-detection logic
            // fires correctly on subsequent auths. Clear it if the admin wipes all pins.
            PinnedAuthority: (gval(view, 'prov_pinnedissuer_' + idx) ||
                              gval(view, 'prov_pinnedtoken_'  + idx) ||
                              gval(view, 'prov_pinnedjwks_'   + idx))
                             ? gval(view, 'prov_authority_' + idx)
                             : '',
            ButtonIcon: collectIcon(view, idx)
        });
    });
    return result;
}

function collectRoleMappings(view) {
    var result = [];
    view.querySelectorAll('#roleMappingList .oidc-card').forEach(function (card, idx) {
        var chips = view.querySelectorAll('#role_libs_' + idx + ' .oidc-library-chip');
        var libIds = [];
        chips.forEach(function (c) { libIds.push(c.getAttribute('data-lib-id')); });
        var mr = gval(view, 'role_maxrating_' + idx);
        result.push({
            RoleName: gval(view, 'role_name_' + idx),
            ProviderFilter: gval(view, 'role_provfilter_' + idx),
            IsAdmin: gchk(view, 'role_admin_' + idx),
            EnableAllLibraries: gchk(view, 'role_alllibs_' + idx),
            LibraryIds: libIds, LibraryNames: [],
            EnableLiveTv: gchk(view, 'role_livetv_' + idx),
            EnableLiveTvManagement: gchk(view, 'role_livetvmgmt_' + idx),
            EnableMediaPlayback: gchk(view, 'role_playback_' + idx),
            EnableRemoteAccess: gchk(view, 'role_remote_' + idx),
            EnableTranscoding: gchk(view, 'role_transcode_' + idx),
            EnableContentDeletion: gchk(view, 'role_delete_' + idx),
            EnableCollectionManagement: gchk(view, 'role_collections_' + idx),
            EnableSubtitleManagement: gchk(view, 'role_subtitles_' + idx),
            MaxParentalRating: mr ? parseInt(mr) : null
        });
    });
    return result;
}

export default function (view) {
    dirtyView = view;

    // Warn on reload / tab-close / navigation away from the dashboard while edits are
    // pending. (SPA navigation to another dashboard section can't be intercepted here,
    // so that path is covered only by the visible "Unsaved changes" indicator.)
    window.addEventListener('beforeunload', beforeUnloadGuard);

    // Any field edit marks the form dirty. Programmatic value/checked assignments during
    // load and re-render don't emit input/change, so they never trip this.
    view.addEventListener('input', function (e) {
        setDirty(true);
        // keep the Fallback-role dropdown in step with unsaved role renames
        if (e.target && e.target.id && e.target.id.indexOf('role_name_') === 0) renderDefaultRoleOptions(view);
    }, true);
    view.addEventListener('change', function () { setDirty(true); }, true);

    // The save bar is position:fixed (a Jellyfin overflow:hidden wrapper breaks
    // position:sticky here), so keep it lined up with the content column and reserve
    // space beneath the form so the last field isn't covered.
    var savebar = view.querySelector('.oidc-savebar');
    var contentPrimary = view.querySelector('.content-primary');
    function alignSaveBar() {
        if (!savebar || !contentPrimary) return;
        var r = contentPrimary.getBoundingClientRect();
        if (r.width < 1) return; // not visible yet
        savebar.style.left = r.left + 'px';
        savebar.style.width = r.width + 'px';
        var pad = (savebar.offsetHeight + 12) + 'px'; // guard: only write when it changes,
        if (contentPrimary.style.paddingBottom !== pad) { // so the ResizeObserver settles
            contentPrimary.style.paddingBottom = pad;
        }
    }
    if (savebar) {
        var bg = getComputedStyle(document.body).backgroundColor;
        savebar.style.background = (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') ? bg : '#101010';
    }
    // A ResizeObserver on the content column re-aligns the bar whenever the form's
    // width changes — tab switch, card add/remove, scrollbar appearing, window resize.
    if (window.ResizeObserver && contentPrimary) {
        new ResizeObserver(alignSaveBar).observe(contentPrimary);
    }
    view.addEventListener('viewbeforehide', function () {
        setDirty(false);
        window.removeEventListener('resize', alignSaveBar);
    });

    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        window.addEventListener('resize', alignSaveBar);

        ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/Config/Libraries')).then(function (data) {
            libs = data || {};
        }).catch(function () {
            libs = {};
        }).then(function () {
            return ApiClient.getPluginConfiguration(pluginId);
        }).then(function (config) {
            cfg = config;
            cfg.Providers = cfg.Providers || [];
            cfg.RoleMappings = cfg.RoleMappings || [];
            renderProviders(view);
            renderRoleMappings(view); // also fills the #defaultRoleName <select> from cfg
            view.querySelector('#autoCreateUsers').checked = cfg.AutoCreateUsers !== false;
            view.querySelector('#migrateLocalUsers').checked = cfg.MigrateLocalUsers === true;
            view.querySelector('#blockPrivateNetworkAuthorities').checked = cfg.BlockPrivateNetworkAuthorities === true;
            view.querySelector('#allowedGroups').value = listToText(cfg.AllowedGroups);
            view.querySelector('#allowedEmailDomains').value = listToText(cfg.AllowedEmailDomains);
            view.querySelector('#allowedEmails').value = listToText(cfg.AllowedEmails);
            view.querySelector('#requireVerifiedEmail').checked = cfg.RequireVerifiedEmail === true;
            view.querySelector('#linkExistingUsersByEmail').checked = cfg.LinkExistingUsersByEmail === true;
            view.querySelector('#manageLoginButtonBranding').checked = cfg.ManageLoginButtonBranding !== false;
            view.querySelector('#hideManualLogin').checked = cfg.HideManualLogin === true;
            view.querySelector('#loginTitle').value = cfg.LoginTitle || 'Please sign in';
            view.querySelector('#loginSubtitle').value = cfg.LoginSubtitle || '';
            loadBrandingSnippet(view);
            setDirty(false);
            alignSaveBar();
            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            console.error('OIDC RBAC: failed to load config', err);
        });
    });

    // Tabs
    view.querySelectorAll('.oidc-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            view.querySelectorAll('.oidc-tab').forEach(function (t) {
                t.classList.remove('is-active');
                t.setAttribute('aria-selected', 'false');
            });
            view.querySelectorAll('.oidc-tab-content').forEach(function (c) {
                c.style.display = 'none';
            });
            this.classList.add('is-active');
            this.setAttribute('aria-selected', 'true');
            view.querySelector('#tab-' + this.getAttribute('data-tab')).style.display = 'block';
        });
    });

    // Copy-to-clipboard buttons (manual branding snippet)
    view.querySelectorAll('[data-copy]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var ta = view.querySelector('#' + btn.getAttribute('data-copy'));
            if (!ta) return;
            ta.select();
            if (navigator.clipboard) {
                navigator.clipboard.writeText(ta.value).catch(function () {});
            } else {
                try { document.execCommand('copy'); } catch (e) { /* ignore */ }
            }
            var orig = btn.textContent;
            btn.textContent = 'Copied';
            setTimeout(function () { btn.textContent = orig; }, 1200);
        });
    });

    // Add provider
    view.querySelector('#btnAddProvider').addEventListener('click', function () {
        if (!cfg) return;
        cfg.Providers = collectProviders(view);
        cfg.Providers.push({
            ProviderId: '', DisplayName: 'New Provider', Authority: '',
            ClientId: '', ClientSecret: '', Scopes: 'openid profile email',
            RoleClaim: 'groups', UsernameClaim: 'preferred_username',
            DisplayNameClaim: 'name', EmailClaim: 'email', PictureClaim: 'picture',
            SyncProfileImage: true, SyncDisplayName: false,
            Enabled: true, ButtonColor: DEFAULT_BUTTON_COLOR,
            ButtonIcon: '', AdditionalParameters: '',
            StrictAccessTokenValidation: true,
            AllowLoopbackAuthority: false, AllowLinkLocalAuthority: false,
            PinnedAuthority: '', PinnedIssuer: '', PinnedTokenEndpoint: '', PinnedJwksUri: ''
        });
        renderProviders(view);
        setDirty(true);
    });

    // Add role mapping
    view.querySelector('#btnAddRoleMapping').addEventListener('click', function () {
        if (!cfg) return;
        cfg.RoleMappings = collectRoleMappings(view);
        cfg.RoleMappings.push({
            RoleName: '', ProviderFilter: '', IsAdmin: false, EnableAllLibraries: false,
            LibraryIds: [], LibraryNames: [], EnableLiveTv: false,
            EnableLiveTvManagement: false, EnableMediaPlayback: true,
            EnableRemoteAccess: true, EnableTranscoding: true,
            EnableContentDeletion: false, EnableCollectionManagement: false,
            EnableSubtitleManagement: false, MaxParentalRating: null
        });
        renderRoleMappings(view);
        setDirty(true);
    });

    // Save
    view.querySelector('#btnSave').addEventListener('click', function () {
        if (!cfg) return;
        // Warn (not block) if any enabled provider has no pins — TOFU will apply on first auth.
        var unpinned = (cfg.Providers || []).filter(function (p, idx) {
            return p.Enabled !== false && !gval(view, 'prov_pinnedissuer_' + idx);
        });
        if (unpinned.length > 0) {
            var names = unpinned.map(function (p) { return p.DisplayName || p.ProviderId; }).join(', ');
            if (!window.confirm('Provider(s) without endpoint pins: ' + names + '.\n\nEndpoints will be trusted on first login (TOFU). Pre-fill the pin fields or run Test Connection to eliminate this window.\n\nSave anyway?')) {
                return;
            }
        }
        Dashboard.showLoadingMsg();
        cfg.Providers = collectProviders(view);
        cfg.RoleMappings = collectRoleMappings(view);
        cfg.DefaultRoleName = gval(view, 'defaultRoleName');
        cfg.AutoCreateUsers = gchk(view, 'autoCreateUsers');
        cfg.MigrateLocalUsers = gchk(view, 'migrateLocalUsers');
        cfg.BlockPrivateNetworkAuthorities = gchk(view, 'blockPrivateNetworkAuthorities');
        cfg.AllowedGroups = textToList(gval(view, 'allowedGroups'));
        cfg.AllowedEmailDomains = textToList(gval(view, 'allowedEmailDomains'));
        cfg.AllowedEmails = textToList(gval(view, 'allowedEmails'));
        cfg.RequireVerifiedEmail = gchk(view, 'requireVerifiedEmail');
        cfg.LinkExistingUsersByEmail = gchk(view, 'linkExistingUsersByEmail');
        cfg.ManageLoginButtonBranding = gchk(view, 'manageLoginButtonBranding');
        cfg.HideManualLogin = gchk(view, 'hideManualLogin');
        cfg.LoginTitle = gval(view, 'loginTitle') || 'Please sign in';
        cfg.LoginSubtitle = gval(view, 'loginSubtitle') || '';
        ApiClient.updatePluginConfiguration(pluginId, cfg).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
            return syncBranding(view);
        }).then(function () {
            loadBrandingSnippet(view);
            setDirty(false);
            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save: ' + (err.message || err));
        });
    });

    // Event delegation for dynamic buttons in provider list
    view.querySelector('#providerList').addEventListener('click', function (e) {
        var copyBtn = e.target.closest('[data-copy]');
        if (copyBtn) {
            var src = view.querySelector('#' + copyBtn.getAttribute('data-copy'));
            if (src && navigator.clipboard) { navigator.clipboard.writeText(src.value).catch(function () {}); }
            var orig = copyBtn.textContent;
            copyBtn.textContent = 'Copied';
            setTimeout(function () { copyBtn.textContent = orig; }, 1200);
            return;
        }
        var btn = e.target.closest('[data-action]');
        if (!btn) return;
        var idx = parseInt(btn.getAttribute('data-idx'));
        if (btn.getAttribute('data-action') === 'remove-provider') {
            cfg.Providers = collectProviders(view);
            cfg.Providers.splice(idx, 1);
            renderProviders(view);
            setDirty(true);
        } else if (btn.getAttribute('data-action') === 'move-provider') {
            // Provider order is the login-button order (LoginButtonController /
            // BrandingSnippetBuilder both iterate the list as-is).
            var j = idx + parseInt(btn.getAttribute('data-dir'));
            if (j < 0 || j >= cfg.Providers.length) return;
            cfg.Providers = collectProviders(view);
            var moved = cfg.Providers.splice(idx, 1)[0];
            cfg.Providers.splice(j, 0, moved);
            renderProviders(view);
            setDirty(true);
        } else if (btn.getAttribute('data-action') === 'test-provider') {
            testProvider(view, idx);
        } else if (btn.getAttribute('data-action') === 'reset-color') {
            var el = view.querySelector('#prov_color_' + idx);
            if (el) el.value = DEFAULT_BUTTON_COLOR;
            setDirty(true);
        }
    });

    // Button Icon: toggle the custom SVG inputs; load a picked .svg file into the textarea.
    // Also: "Prefill for <IdP>" — one-shot fill of claim/scope/icon fields.
    view.querySelector('#providerList').addEventListener('change', function (e) {
        var t = e.target;
        if (!t || !t.id) return;
        if (t.id.indexOf('prov_preset_') === 0 && t.value) {
            var pidx = t.id.slice('prov_preset_'.length);
            var preset = PROVIDER_PRESETS[t.value];
            t.value = ''; // it's a verb, not state
            if (!preset) return;
            var setVal = function (id, v) { var el = view.querySelector('#' + id); if (el) el.value = v; };
            setVal('prov_roleclaim_' + pidx, preset.roleClaim);
            setVal('prov_userclaim_' + pidx, preset.usernameClaim);
            setVal('prov_scopes_' + pidx, preset.scopes);
            var iconSel = view.querySelector('#prov_icon_' + pidx);
            if (iconSel) {
                iconSel.value = preset.icon || 'none';
                iconSel.dispatchEvent(new Event('change', { bubbles: true }));
            }
            setDirty(true);
            return;
        }
        if (t.id.indexOf('prov_icon_') === 0 && t.tagName === 'SELECT') {
            var idx = t.id.slice('prov_icon_'.length);
            var custom = t.value === 'custom';
            var svg = view.querySelector('#prov_icon_svg_' + idx);
            var file = view.querySelector('#prov_icon_file_' + idx);
            if (svg) svg.style.display = custom ? '' : 'none';
            if (file) file.style.display = custom ? '' : 'none';
        } else if (t.id.indexOf('prov_icon_file_') === 0 && t.files && t.files[0]) {
            var fidx = t.id.slice('prov_icon_file_'.length);
            var f = t.files[0];
            var reader = new FileReader();
            reader.onload = function () {
                var ta = view.querySelector('#prov_icon_svg_' + fidx);
                if (ta) ta.value = String(reader.result || '').trim();
            };
            // SVG stays as markup; raster formats become a data: URI.
            if (/svg/i.test(f.type) || /\.svg$/i.test(f.name)) {
                reader.readAsText(f);
            } else {
                reader.readAsDataURL(f);
            }
        }
    });

    // Event delegation for dynamic buttons in role mapping list
    view.querySelector('#roleMappingList').addEventListener('click', function (e) {
        if (e.target.classList.contains('remove')) {
            e.target.parentElement.remove();
            setDirty(true);
            return;
        }
        var btn = e.target.closest('[data-action]');
        if (!btn) return;
        var idx = parseInt(btn.getAttribute('data-idx'));
        if (btn.getAttribute('data-action') === 'remove-role') {
            cfg.RoleMappings = collectRoleMappings(view);
            cfg.RoleMappings.splice(idx, 1);
            renderRoleMappings(view);
            setDirty(true);
        } else if (btn.getAttribute('data-action') === 'add-lib') {
            var sel = view.querySelector('#role_libadd_' + idx);
            if (!sel || !sel.value) return;
            var cont = view.querySelector('#role_libs_' + idx);
            var chips = cont.querySelectorAll('.oidc-library-chip');
            for (var i = 0; i < chips.length; i++) {
                if (chips[i].getAttribute('data-lib-id') === sel.value) return;
            }
            addLibChip(cont, sel.value);
            sel.value = '';
            setDirty(true);
        }
    });
}
