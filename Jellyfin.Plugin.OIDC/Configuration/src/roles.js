// Role-mapping card rendering and its round-trip back into config objects.
import { el, esc, emptyState, gval, gchk } from './dom.js';
import { fld, chk, permGroup } from './fields.js';
import { cfg, libs, ratings } from './state.js';
import { STRINGS } from './strings.js';

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

    var opts = '<option value="">' + esc(STRINGS.role.unrestrictedOption) + '</option>';
    var known = false;
    ratings.forEach(function (r) {
        var sel = (r.Name.toLowerCase() === selName.toLowerCase()) ? ' selected' : '';
        if (sel) { known = true; }
        opts += '<option value="' + esc(r.Name) + '"' + sel + '>' + esc(r.Name) + '</option>';
    });

    if (selName && !known) {
        opts += '<option value="' + esc(selName) + '" selected>' + esc(selName) + esc(STRINGS.role.notDefinedSuffix) + '</option>';
    } else if (!selName && legacy != null) {
        opts += '<option value="__legacy__" selected disabled>' + esc(STRINGS.role.customScorePrefix) + legacy + esc(STRINGS.role.customScoreSuffix) + '</option>';
    }
    return opts;
}

// Fills the "Default Role" <select> on the Role Mappings tab from the current role names.
// Reads live DOM values when cards are present (so it tracks unsaved renames), else cfg.
export function renderDefaultRoleOptions(view) {
    var sel = view.querySelector('#defaultRoleName');
    if (!sel) return;
    var current = sel.value || cfg.DefaultRoleName || '';
    var source = view.querySelector('#roleMappingList .oidc-item-card')
        ? collectRoleMappings(view).map(function (m) { return m.RoleName; })
        : (cfg.RoleMappings || []).map(function (m) { return m.RoleName; });
    var names = [];
    source.forEach(function (n) {
        n = (n || '').trim();
        if (n && names.every(function (x) { return x.toLowerCase() !== n.toLowerCase(); })) names.push(n);
    });
    var opts = '<option value="">' + esc(STRINGS.role.noneOption) + '</option>';
    if (current && names.every(function (x) { return x.toLowerCase() !== current.toLowerCase(); })) {
        opts += '<option value="' + esc(current) + '">' + esc(current) + esc(STRINGS.role.notDefinedRoleSuffix) + '</option>';
    }
    names.forEach(function (n) { opts += '<option value="' + esc(n) + '">' + esc(n) + '</option>'; });
    sel.innerHTML = opts;
    sel.value = current;
}

