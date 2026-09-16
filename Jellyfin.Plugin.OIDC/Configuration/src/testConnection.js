// The provider card's "Test Connection" round-trip: validates + pins discovery endpoints.
import { gval, gchk, sval } from './dom.js';
import { cfg, setDirty } from './state.js';
import { STRINGS } from './strings.js';

// Sets a .oidc-test-result span's text and colour via className (oidc-status--dim/ok/warn/error)
// instead of writing style.color directly, so the colour scale lives in CSS, not scattered
// hex literals through this file.
export function setTestStatus(resultEl, variant, text) {
    if (!resultEl) return;
    resultEl.className = 'oidc-test-result' + (variant ? ' oidc-status--' + variant : '');
    resultEl.textContent = text;
}

export function testProvider(view, idx) {
    var issuerEl = view.querySelector('#prov_pinnedissuer_' + idx);
    var authority = issuerEl ? issuerEl.value : '';
    var scopes = gval(view, 'prov_scopes_' + idx);
    var resultEl = view.querySelector('.oidc-test-result[data-idx="' + idx + '"]');
    if (!authority) {
        setTestStatus(resultEl, 'error', STRINGS.testConnection.issuerRequired);
        return;
    }
    setTestStatus(resultEl, 'dim', STRINGS.testConnection.testing);

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
            // The pinned issuer must be the discovery document's own `issuer` value, not the
            // URL the admin typed (they differ on a trailing slash, path normalisation, or the
            // rare IdP whose discovery URL isn't its issuer). Persisting the typed string would
            // make the very next login fail the server-side pin check. The hidden Authority
            // mirror keeps the URL that actually resolved discovery.
            var canonicalIssuer = result.Issuer || authority;
            cfg.Providers[idx].Authority = authority;
            cfg.Providers[idx].PinnedAuthority = authority;
            cfg.Providers[idx].PinnedIssuer = canonicalIssuer;
            cfg.Providers[idx].PinnedTokenEndpoint = result.TokenEndpoint || '';
            cfg.Providers[idx].PinnedJwksUri = result.JwksUri || '';
            cfg.Providers[idx].PinnedUserInfoEndpoint = result.UserInfoEndpoint || '';
            cfg.Providers[idx].PinnedAuthorizeEndpoint = result.AuthorizationEndpoint || '';

            sval(view, 'prov_discovery_' + idx, authority);
            sval(view, 'prov_pinnedauthority_' + idx, authority);
            sval(view, 'prov_pinnedtoken_' + idx, result.TokenEndpoint || '');
            sval(view, 'prov_pinnedjwks_' + idx, result.JwksUri || '');
            sval(view, 'prov_pinneduserinfo_' + idx, result.UserInfoEndpoint || '');
            sval(view, 'prov_pinnedauthorize_' + idx, result.AuthorizationEndpoint || '');

            // Show the canonical issuer in the box and mark exactly that value verified, so
            // Save persists the issuer the server will actually see at login.
            if (issuerEl) {
                issuerEl.value = canonicalIssuer;
                issuerEl.dataset.verified = canonicalIssuer;
            }
            var sec = issuerEl && issuerEl.closest('details.oidc-section');
            if (sec) sec.open = true;
            setDirty(true); // pins were written into the form; Save persists them
            var msg = STRINGS.testConnection.okPrefix + result.Issuer;
            var hasScopeWarning = result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0;
            if (hasScopeWarning) {
                msg += STRINGS.testConnection.scopeWarningPrefix + result.UnsupportedRequestedScopes.join(', ') + STRINGS.testConnection.scopeWarningSuffix;
            }
            setTestStatus(resultEl, hasScopeWarning ? 'warn' : 'ok', msg);
            Dashboard.alert({
                title: STRINGS.testConnection.dialogTitleOk,
                message:
                    STRINGS.testConnection.issuerLinePrefix + result.Issuer + '\n' +
                    STRINGS.testConnection.authorizeLinePrefix + result.AuthorizationEndpoint + '\n' +
                    STRINGS.testConnection.tokenLinePrefix + result.TokenEndpoint + '\n' +
                    (result.UserInfoEndpoint ? STRINGS.testConnection.userInfoLinePrefix + result.UserInfoEndpoint + '\n' : '') +
                    (result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0
                        ? STRINGS.testConnection.scopeWarningBlockPrefix + result.UnsupportedRequestedScopes.join(', ')
                        : '')
            });
        } else {
            setTestStatus(resultEl, 'error', STRINGS.testConnection.failedPrefix + result.Error);
            Dashboard.alert({ title: STRINGS.testConnection.dialogTitleFailed, message: result.Error || STRINGS.testConnection.unknownError });
        }
    }).catch(function (err) {
        var msg = (err && (err.statusText || err.message)) || STRINGS.testConnection.networkError;
        setTestStatus(resultEl, 'error', STRINGS.testConnection.failedPrefix + msg);
        Dashboard.alert({ title: STRINGS.testConnection.dialogTitleFailed, message: msg });
    });
}
