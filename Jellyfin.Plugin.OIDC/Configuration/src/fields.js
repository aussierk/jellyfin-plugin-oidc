// Small labelled-input builders shared by the provider and role-mapping cards.
import { el, esc } from './dom.js';

export function fld(label, type, id, value, placeholder, full) {
    return el('div', { class: full ? 'oidc-field full' : 'oidc-field' },
        el('label', { for: id }, esc(label)) +
        el('input', {
            is: 'emby-input', type: type, id: id, value: String(value || ''),
            placeholder: placeholder || null,
            autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false'
        }));
}

export function chk(id, label, checked) {
    return el('label', null,
        el('input', { type: 'checkbox', id: id, is: 'emby-checkbox', checked: !!checked }) +
        ' ' + esc(label));
}

// A titled cluster of related permission checkboxes for the role card.
export function permGroup(title, inner) {
    return el('div', { class: 'oidc-perm-group' },
        el('div', { class: 'oidc-perm-title' }, esc(title)) +
        el('div', { class: 'oidc-checkbox-row' }, inner));
}
