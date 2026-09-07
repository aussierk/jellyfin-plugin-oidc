// Pure DOM show/hide state toggles driven by a checkbox elsewhere on the page.

// Greys out the role-mapping list + fallback role and shows a hint while "Manage user policy
// from role mappings" is off, since neither has any effect in that state.
export function updateRbacManagementUi(view) {
    var managed = view.querySelector('#manageUserPolicy').checked;
    var list = view.querySelector('#roleMappingList');
    var fallback = view.querySelector('#defaultRoleName');
    var addBtn = view.querySelector('#btnAddRoleMapping');
    var hint = view.querySelector('#roleMappingsInactiveHint');
    // .oidc-dimmed carries both opacity and pointer-events:none; harmless on fallback/addBtn
    // (already blocked via .disabled below), needed on list (has no .disabled of its own).
    [list, fallback, addBtn].forEach(function (node) {
        if (node) node.classList.toggle('oidc-dimmed', !managed);
    });
    if (fallback) fallback.disabled = !managed;
    if (addBtn) addBtn.disabled = !managed;
    if (hint) hint.hidden = managed;
}

// Greys out the email allowlist fields while "Require a verified email" is off, since an
// unverified email can't be trusted as an admission signal and the lists are inert either way.
// If the lists have content while inert, an explicit warning replaces the silent dimming.
export function updateEmailAllowlistUi(view) {
    var active = view.querySelector('#requireVerifiedEmail').checked;
    var fields = view.querySelector('#emailAllowlistFields');
    if (!fields) return;
    // #emailAllowlistFields is display:contents (so it doesn't add an extra grid track),
    // which means it has no box of its own - the dimmed look has to go on its children instead.
    fields.querySelectorAll('.oidc-field').forEach(function (field) {
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
