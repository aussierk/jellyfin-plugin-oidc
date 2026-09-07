// Page controller for the plugin's admin config page. Jellyfin dynamically imports this
// module (via the data-controller attribute on configPage.html) and calls only the default
// export with the page's root element - everything else here is internal wiring.
import { gval, gchk } from './dom.js';
import {
    pluginId, cfg, libs, ratings, dirtyView, setCfg, setLibs, setRatings, setDirtyView,
    setDirty, beforeUnloadGuard
} from './state.js';
import { listToText, textToList } from './utils.js';
import { loadBrandingSnippet, syncBranding } from './branding.js';
import { DEFAULT_BUTTON_COLOR, PROVIDER_PRESETS, renderProviders, collectProviders } from './providers.js';
import { renderRoleMappings, renderDefaultRoleOptions, collectRoleMappings, addLibChip } from './roles.js';
import { updateRbacManagementUi, updateEmailAllowlistUi } from './uiToggles.js';
import { testProvider } from './testConnection.js';

export default function (view) {
    setDirtyView(view);

    // Warn on reload/tab-close while edits are pending; in-SPA navigation relies on the visible indicator.
    window.addEventListener('beforeunload', beforeUnloadGuard);

    // Any field edit marks the form dirty. Programmatic assignments don't emit input/change, so they don't.
    view.addEventListener('input', function (e) {
        setDirty(true);
        // keep the Fallback-role dropdown in step with unsaved role renames
        if (e.target && e.target.id && e.target.id.indexOf('role_name_') === 0) renderDefaultRoleOptions(view);
        // live warning when a pinned Issuer URL is edited without re-running Test Connection
        if (e.target && e.target.id && e.target.id.indexOf('prov_pinnedissuer_') === 0) {
            var idx = e.target.id.slice('prov_pinnedissuer_'.length);
            var verified = e.target.dataset.verified || '';
            var statusEl = view.querySelector('[data-pin-status="' + idx + '"]');
            if (statusEl && verified) {
                statusEl.textContent = e.target.value === verified
                    ? 'Pinned via Test Connection - token endpoint, JWKS URI & userinfo endpoint are locked to the values returned for this issuer.'
                    : 'Issuer URL changed - run Test Connection to re-pin before you can save.';
            }
        }
    }, true);
    view.addEventListener('change', function () { setDirty(true); }, true);
    view.querySelector('#manageUserPolicy').addEventListener('change', function () { updateRbacManagementUi(view); });
    view.querySelector('#requireVerifiedEmail').addEventListener('change', function () { updateEmailAllowlistUi(view); });

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

        Promise.all([
            ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/Config/Libraries')).catch(function () { return {}; }),
            ApiClient.getJSON(ApiClient.getUrl('sso/OIDC/Config/Ratings')).catch(function () { return []; })
        ]).then(function (results) {
            setLibs(results[0] || {});
            setRatings(results[1] || []);
        }).then(function () {
            return ApiClient.getPluginConfiguration(pluginId);
        }).then(function (config) {
            config.Providers = config.Providers || [];
            config.RoleMappings = config.RoleMappings || [];
            setCfg(config);
            renderProviders(view);
            renderRoleMappings(view); // also fills the #defaultRoleName <select> from cfg
            view.querySelector('#autoCreateUsers').checked = cfg.AutoCreateUsers !== false;
            view.querySelector('#migrateLocalUsers').checked = cfg.MigrateLocalUsers === true;
            view.querySelector('#blockPrivateNetworkAuthorities').checked = cfg.BlockPrivateNetworkAuthorities === true;
            view.querySelector('#allowedGroups').value = listToText(cfg.AllowedGroups);
            view.querySelector('#requireVerifiedEmail').checked = cfg.RequireVerifiedEmail === true;
            view.querySelector('#allowedEmailDomains').value = listToText(cfg.AllowedEmailDomains);
            view.querySelector('#allowedEmails').value = listToText(cfg.AllowedEmails);
            view.querySelector('#linkExistingUsersByEmail').checked = cfg.LinkExistingUsersByEmail === true;
            view.querySelector('#manageUserPolicy').checked = cfg.ManageUserPolicy !== false;
            view.querySelector('#enableLibraryAccessManagement').checked = cfg.EnableLibraryAccessManagement !==false;
            updateRbacManagementUi(view);
            updateEmailAllowlistUi(view);
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
                c.classList.add('oidc-hidden');
            });
            this.classList.add('is-active');
            this.setAttribute('aria-selected', 'true');
            view.querySelector('#tab-' + this.getAttribute('data-tab')).classList.remove('oidc-hidden');
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
            ClientId: '', ClientSecret: '', ClientSecretFile: '', Scopes: 'openid profile email',
            RoleClaim: 'groups', UsernameClaim: 'preferred_username',
            DisplayNameClaim: 'name', EmailClaim: 'email', PictureClaim: 'picture',
            SyncProfileImage: true, SyncDisplayName: false,
            Enabled: true, ButtonColor: DEFAULT_BUTTON_COLOR,
            ButtonIcon: '', AdditionalParameters: '',
            StrictAccessTokenValidation: true,
            AllowLoopbackAuthority: false, AllowLinkLocalAuthority: false,
            TrustedForEmailLinking: false,
            PinnedAuthority: '', PinnedIssuer: '', PinnedTokenEndpoint: '', PinnedJwksUri: '',
            PinnedUserInfoEndpoint: '', PinnedAuthorizeEndpoint: ''
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
            EnableSubtitleManagement: false, MaxParentalRatingName: '', MaxParentalRating: null
        });
        renderRoleMappings(view);
        setDirty(true);
    });

    // Save
    view.querySelector('#btnSave').addEventListener('click', function () {
        if (!cfg) return;

        // Block save on a duplicate Provider ID; the server rejects it too, this just saves a round-trip.
        var idCounts = {};
        var duplicateIds = [];
        view.querySelectorAll('#providerList .oidc-card').forEach(function (card, idx) {
            var id = (gval(view, 'prov_id_' + idx) || '').trim().toLowerCase();
            if (!id) return;
            idCounts[id] = (idCounts[id] || 0) + 1;
            if (idCounts[id] === 2) duplicateIds.push(id);
        });
        if (duplicateIds.length > 0) {
            Dashboard.alert({
                title: 'Duplicate Provider ID',
                message: 'Provider ID(s) used by more than one provider: ' + duplicateIds.join(', ') +
                    '.\n\nEach provider must have a unique Provider ID.'
            });
            return;
        }

        // Block save if a pinned Issuer URL was edited without re-running Test Connection;
        // otherwise the save silently re-pins to an unverified value.
        var unverified = [];
        view.querySelectorAll('#providerList .oidc-card').forEach(function (card, idx) {
            var issuerEl = view.querySelector('#prov_pinnedissuer_' + idx);
            if (!issuerEl) return;
            var verified = issuerEl.dataset.verified || '';
            if (verified && issuerEl.value !== verified) {
                unverified.push(gval(view, 'prov_name_' + idx) || gval(view, 'prov_id_' + idx) || ('#' + (idx + 1)));
            }
        });
        if (unverified.length > 0) {
            Dashboard.alert({
                title: 'Issuer URL changed',
                message: 'Provider(s) with an edited, unverified Issuer URL: ' + unverified.join(', ') +
                    '.\n\nRun Test Connection to re-pin before saving.'
            });
            return;
        }

        // Warn (not block) if any enabled provider has no pins - TOFU will apply on first auth.
        var unpinned = (cfg.Providers || []).filter(function (p, idx) {
            var el = view.querySelector('#prov_pinnedissuer_' + idx);
            return p.Enabled !== false && !(el && el.dataset.verified);
        });
        if (unpinned.length > 0) {
            var names = unpinned.map(function (p) { return p.DisplayName || p.ProviderId; }).join(', ');
            if (!window.confirm('Provider(s) without endpoint pins: ' + names + '.\n\nEndpoints will be trusted on first login (TOFU). Run Test Connection to eliminate this window.\n\nSave anyway?')) {
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
        cfg.RequireVerifiedEmail = gchk(view, 'requireVerifiedEmail');
        cfg.AllowedEmailDomains = textToList(gval(view, 'allowedEmailDomains'));
        cfg.AllowedEmails = textToList(gval(view, 'allowedEmails'));
        cfg.LinkExistingUsersByEmail = gchk(view, 'linkExistingUsersByEmail');
        cfg.ManageUserPolicy = gchk(view, 'manageUserPolicy');
        cfg.EnableLibraryAccessManagement = gchk(view, 'enableLibraryAccessManagement');
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
            // Provider order is the login-button order (the server iterates the list as-is).
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
    // Also: "Prefill for <IdP>" - one-shot fill of claim/scope/icon fields.
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
            if (svg) svg.classList.toggle('oidc-hidden', !custom);
            if (file) file.classList.toggle('oidc-hidden', !custom);
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
