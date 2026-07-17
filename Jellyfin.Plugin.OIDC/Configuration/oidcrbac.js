var pluginId = 'e1c020c5-3972-4b7b-9538-ee4934cc902c';
var cfg = null;
var libs = {};

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
        '<input type="' + type + '" id="' + id + '" value="' + esc(String(value || '')) + '"' +
        (placeholder ? ' placeholder="' + esc(placeholder) + '"' : '') + ' />' +
        '</div>';
}

function chk(id, label, checked) {
    return '<label><input type="checkbox" id="' + id + '"' + (checked ? ' checked' : '') + ' /> ' + esc(label) + '</label>';
}

function addLibChip(container, libId) {
    var chip = document.createElement('span');
    chip.className = 'oidc-library-chip';
    chip.setAttribute('data-lib-id', libId);
    chip.innerHTML = esc(libs[libId] || libId) + ' <span class="remove">&times;</span>';
    container.appendChild(chip);
}

function renderProviders(view) {
    var container = view.querySelector('#providerList');
    container.innerHTML = '';
    cfg.Providers.forEach(function (p, idx) {
        var card = document.createElement('div');
        card.className = 'oidc-card';
        card.innerHTML = '<h4>' + esc(p.DisplayName || 'New Provider') +
            (p.Enabled ? ' <span style="color:#4caf50">&#9679;</span>' : ' <span style="color:#888">&#9679;</span>') +
            '</h4>' +
            '<div class="oidc-grid">' +
            fld('Provider ID', 'text', 'prov_id_' + idx, p.ProviderId, 'Unique identifier (e.g. keycloak)') +
            fld('Display Name', 'text', 'prov_name_' + idx, p.DisplayName, 'Shown on login button') +
            fld('Authority URL', 'text', 'prov_authority_' + idx, p.Authority, 'https://idp.example.com/realms/myrealm', true) +
            fld('Client ID', 'text', 'prov_clientid_' + idx, p.ClientId, '') +
            fld('Client Secret', 'password', 'prov_secret_' + idx, p.ClientSecret, '') +
            fld('Scopes', 'text', 'prov_scopes_' + idx, p.Scopes || 'openid profile email', '') +
            fld('Role Claim Path', 'text', 'prov_roleclaim_' + idx, p.RoleClaim || 'groups', 'e.g. groups or realm_access.roles') +
            fld('Username Claim', 'text', 'prov_userclaim_' + idx, p.UsernameClaim || 'preferred_username', '') +
            fld('Display Name Claim', 'text', 'prov_displayclaim_' + idx, p.DisplayNameClaim || 'name', '') +
            fld('Button Color', 'color', 'prov_color_' + idx, p.ButtonColor || '#4285F4', '') +
            fld('Additional Params', 'text', 'prov_params_' + idx, p.AdditionalParameters || '', 'key=val&key2=val2', true) +
            fld('Server Base URL (override)', 'text', 'prov_baseurl_' + idx, p.ServerBaseUrl || '', 'Optional: https://jellyfin.example.com — overrides auto-detected redirect_uri host', true) +
            '<div class="oidc-field"><label><input type="checkbox" id="prov_enabled_' + idx + '"' +
            (p.Enabled !== false ? ' checked' : '') + '/> Enabled</label></div>' +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_strict_access_' + idx + '"' +
            (p.StrictAccessTokenValidation !== false ? ' checked' : '') + '/> Strict access token validation</label>' +
            '<span style="font-size:0.8em;color:#aaa;margin-left:1.5em;">Only applies when the IdP issues JWT access tokens (e.g. Keycloak). Opaque access tokens (Google, default Authelia) are skipped automatically and unaffected by this setting. Uncheck if your IdP signs access tokens with a different key than the JWKS endpoint advertises.</span></div>' +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_allow_loopback_' + idx + '"' +
            (p.AllowLoopbackAuthority === true ? ' checked' : '') + '/> Allow loopback Authority</label>' +
            '<span style="font-size:0.8em;color:#aaa;margin-left:1.5em;">By default, an Authority resolving to a loopback address (127.0.0.1, ::1) is blocked. Enable this only if your IdP is intentionally hosted at loopback.</span></div>' +
            '<div class="oidc-field full"><label><input type="checkbox" id="prov_allow_linklocal_' + idx + '"' +
            (p.AllowLinkLocalAuthority === true ? ' checked' : '') + '/> Allow link-local Authority</label>' +
            '<span style="font-size:0.8em;color:#aaa;margin-left:1.5em;">By default, an Authority resolving to a link-local address (169.254.x.x, fe80::) is blocked. Enable this only if your IdP is intentionally hosted at a link-local address.</span></div>' +
            '<div class="oidc-field full" style="margin-top:0.5em;">' +
            '<label style="font-weight:600;font-size:0.9em;">Endpoint Pins ' +
            '<span style="font-weight:normal;color:#aaa;">— pre-fill from your IdP docs to eliminate first-use trust, or click Test Connection to fill automatically</span></label>' +
            '<div style="display:grid;grid-template-columns:1fr 1fr;gap:0.5em;margin-top:0.4em;">' +
            fld('Issuer', 'text', 'prov_pinnedissuer_' + idx, p.PinnedIssuer || '', 'https://idp.example.com/realms/myrealm') +
            fld('Token Endpoint', 'text', 'prov_pinnedtoken_' + idx, p.PinnedTokenEndpoint || '', 'https://idp.example.com/.../token') +
            fld('JWKS URI', 'text', 'prov_pinnedjwks_' + idx, p.PinnedJwksUri || '', 'https://idp.example.com/.../certs') +
            '</div></div>' +
            '</div>' +
            '<div style="margin-top:0.5em;display:flex;gap:0.5em;align-items:center;">' +
            '<button type="button" class="oidc-btn-secondary" data-action="test-provider" data-idx="' + idx + '">Test Connection</button>' +
            '<button type="button" class="oidc-btn-remove" data-action="remove-provider" data-idx="' + idx + '">Remove</button>' +
            '<span class="oidc-test-result" data-idx="' + idx + '" style="font-size:0.9em;"></span>' +
            '</div>';
        container.appendChild(card);
    });
}

