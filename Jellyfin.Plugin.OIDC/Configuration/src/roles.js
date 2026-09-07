// Role-mapping card rendering and its round-trip back into config objects.
import { el, esc, emptyState, gval, gchk } from './dom.js';
import { fld, chk, permGroup } from './fields.js';
import { cfg, libs, ratings } from './state.js';

export function addLibChip(container, libId) {
    var chip = document.createElement('span');
    chip.className = 'oidc-library-chip';
    chip.setAttribute('data-lib-id', libId);
    chip.innerHTML = esc(libs[libId] || libId) + ' <span class="remove">&times;</span>';
    container.appendChild(chip);
}

// Options for a role card's Max Parental Rating <select>. Value = rating name (what we store).
// Pre-selects m.MaxParentalRatingName; falls back to matching a legacy numeric m.MaxParentalRating
// against the rating list, and shows a disabled "Custom (n)" entry if that legacy value is unknown.
export function ratingOptions(m) {
    var selName = (m.MaxParentalRatingName || '').trim();
    var legacy = (typeof m.MaxParentalRating === 'number') ? m.MaxParentalRating : null;
    if (!selName && legacy != null) {
        var hit = ratings.find(function (r) { return r.Score === legacy; });
        if (hit) { selName = hit.Name; }
    }

    var opts = '<option value="">- Unrestricted -</option>';
    var known = false;
    ratings.forEach(function (r) {
        var sel = (r.Name.toLowerCase() === selName.toLowerCase()) ? ' selected' : '';
        if (sel) { known = true; }
        opts += '<option value="' + esc(r.Name) + '"' + sel + '>' + esc(r.Name) + '</option>';
    });

    if (selName && !known) {
        opts += '<option value="' + esc(selName) + '" selected>' + esc(selName) + ' (not defined on this server)</option>';
    } else if (!selName && legacy != null) {
        opts += '<option value="__legacy__" selected disabled>Custom score ' + legacy + ' (re-pick to update)</option>';
    }
    return opts;
}

// Fills the "Fallback role" <select> on the Role Mappings tab from the current role names.
// Reads live DOM values when cards are present (so it tracks unsaved renames), else cfg.
export function renderDefaultRoleOptions(view) {
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
    var opts = '<option value="">- none -</option>';
    if (current && names.every(function (x) { return x.toLowerCase() !== current.toLowerCase(); })) {
        opts += '<option value="' + esc(current) + '">' + esc(current) + ' (not a defined role)</option>';
    }
    names.forEach(function (n) { opts += '<option value="' + esc(n) + '">' + esc(n) + '</option>'; });
    sel.innerHTML = opts;
    sel.value = current;
}

export function renderRoleMappings(view) {
    var container = view.querySelector('#roleMappingList');
    container.innerHTML = '';
    if (!cfg.RoleMappings.length) {
        container.innerHTML = emptyState(
            'No role mappings - signed-in users get the fallback role selected above, '
            + 'or no extra permissions if that is "- none -".');
        renderDefaultRoleOptions(view);
        return;
    }
    cfg.RoleMappings.forEach(function (m, idx) {
        var card = document.createElement('details');
        card.className = 'oidc-card oidc-role';
        var libOpts = Object.keys(libs).map(function (id) {
            return el('option', { value: id }, esc(libs[id]));
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
        var provOpts = el('option', { value: '', selected: !m.ProviderFilter }, 'All providers (global)') +
            (cfg.Providers || []).map(function (p) {
                return el('option', { value: p.ProviderId, selected: m.ProviderFilter === p.ProviderId },
                    esc(p.DisplayName || p.ProviderId));
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

        card.innerHTML = el('summary', { class: 'oidc-role-summary' },
            el('h4', null, 'Role: ' + esc(m.RoleName || 'New Role')) +
            (m.IsAdmin ? el('span', { class: 'oidc-badge' }, 'Admin') : '') +
            el('span', { class: 'oidc-role-scope' }, esc(scopeParts.join('  ·  ')))) +
            fld('Role Name', 'text', 'role_name_' + idx, m.RoleName, 'Must match IdP role claim value', true) +
            el('div', { class: 'oidc-field full oidc-mb-md' },
                el('label', null, 'Provider Filter ' + el('span', { class: 'oidc-hint' }, '(restrict to one provider - leave blank to apply to all)')) +
                el('select', { is: 'emby-select', id: 'role_provfilter_' + idx }, provOpts)) +
            el('div', { class: 'oidc-field full oidc-mt-sm' },
                el('label', null, 'Permissions ' + el('span', { class: 'oidc-hint' }, '(Administrator grants everything below)')) +
                el('p', { class: 'oidc-hint oidc-hint-tight' }, 'When a user matches several roles, all their permissions are combined and the strictest parental rating wins.') +
                el('div', { class: 'oidc-checkbox-row oidc-mt-xs' }, chk('role_admin_' + idx, 'Administrator', m.IsAdmin)) +
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
                    chk('role_delete_' + idx, 'Delete content', m.EnableContentDeletion))) +
            el('div', { class: 'oidc-field full oidc-mt-md' },
                el('div', { class: 'oidc-perm-title' }, 'Library access') +
                el('div', { class: 'oidc-checkbox-row' }, chk('role_alllibs_' + idx, 'All libraries', m.EnableAllLibraries)) +
                el('label', { class: 'oidc-mt-sm2' }, 'Specific libraries ' + el('span', { class: 'oidc-hint' }, '(used when "All libraries" is off)')) +
                el('select', { is: 'emby-select', id: 'role_libadd_' + idx }, el('option', { value: '' }, '-- Select library --') + libOpts) +
                el('button', { type: 'button', class: 'oidc-btn-secondary oidc-mt-sm oidc-w-fit', 'data-action': 'add-lib', 'data-idx': idx }, 'Add Library') +
                el('div', { id: 'role_libs_' + idx, class: 'oidc-library-list' })) +
            el('div', { class: 'oidc-field oidc-mt-md' },
                el('label', null, 'Max Parental Rating ' + el('span', { class: 'oidc-hint' }, '(empty = unrestricted; strictest wins when several roles match)')) +
                el('select', { is: 'emby-select', id: 'role_maxrating_' + idx }, ratingOptions(m))) +
            el('div', { class: 'oidc-mt-md' },
                el('button', { type: 'button', class: 'oidc-btn-remove', 'data-action': 'remove-role', 'data-idx': idx }, 'Remove'));
        card.open = !m.RoleName;
        container.appendChild(card);
        var libCont = view.querySelector('#role_libs_' + idx);
        selectedLibs.forEach(function (libId) { addLibChip(libCont, libId); });
    });
    renderDefaultRoleOptions(view);
}

export function collectRoleMappings(view) {
    var result = [];
    view.querySelectorAll('#roleMappingList .oidc-card').forEach(function (card, idx) {
        var chips = view.querySelectorAll('#role_libs_' + idx + ' .oidc-library-chip');
        var libIds = [];
        chips.forEach(function (c) { libIds.push(c.getAttribute('data-lib-id')); });
        var mr = gval(view, 'role_maxrating_' + idx);
        var mrName = (mr && mr !== '__legacy__') ? mr : '';
        // Preserve an unresolved legacy numeric score only while the admin hasn't picked a name.
        var mrLegacy = mrName ? null : (cfg.RoleMappings[idx] ? cfg.RoleMappings[idx].MaxParentalRating : null);
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
            MaxParentalRatingName: mrName,
            MaxParentalRating: mrLegacy
        });
    });
    return result;
}
