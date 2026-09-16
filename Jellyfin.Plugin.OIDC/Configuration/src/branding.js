// Keeps the marker-fenced SSO login-button block inside Jellyfin's Branding settings (Login
// Disclaimer + Custom CSS) in sync with the plugin's enabled providers.
import { gchk, sval } from './dom.js';
import { cfg } from './state.js';
import { STRINGS } from './strings.js';

// Markers fencing the plugin-managed block inside Branding (Login Disclaimer / Custom CSS).
// Kept in sync with LoginButtonSnippetBuilder on the server; LoginButtonSnippetMarkerSyncTests enforces it.
export var HTML_START = '<!-- oidc-sso-buttons:start -->';
export var HTML_END = '<!-- oidc-sso-buttons:end -->';
export var CSS_START = '/* oidc-sso-buttons:start */';
export var CSS_END = '/* oidc-sso-buttons:end */';

// Replaces the marked region in `text` with `block`. If the markers aren't present, appends
// `block`. An empty `block` removes the region. Returns `text` unchanged when there's nothing
// to do (no region and nothing to add).
export function spliceRegion(text, startMarker, endMarker, block) {
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

export function setBrandingStatus(view, installed) {
    var el = view.querySelector('#brandingStatus');
    if (el) el.textContent = installed ? STRINGS.branding.installed : STRINGS.branding.notInstalled;
}

// Fetches the current snippet into the manual copy/paste boxes and reflects install status.
export function loadBrandingSnippet(view) {
    ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/LoginButtonSnippet')).then(function (snip) {
        sval(view, 'brandingHtml', (snip && snip.Html) || '');
        sval(view, 'brandingCss', (snip && snip.Css) || '');
    }).catch(function () {});
    ApiClient.getNamedConfiguration('branding').then(function (b) {
        setBrandingStatus(view, ((b && b.LoginDisclaimer) || '').indexOf(HTML_START) !== -1);
    }).catch(function () {});
}

// Keeps the marked block in Branding in sync with the just-saved config. Called after the
// plugin configuration is persisted, so GET LoginButtonSnippet reflects the new provider list.
export function syncBranding(view) {
    var manage = gchk(view, 'manageLoginButtonBranding');
    var enabledCount = (cfg.Providers || []).filter(function (p) { return p.Enabled !== false; }).length;

    return Promise.all([
        ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/LoginButtonSnippet')),
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
            action = window.confirm(STRINGS.branding.removeConfirm) ? 'remove' : 'none';
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
