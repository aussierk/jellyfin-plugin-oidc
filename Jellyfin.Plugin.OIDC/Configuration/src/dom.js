// Small DOM/escaping primitives with no dependency on plugin state - used by every other
// module in this page controller.
import { STRINGS } from './strings.js';

// Escapes HTML text and quoted attribute values; call sites interpolate IdP discovery strings
// into value="..." / href="...", so the quote replacements matter.
export function esc(str) {
    var d = document.createElement('div');
    d.textContent = str == null ? '' : String(str);
    return d.innerHTML.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// Builds one element's HTML from an attributes object. Attr values are esc()'d; a BOOL_ATTRS
// name is written bare when truthy; `inner` is raw (already-composed) HTML. Only VOID_TAGS may
// self-close; a non-void tag written as '<span />' stays open in HTML and eats the rest of the page.
var BOOL_ATTRS = { checked: 1, selected: 1, disabled: 1, open: 1, readonly: 1, hidden: 1 };
var VOID_TAGS = { input: 1, br: 1, hr: 1, img: 1 };
export function el(tag, attrs, inner) {
    var html = '<' + tag;
    Object.keys(attrs || {}).forEach(function (name) {
        var v = attrs[name];
        if (v === false || v == null) return;
        if (BOOL_ATTRS[name]) {
            if (v) html += ' ' + name;
            return;
        }
        html += ' ' + name + '="' + esc(v) + '"';
    });
    if (VOID_TAGS[tag]) return html + ' />';
    return html + '>' + (inner == null ? '' : inner) + '</' + tag + '>';
}

export function gval(view, id) {
    var el = view.querySelector('#' + id);
    return el ? el.value : '';
}

export function gchk(view, id) {
    var el = view.querySelector('#' + id);
    return el ? el.checked : false;
}

export function sval(view, id, value) {
    var el = view.querySelector('#' + id);
    if (el) el.value = value;
}

export function schk(view, id, checked) {
    var el = view.querySelector('#' + id);
    if (el) el.checked = checked;
}

export function emptyState(msg) {
    return el('div', { class: 'oidc-empty' }, esc(msg));
}

// Copies the #srcId field's value to the clipboard and flashes btn's text to "Copied" for 1.2s.
// #srcId is a hidden input, so there's no selectable/focusable element for an execCommand('copy')
// fallback to act on; without the async Clipboard API (non-secure-context browsers) this is a no-op.
export function copyToClipboard(view, srcId, btn) {
    var src = view.querySelector('#' + srcId);
    if (!src || !navigator.clipboard) return;
    navigator.clipboard.writeText(src.value).then(function () {
        var orig = btn.textContent;
        btn.textContent = STRINGS.copyBtnCopied;
        setTimeout(function () { btn.textContent = orig; }, 1200);
    }).catch(function () {});
}