function renderRoleMappings(view) {
    var container = view.querySelector('#roleMappingList');
    container.innerHTML = '';
    cfg.RoleMappings.forEach(function (m, idx) {
        var card = document.createElement('div');
        card.className = 'oidc-card';
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
        card.innerHTML = '<h4>Role: ' + esc(m.RoleName || 'New Role') + '</h4>' +
            '<div class="oidc-grid">' +
            fld('Role Name', 'text', 'role_name_' + idx, m.RoleName, 'Must match IdP role claim value') +
            fld('Priority', 'number', 'role_priority_' + idx, m.Priority || 0, 'Higher = takes precedence') +
            '</div>' +
            '<div class="oidc-field full" style="margin-bottom:0.5em;">' +
            '<label>Provider Filter <span style="font-size:0.8em;color:#aaa;">(restrict to one provider — leave blank to apply to all)</span></label>' +
            '<select id="role_provfilter_' + idx + '">' + provOpts + '</select>' +
            '</div>' +
            '<div class="oidc-checkbox-row">' +
            chk('role_admin_' + idx, 'Administrator', m.IsAdmin) +
            chk('role_alllibs_' + idx, 'All Libraries', m.EnableAllLibraries) +
            chk('role_livetv_' + idx, 'Live TV', m.EnableLiveTv) +
            chk('role_livetvmgmt_' + idx, 'Live TV Mgmt', m.EnableLiveTvManagement) +
            chk('role_playback_' + idx, 'Playback', m.EnableMediaPlayback !== false) +
            chk('role_remote_' + idx, 'Remote Access', m.EnableRemoteAccess !== false) +
            chk('role_transcode_' + idx, 'Transcoding', m.EnableTranscoding !== false) +
            chk('role_delete_' + idx, 'Delete Content', m.EnableContentDeletion) +
            chk('role_collections_' + idx, 'Collections', m.EnableCollectionManagement) +
            chk('role_subtitles_' + idx, 'Subtitles', m.EnableSubtitleManagement) +
            '</div>' +
            '<div class="oidc-field" style="margin-top:0.5em;">' +
            '<label>Libraries (when "All Libraries" is unchecked)</label>' +
            '<select id="role_libadd_' + idx + '"><option value="">-- Select library --</option>' + libOpts + '</select>' +
            '<button type="button" class="oidc-btn-secondary" style="margin-top:0.3em;width:fit-content;" data-action="add-lib" data-idx="' + idx + '">Add Library</button>' +
            '<div id="role_libs_' + idx + '" class="oidc-library-list"></div>' +
            '</div>' +
            '<div class="oidc-field" style="margin-top:0.5em;">' +
            '<label>Max Parental Rating (empty = unrestricted)</label>' +
            '<input type="number" id="role_maxrating_' + idx + '" value="' + (m.MaxParentalRating != null ? m.MaxParentalRating : '') + '" />' +
            '</div>' +
            '<div style="margin-top:0.5em;">' +
            '<button type="button" class="oidc-btn-remove" data-action="remove-role" data-idx="' + idx + '">Remove</button>' +
            '</div>';
        container.appendChild(card);
        var libCont = view.querySelector('#role_libs_' + idx);
        selectedLibs.forEach(function (libId) { addLibChip(libCont, libId); });
    });
}

