// Module-level state shared across the page controller: loaded config, per-page-show
// libraries/ratings, and the unsaved-changes flag. Reads and property mutation work through the
// live ES binding; *reassigning* a binding (fresh load, test hook) must go through the setters.
import { STRINGS } from './strings.js';

export const pluginId = 'e1c020c5-3972-4b7b-9538-ee4934cc902c';

export let cfg = null;
export let libs = {};
export let ratings = []; // [{ Name, Score, SubScore }] from Jellyfin, for the Max Parental Rating picker

// Unsaved-changes tracking. `dirty` flips true on any edit and false after a load or a
// successful save; the sticky save bar and the beforeunload guard both read it.
export let dirty = false;
export let dirtyView = null;

export function setCfg(v) { cfg = v; }
export function setLibs(v) { libs = v; }
export function setRatings(v) { ratings = v; }
export function setDirtyView(v) { dirtyView = v; }

export function setDirty(v) {
    dirty = v;
    if (!dirtyView) return;
    var s = dirtyView.querySelector('#saveStatus');
    if (s) s.textContent = v ? STRINGS.saveFlow.unsavedChanges : '';
    var btn = dirtyView.querySelector('#btnSave');
    if (btn) btn.classList.toggle('oidc-save-dirty', v);
}

export function beforeUnloadGuard(e) {
    if (!dirty) return undefined;
    e.preventDefault();
    e.returnValue = '';
    return '';
}

// Test-only hook: points this module's state at fixture data, bypassing index.js.
export function __setTestState(state) {
    if (Object.prototype.hasOwnProperty.call(state, 'cfg')) cfg = state.cfg;
    if (Object.prototype.hasOwnProperty.call(state, 'libs')) libs = state.libs;
    if (Object.prototype.hasOwnProperty.call(state, 'ratings')) ratings = state.ratings;
    if (Object.prototype.hasOwnProperty.call(state, 'dirtyView')) dirtyView = state.dirtyView;
}
