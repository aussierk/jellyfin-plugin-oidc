// <textarea> ↔ string[] for the allowlist fields (one entry per line or comma-separated).
export function listToText(arr) { return (arr || []).join('\n'); }
export function textToList(str) {
    return (str || '').split(/[\n,]+/).map(function (s) { return s.trim(); }).filter(Boolean);
}
