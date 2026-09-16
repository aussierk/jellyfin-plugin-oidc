// Small labelled-input builders shared by the provider and role-mapping cards.
import { el, esc } from './dom.js';

// is="emby-input" creates its OWN <label> on upgrade (emby-input.js createdCallback: reads
// the `label` attribute, inserts a <label class="inputLabel"> as its previous sibling) - it
// does not read or use any <label> we write ourselves. Passing `label` here is what actually
// wires up that native label; a hand-written <label> alongside it would just be a dead,
// duplicate element competing for the same vertical space.
export function fld(label, type, id, value, placeholder, full) {
    return el('div', { class: full ? 'inputContainer full' : 'inputContainer' },
        el('input', {
            is: 'emby-input', type: type, id: id, value: String(value || ''),
            label: label, placeholder: placeholder || null,
            autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false'
        }));
}

export function chk(id, label, checked) {
    return el('label', null,
        el('input', { type: 'checkbox', id: id, is: 'emby-checkbox', checked: !!checked }) +
        ' ' + el('span', null, esc(label)));
}

// A checkbox with a fieldDescription line underneath it (the common "toggle + explanation" shape
// used by the provider card's security section).
export function chkWithDesc(id, label, desc, checked) {
    return el('div', { class: 'checkboxContainer checkboxContainer-withDescription full' },
        el('label', null,
            el('input', { type: 'checkbox', id: id, is: 'emby-checkbox', checked: !!checked }) +
            ' ' + el('span', null, esc(label))) +
        el('div', { class: 'fieldDescription' }, esc(desc)));
}

// A titled cluster of related permission checkboxes for the role card.
export function permGroup(title, inner) {
    return el('div', { class: 'oidc-perm-group' },
        el('div', { class: 'oidc-perm-title' }, esc(title)) +
        el('div', { class: 'oidc-checkbox-row' }, inner));
}