function testProvider(view, idx) {
    var authority = gval(view, 'prov_authority_' + idx);
    var scopes = gval(view, 'prov_scopes_' + idx);
    var resultEl = view.querySelector('.oidc-test-result[data-idx="' + idx + '"]');
    if (!authority) {
        if (resultEl) { resultEl.style.color = '#c62828'; resultEl.textContent = 'Authority URL is required'; }
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
            if (resultEl) { resultEl.style.color = '#c62828'; resultEl.textContent = 'Failed: ' + result.Error; }
            Dashboard.alert({ title: 'Provider test failed', message: result.Error || 'Unknown error' });
        }
    }).catch(function (err) {
        var msg = (err && (err.statusText || err.message)) || 'Network error';
        if (resultEl) { resultEl.style.color = '#c62828'; resultEl.textContent = 'Failed: ' + msg; }
        Dashboard.alert({ title: 'Provider test failed', message: msg });
    });
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
            ButtonIcon: ''
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
            Priority: parseInt(gval(view, 'role_priority_' + idx)) || 0,
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
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();

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
            renderRoleMappings(view);
            view.querySelector('#defaultProvider').value = cfg.DefaultProvider || '';
            view.querySelector('#defaultRoleName').value = cfg.DefaultRoleName || '';
            view.querySelector('#autoCreateUsers').checked = cfg.AutoCreateUsers !== false;
            view.querySelector('#migrateLocalUsers').checked = cfg.MigrateLocalUsers === true;
            view.querySelector('#syncDisplayName').checked = cfg.SyncDisplayName === true;
            view.querySelector('#blockPrivateNetworkAuthorities').checked = cfg.BlockPrivateNetworkAuthorities === true;
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
                t.style.borderBottomColor = 'transparent';
                t.style.color = '#aaa';
            });
            view.querySelectorAll('.oidc-tab-content').forEach(function (c) {
                c.style.display = 'none';
            });
            this.style.borderBottomColor = '#00a4dc';
            this.style.color = '#00a4dc';
            view.querySelector('#tab-' + this.getAttribute('data-tab')).style.display = 'block';
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
            DisplayNameClaim: 'name', Enabled: true, ButtonColor: '#4285F4',
            ButtonIcon: '', AdditionalParameters: '',
            StrictAccessTokenValidation: true,
            AllowLoopbackAuthority: false, AllowLinkLocalAuthority: false,
            PinnedAuthority: '', PinnedIssuer: '', PinnedTokenEndpoint: '', PinnedJwksUri: ''
        });
        renderProviders(view);
    });

    // Add role mapping
    view.querySelector('#btnAddRoleMapping').addEventListener('click', function () {
        if (!cfg) return;
        cfg.RoleMappings = collectRoleMappings(view);
        cfg.RoleMappings.push({
            RoleName: '', Priority: 0, ProviderFilter: '', IsAdmin: false, EnableAllLibraries: false,
            LibraryIds: [], LibraryNames: [], EnableLiveTv: false,
            EnableLiveTvManagement: false, EnableMediaPlayback: true,
            EnableRemoteAccess: true, EnableTranscoding: true,
            EnableContentDeletion: false, EnableCollectionManagement: false,
            EnableSubtitleManagement: false, MaxParentalRating: null
        });
        renderRoleMappings(view);
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
        cfg.DefaultProvider = gval(view, 'defaultProvider');
        cfg.DefaultRoleName = gval(view, 'defaultRoleName');
        cfg.AutoCreateUsers = gchk(view, 'autoCreateUsers');
        cfg.MigrateLocalUsers = gchk(view, 'migrateLocalUsers');
        cfg.SyncDisplayName = gchk(view, 'syncDisplayName');
        cfg.BlockPrivateNetworkAuthorities = gchk(view, 'blockPrivateNetworkAuthorities');
        ApiClient.updatePluginConfiguration(pluginId, cfg).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save: ' + (err.message || err));
        });
    });

    // Event delegation for dynamic buttons in provider list
    view.querySelector('#providerList').addEventListener('click', function (e) {
        var btn = e.target.closest('[data-action]');
        if (!btn) return;
        var idx = parseInt(btn.getAttribute('data-idx'));
        if (btn.getAttribute('data-action') === 'remove-provider') {
            cfg.Providers = collectProviders(view);
            cfg.Providers.splice(idx, 1);
            renderProviders(view);
        } else if (btn.getAttribute('data-action') === 'test-provider') {
            testProvider(view, idx);
        }
    });

    // Event delegation for dynamic buttons in role mapping list
    view.querySelector('#roleMappingList').addEventListener('click', function (e) {
        if (e.target.classList.contains('remove')) {
            e.target.parentElement.remove();
            return;
        }
        var btn = e.target.closest('[data-action]');
        if (!btn) return;
        var idx = parseInt(btn.getAttribute('data-idx'));
        if (btn.getAttribute('data-action') === 'remove-role') {
            cfg.RoleMappings = collectRoleMappings(view);
            cfg.RoleMappings.splice(idx, 1);
            renderRoleMappings(view);
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
        }
    });
}