export function renderRoleMappings(view) {
    var container = view.querySelector('#roleMappingList');
    container.innerHTML = '';
    if (!cfg.RoleMappings.length) {
        container.innerHTML = emptyState(STRINGS.rolesTab.emptyState);
        renderDefaultRoleOptions(view);
        return;
    }
    cfg.RoleMappings.forEach(function (m, idx) {
        var card = document.createElement('details');
        card.className = 'oidc-item-card oidc-role';
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
        var provOpts = el('option', { value: '', selected: !m.ProviderFilter }, STRINGS.role.allProviders) +
            (cfg.Providers || []).map(function (p) {
                return el('option', { value: p.ProviderId, selected: m.ProviderFilter === p.ProviderId },
                    esc(p.DisplayName || p.ProviderId));
            }).join('');
        // Collapsed summary: role name + Admin badge + a one-line scope (provider,
        // library access). A role without a name opens expanded.
        var provLabel = m.ProviderFilter
            ? (((cfg.Providers || []).find(function (p) { return p.ProviderId === m.ProviderFilter; }) || {}).DisplayName || m.ProviderFilter)
            : STRINGS.role.allProviders;
        var libLabel = m.EnableAllLibraries
            ? STRINGS.role.allLibraries
            : (selectedLibs.length ? selectedLibs.length + (selectedLibs.length === 1 ? STRINGS.role.librarySingular : STRINGS.role.libraryPlural) : STRINGS.role.noLibraryAccess);
        var scopeParts = [provLabel, libLabel];

        card.innerHTML = el('summary', { class: 'oidc-role-summary' },
            el('h4', null, STRINGS.role.roleNamePrefix + esc(m.RoleName || STRINGS.role.newRoleName)) +
            (m.IsAdmin ? el('span', { class: 'oidc-badge' }, STRINGS.role.adminBadge) : '') +
            el('span', { class: 'oidc-role-scope' }, esc(scopeParts.join('  ·  ')))) +
            fld(STRINGS.role.roleNameLabel, 'text', 'role_name_' + idx, m.RoleName, STRINGS.role.roleNamePlaceholder, true) +
            el('div', { class: 'selectContainer full oidc-mb-md' },
                el('label', null, STRINGS.role.providerFilterLabel + el('span', { class: 'fieldDescription' }, STRINGS.role.providerFilterHint)) +
                el('select', { is: 'emby-select', id: 'role_provfilter_' + idx }, provOpts)) +
            el('div', { class: 'oidc-field full oidc-mt-sm' },
                el('label', null, STRINGS.role.permissionsLabel + el('span', { class: 'fieldDescription' }, STRINGS.role.permissionsHint)) +
                el('p', { class: 'fieldDescription oidc-hint-tight' }, STRINGS.role.permissionsNote) +
                el('div', { class: 'oidc-checkbox-row oidc-mt-xs' }, chk('role_admin_' + idx, STRINGS.role.administrator, m.IsAdmin)) +
                permGroup(STRINGS.role.playbackGroup,
                    chk('role_playback_' + idx, STRINGS.role.playbackGroup, m.EnableMediaPlayback !== false) +
                    chk('role_transcode_' + idx, STRINGS.role.transcoding, m.EnableTranscoding !== false) +
                    chk('role_remote_' + idx, STRINGS.role.remoteAccess, m.EnableRemoteAccess !== false)) +
                permGroup(STRINGS.role.liveTvGroup,
                    chk('role_livetv_' + idx, STRINGS.role.liveTvAccess, m.EnableLiveTv) +
                    chk('role_livetvmgmt_' + idx, STRINGS.role.liveTvRecordingManagement, m.EnableLiveTvManagement)) +
                permGroup(STRINGS.role.contentManagementGroup,
                    chk('role_collections_' + idx, STRINGS.role.collections, m.EnableCollectionManagement) +
                    chk('role_subtitles_' + idx, STRINGS.role.subtitles, m.EnableSubtitleManagement) +
                    chk('role_delete_' + idx, STRINGS.role.deleteContent, m.EnableContentDeletion))) +
            el('div', { class: 'oidc-field full oidc-mt-md' },
                el('div', { class: 'oidc-perm-title' }, STRINGS.role.libraryAccessTitle) +
                el('div', { class: 'oidc-checkbox-row' }, chk('role_alllibs_' + idx, STRINGS.role.allLibraries, m.EnableAllLibraries)) +
                el('label', { class: 'oidc-mt-sm2' }, STRINGS.role.specificLibrariesLabel + el('span', { class: 'fieldDescription' }, STRINGS.role.specificLibrariesHint)) +
                el('select', { is: 'emby-select', id: 'role_libadd_' + idx }, el('option', { value: '' }, STRINGS.role.selectLibraryOption) + libOpts) +
                el('button', { is: 'emby-button', type: 'button', class: 'oidc-btn-secondary oidc-mt-sm oidc-w-fit', 'data-action': 'add-lib', 'data-idx': idx }, STRINGS.role.addLibraryBtn) +
                el('div', { id: 'role_libs_' + idx, class: 'oidc-library-list' })) +
            el('div', { class: 'selectContainer oidc-mt-md' },
                el('label', null, STRINGS.role.maxParentalRatingLabel + el('span', { class: 'fieldDescription' }, STRINGS.role.maxParentalRatingHint)) +
                el('select', { is: 'emby-select', id: 'role_maxrating_' + idx }, ratingOptions(m))) +
            el('div', { class: 'oidc-mt-md' },
                el('button', { is: 'emby-button', type: 'button', class: 'oidc-btn-remove', 'data-action': 'remove-role', 'data-idx': idx }, STRINGS.common.removeBtn));
        card.open = !m.RoleName;
        container.appendChild(card);
        var libCont = view.querySelector('#role_libs_' + idx);
        selectedLibs.forEach(function (libId) { addLibChip(libCont, libId); });
    });
    renderDefaultRoleOptions(view);
}

export function collectRoleMappings(view) {
    var result = [];
    view.querySelectorAll('#roleMappingList .oidc-item-card').forEach(function (card, idx) {
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
