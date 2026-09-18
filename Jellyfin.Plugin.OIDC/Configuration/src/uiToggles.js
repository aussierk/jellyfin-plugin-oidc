// Pure DOM show/hide state toggles driven by a checkbox elsewhere on the page.

// Dims the role-mapping controls while policy management is off (they're inert either way).
// libraryAccessContainer is dimmed as a wrapper, not the emby-checkbox input, which broke its checkmark.
export function updateRbacManagementUi(view) {
    var managed = view.querySelector('#manageUserPolicy').checked;
    var list = view.querySelector('#roleMappingList');
    var fallback = view.querySelector('#defaultRoleName');
    var addBtn = view.querySelector('#btnAddRoleMapping');
    var libraryAccessContainer = view.querySelector('#enableLibraryAccessManagementContainer');
    var hint = view.querySelector('#roleMappingsInactiveHint');
    [list, fallback, addBtn, libraryAccessContainer].forEach(function (node) {
        if (node) node.classList.toggle('oidc-dimmed', !managed);
    });
    if (fallback) fallback.disabled = !managed;
    if (addBtn) addBtn.disabled = !managed;
    if (hint) hint.hidden = managed;
}

// Dims the email allowlist fields while "Require a verified email" is off; warns instead if they're populated.
export function updateEmailAllowlistUi(view) {
    var active = view.querySelector('#requireVerifiedEmail').checked;
    var fields = view.querySelector('#emailAllowlistFields');
    if (!fields) return;
    // display:contents has no box of its own, so the dimmed look goes on the children instead.
    fields.querySelectorAll('.oidc-allowlist-field').forEach(function (field) {
        field.classList.toggle('oidc-dimmed', !active);
    });
    fields.querySelectorAll('textarea').forEach(function (textarea) {
        textarea.disabled = !active;
    });

    var warn = view.querySelector('#emailAllowlistInertWarning');
    if (warn) {
        var hasContent = Array.prototype.some.call(
            fields.querySelectorAll('textarea'), function (t) { return t.value.trim() !== ''; });
        warn.hidden = active || !hasContent;
    }
}
