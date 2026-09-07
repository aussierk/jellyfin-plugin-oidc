// Unit tests for the plugin's admin config page. Runs under vitest + jsdom (see
// vitest.config.js at the repo root) - `npm test` from the repo root, or `npm run test:watch`.
//
// Each test imports directly from the module it's exercising (dom.js, providers.js, roles.js,
// etc.) rather than from the built oidcrbac.js bundle - index.js (the page controller Jellyfin
// actually loads) only exports its default, so these are the real unit boundaries.
import { beforeEach, describe, expect, it } from 'vitest';
import { el, emptyState, esc, gchk, gval } from './dom.js';
import { __setTestState, setDirty } from './state.js';
import { listToText, textToList } from './utils.js';
import { spliceRegion } from './branding.js';
import { chk, fld, permGroup } from './fields.js';
import {
    authorityHost,
    backchannelLogoutUrl,
    collectIcon,
    collectProviders,
    envVarSuggestion,
    iconField,
    iconIsCustom,
    presetField,
    provGroup,
    renderProviders
} from './providers.js';
import { addLibChip, collectRoleMappings, ratingOptions, renderRoleMappings } from './roles.js';
import { updateEmailAllowlistUi, updateRbacManagementUi } from './uiToggles.js';
import { setTestStatus } from './testConnection.js';

beforeEach(() => {
    __setTestState({ cfg: null, libs: {}, ratings: [] });
});

describe('esc', () => {
    it('escapes HTML-significant characters for both text and attribute contexts', () => {
        expect(esc('<script>alert(1)</script>')).toBe('&lt;script&gt;alert(1)&lt;/script&gt;');
        expect(esc('a & b')).toBe('a &amp; b');
        expect(esc('say "hi"')).toBe('say &quot;hi&quot;');
        expect(esc("it's")).toBe('it&#39;s');
    });

    it('treats null/undefined as empty string', () => {
        expect(esc(null)).toBe('');
        expect(esc(undefined)).toBe('');
    });

    it('stringifies non-string input', () => {
        expect(esc(42)).toBe('42');
        expect(esc(true)).toBe('true');
    });
});

describe('el', () => {
    it('builds a self-closing void element with escaped attribute values', () => {
        expect(el('input', { type: 'text', value: 'a "quote"' }))
            .toBe('<input type="text" value="a &quot;quote&quot;" />');
    });

    it('never self-closes a non-void element, even with no inner content', () => {
        const html = el('span', { class: 'x' });
        expect(html).toBe('<span class="x"></span>');
        // A bare '<span ... />' would NOT close in HTML parsing - regression guard for that bug.
        expect(html.endsWith('/>')).toBe(false);
    });

    it('writes a boolean attribute bare when true and omits it when false/undefined', () => {
        expect(el('input', { type: 'checkbox', checked: true })).toContain(' checked');
        expect(el('input', { type: 'checkbox', checked: false })).not.toContain('checked');
        expect(el('input', { type: 'checkbox' })).not.toContain('checked');
    });

    it('omits an attribute whose value is null or undefined, but keeps an empty string', () => {
        const html = el('input', { type: 'text', placeholder: null, value: '' });
        expect(html).not.toContain('placeholder');
        expect(html).toContain('value=""');
    });

    it('does not escape `inner` - callers are expected to have already composed/escaped it', () => {
        expect(el('div', null, '<b>raw</b>')).toBe('<div><b>raw</b></div>');
    });
});

describe('listToText / textToList', () => {
    it('round-trips a plain array through newline-joined text', () => {
        expect(listToText(['a', 'b', 'c'])).toBe('a\nb\nc');
        expect(textToList('a\nb\nc')).toEqual(['a', 'b', 'c']);
    });

    it('treats commas as separators too, and trims/drops blanks', () => {
        expect(textToList('a, b,, c\n\nd ')).toEqual(['a', 'b', 'c', 'd']);
    });

    it('handles empty/undefined input without throwing', () => {
        expect(listToText(undefined)).toBe('');
        expect(textToList('')).toEqual([]);
        expect(textToList(undefined)).toEqual([]);
    });
});

describe('spliceRegion', () => {
    const START = '<!-- s -->';
    const END = '<!-- e -->';

    it('appends the block when no region markers are present', () => {
        expect(spliceRegion('', START, END, 'BLOCK')).toBe('BLOCK');
        expect(spliceRegion('existing', START, END, 'BLOCK')).toBe('existing\n\nBLOCK');
    });

    it('replaces the whole marked region (markers included) with the new block', () => {
        // The caller's `block` is expected to carry its own fresh markers (that's how the real
        // HTML_START/HTML_END snippet from the server is shaped) - spliceRegion doesn't re-wrap.
        const text = 'before\n' + START + 'old' + END + '\nafter';
        const newBlock = START + 'NEW' + END;
        expect(spliceRegion(text, START, END, newBlock)).toBe('before\n' + newBlock + '\nafter');
    });

    it('removes the region (markers included) when block is empty, collapsing extra blank lines', () => {
        const text = 'keep-before\n\n' + START + 'old' + END + '\n\nkeep-after';
        expect(spliceRegion(text, START, END, '')).toBe('keep-before\n\nkeep-after');
    });

    it('returns the text unchanged when there is no region and nothing to add', () => {
        expect(spliceRegion('plain text', START, END, '')).toBe('plain text');
    });
});

describe('authorityHost', () => {
    it('returns the host of a valid absolute URL', () => {
        expect(authorityHost('https://idp.example.com/realms/x')).toBe('idp.example.com');
    });

    it('falls back to stripping the scheme for a non-URL string', () => {
        expect(authorityHost('idp.example.com/realms/x')).toBe('idp.example.com');
    });

    it('returns empty string for falsy input', () => {
        expect(authorityHost('')).toBe('');
        expect(authorityHost(null)).toBe('');
    });
});

describe('envVarSuggestion', () => {
    it('slugifies the provider id into an uppercase env var name', () => {
        expect(envVarSuggestion({ ProviderId: 'keycloak' })).toBe('KEYCLOAK_CLIENT_SECRET');
    });

    it('replaces non-alphanumeric runs with a single underscore and trims edges', () => {
        expect(envVarSuggestion({ ProviderId: 'my-idp.01!' })).toBe('MY_IDP_01_CLIENT_SECRET');
    });

    it('falls back to PROVIDER when the id is blank or fully stripped', () => {
        expect(envVarSuggestion({ ProviderId: '' })).toBe('PROVIDER_CLIENT_SECRET');
        expect(envVarSuggestion({ ProviderId: '...' })).toBe('PROVIDER_CLIENT_SECRET');
    });
});

describe('iconIsCustom', () => {
    it('is false for a bundled icon key or an empty value', () => {
        expect(iconIsCustom('keycloak')).toBe(false);
        expect(iconIsCustom('')).toBe(false);
        expect(iconIsCustom(null)).toBe(false);
    });

    it('is true for anything not in the bundled key list (raw SVG, data: URI, unknown key)', () => {
        expect(iconIsCustom('<svg></svg>')).toBe(true);
        expect(iconIsCustom('data:image/png;base64,AAAA')).toBe(true);
    });
});

describe('fld / chk / permGroup / presetField / provGroup / emptyState', () => {
    it('fld renders a labelled input with escaped value and optional placeholder', () => {
        const html = fld('Display Name', 'text', 'prov_name_0', 'Bob "the" <builder>', 'placeholder', false);
        expect(html).toContain('<label for="prov_name_0">Display Name</label>');
        expect(html).toContain('value="Bob &quot;the&quot; &lt;builder&gt;"');
        expect(html).toContain('placeholder="placeholder"');
        expect(html).not.toContain('class="oidc-field full"');
    });

    it('fld adds the "full" class when requested and omits placeholder when blank', () => {
        const html = fld('Client ID', 'text', 'prov_clientid_0', '', '', true);
        expect(html).toContain('class="oidc-field full"');
        expect(html).not.toContain('placeholder');
    });

    it('chk renders checked/unchecked correctly', () => {
        expect(chk('a', 'A label', true)).toContain('checked');
        expect(chk('a', 'A label', false)).not.toContain('checked');
    });

    it('permGroup wraps its checkboxes with a title', () => {
        const html = permGroup('Playback', chk('x', 'X', true));
        expect(html).toContain('oidc-perm-title">Playback<');
        expect(html).toContain('oidc-checkbox-row');
    });

    it('presetField lists every configured IdP preset as an option', () => {
        const html = presetField(0);
        expect(html).toContain('id="prov_preset_0"');
        expect(html).toContain('<option value="keycloak">Keycloak</option>');
        expect(html).toContain('<option value="auth0">Auth0</option>');
    });

    it('provGroup wraps the header in a real <summary> so the disclosure widget shows the title', () => {
        // Regression guard: provGroup used to emit the header as plain text with no <summary>
        // wrapper, so browsers fell back to a generic "Details" label instead of e.g. "Connection".
        const html = provGroup('Connection', 'a hint', '<div>INNER</div>', true);
        expect(html).toMatch(/^<details class="oidc-section" open><summary>Connection/);
        expect(html).toContain('</summary><div class="oidc-grid">');
        expect(html).toContain('INNER');
    });

    it('provGroup omits the open attribute when not requested', () => {
        expect(provGroup('Advanced', null, '<div></div>', false)).not.toContain(' open');
    });

    it('emptyState escapes its message', () => {
        expect(emptyState("can't <do> that")).toBe('<div class="oidc-empty">can&#39;t &lt;do&gt; that</div>');
    });
});

describe('iconField', () => {
    it('shows the custom SVG/file inputs (not oidc-hidden) when the icon is not a bundled key', () => {
        const html = iconField(0, '<svg></svg>');
        expect(html).toContain('option value="custom" selected');
        expect(html).not.toMatch(/id="prov_icon_svg_0"[^>]*oidc-hidden/);
    });

    it('hides the custom inputs for a bundled icon key', () => {
        const html = iconField(0, 'keycloak');
        expect(html).toContain('option value="keycloak" selected');
        expect(html).toMatch(/id="prov_icon_svg_0"[^>]*oidc-hidden/);
        expect(html).toMatch(/id="prov_icon_file_0"[^>]*oidc-hidden/);
    });
});

describe('backchannelLogoutUrl', () => {
    it('uses the provider ServerBaseUrl override when set', () => {
        expect(backchannelLogoutUrl({ ProviderId: 'kc', ServerBaseUrl: 'https://jf.example.com/' }))
            .toBe('https://jf.example.com/sso/OIDC/BackchannelLogout/kc');
    });

    it('falls back to a placeholder when no ApiClient is available (e.g. in this test env)', () => {
        expect(backchannelLogoutUrl({ ProviderId: 'kc' }))
            .toBe('(your server URL)/sso/OIDC/BackchannelLogout/kc');
    });

    it('URL-encodes the provider id', () => {
        expect(backchannelLogoutUrl({ ProviderId: 'a b', ServerBaseUrl: 'https://jf.example.com' }))
            .toBe('https://jf.example.com/sso/OIDC/BackchannelLogout/a%20b');
    });
});

describe('ratingOptions', () => {
    beforeEach(() => {
        __setTestState({ ratings: [{ Name: 'G', Score: 1 }, { Name: 'PG-13', Score: 3 }] });
    });

    it('selects the option matching MaxParentalRatingName', () => {
        const html = ratingOptions({ MaxParentalRatingName: 'PG-13' });
        expect(html).toContain('value="PG-13" selected');
    });

    it('falls back to matching a legacy numeric score when no name is set', () => {
        const html = ratingOptions({ MaxParentalRating: 3 });
        expect(html).toContain('value="PG-13" selected');
    });

    it('adds a disabled "custom score" option when the legacy score matches nothing known', () => {
        const html = ratingOptions({ MaxParentalRating: 99 });
        expect(html).toContain('Custom score 99');
        expect(html).toContain('disabled');
    });

    it('adds an unknown-name option when the stored name is not in the server list', () => {
        const html = ratingOptions({ MaxParentalRatingName: 'NC-17' });
        expect(html).toContain('not defined on this server');
    });

    it('selects nothing (Unrestricted) when the mapping has no rating at all', () => {
        const html = ratingOptions({});
        expect(html).not.toContain('selected');
    });
});

describe('setTestStatus', () => {
    it('sets both the status class and the text', () => {
        const span = document.createElement('span');
        setTestStatus(span, 'error', 'Issuer URL is required');
        expect(span.className).toBe('oidc-test-result oidc-status--error');
        expect(span.textContent).toBe('Issuer URL is required');
    });

    it('omits the modifier class when no variant is given', () => {
        const span = document.createElement('span');
        setTestStatus(span, null, 'Testing...');
        expect(span.className).toBe('oidc-test-result');
    });

    it('does nothing when passed a null element (defensive null-check)', () => {
        expect(() => setTestStatus(null, 'ok', 'x')).not.toThrow();
    });
});

describe('addLibChip', () => {
    it('appends a chip with the library name and a remove control, tagged with the library id', () => {
        __setTestState({ libs: { lib1: 'Movies & TV' } });
        const container = document.createElement('div');
        addLibChip(container, 'lib1');
        const chip = container.querySelector('.oidc-library-chip');
        expect(chip).not.toBeNull();
        expect(chip.getAttribute('data-lib-id')).toBe('lib1');
        expect(chip.textContent).toContain('Movies & TV');
        expect(chip.querySelector('.remove')).not.toBeNull();
    });

    it('falls back to the raw id when the library name is unknown', () => {
        __setTestState({ libs: {} });
        const container = document.createElement('div');
        addLibChip(container, 'unknown-id');
        expect(container.querySelector('.oidc-library-chip').textContent).toContain('unknown-id');
    });
});

// --- DOM-level tests: render*/collect* build and read back real markup via jsdom -----------

function makeProvider(overrides) {
    return Object.assign({
        ProviderId: 'keycloak',
        DisplayName: 'Keycloak',
        Authority: 'https://idp.example.com/realms/x',
        PinnedIssuer: 'https://idp.example.com/realms/x',
        ClientId: 'jellyfin',
        ClientSecret: 'shh',
        Enabled: true,
        ButtonColor: '#4285F4',
        ButtonIcon: 'keycloak'
    }, overrides);
}

function makeView(bodyHtml) {
    document.body.innerHTML = bodyHtml;
    return document.body;
}

describe('renderProviders', () => {
    it('shows the empty-state message and no cards when there are no providers', () => {
        __setTestState({ cfg: { Providers: [] } });
        const view = makeView('<div id="providerList"></div>');

        renderProviders(view);

        expect(view.querySelector('#providerList .oidc-card')).toBeNull();
        expect(view.querySelector('#providerList .oidc-empty')).not.toBeNull();
    });

    it('renders one .oidc-card per provider, with a real <summary> per section', () => {
        __setTestState({ cfg: { Providers: [makeProvider(), makeProvider({ ProviderId: 'okta', DisplayName: 'Okta' })] } });
        const view = makeView('<div id="providerList"></div>');

        renderProviders(view);

        const cards = view.querySelectorAll('#providerList .oidc-card');
        expect(cards.length).toBe(2);
        // 4 provGroup sections per card, each now with a real <summary> (regression guard).
        expect(cards[0].querySelectorAll('details.oidc-section > summary').length).toBe(4);
        expect(cards[0].querySelector('h4').textContent).toBe('Keycloak');
    });

    it('marks a disabled provider with the oidc-disabled class', () => {
        __setTestState({ cfg: { Providers: [makeProvider({ Enabled: false })] } });
        const view = makeView('<div id="providerList"></div>');

        renderProviders(view);

        expect(view.querySelector('#providerList .oidc-card').classList.contains('oidc-disabled')).toBe(true);
    });

    it('disables the first card\'s "move up" button and the last card\'s "move down" button', () => {
        __setTestState({ cfg: { Providers: [makeProvider(), makeProvider({ ProviderId: 'okta' })] } });
        const view = makeView('<div id="providerList"></div>');

        renderProviders(view);

        const cards = view.querySelectorAll('#providerList .oidc-card');
        expect(cards[0].querySelector('[data-action="move-provider"][data-dir="-1"]').disabled).toBe(true);
        expect(cards[0].querySelector('[data-action="move-provider"][data-dir="1"]').disabled).toBe(false);
        expect(cards[1].querySelector('[data-action="move-provider"][data-dir="1"]').disabled).toBe(true);
    });
});

describe('collectProviders', () => {
    it('round-trips a rendered provider back into a config object with edited values', () => {
        __setTestState({ cfg: { Providers: [makeProvider()] } });
        const view = makeView('<div id="providerList"></div>');
        renderProviders(view);

        view.querySelector('#prov_name_0').value = 'Renamed';
        view.querySelector('#prov_scopes_0').value = 'openid profile';
        view.querySelector('#prov_enabled_0').checked = false;
        view.querySelector('#prov_trusted_email_link_0').checked = true;

        const result = collectProviders(view);

        expect(result).toHaveLength(1);
        expect(result[0].ProviderId).toBe('keycloak');
        expect(result[0].DisplayName).toBe('Renamed');
        expect(result[0].Scopes).toBe('openid profile');
        expect(result[0].Enabled).toBe(false);
        expect(result[0].TrustedForEmailLinking).toBe(true);
    });

    it('ignores an edited-but-unverified issuer box for both PinnedIssuer and Authority', () => {
        __setTestState({ cfg: { Providers: [makeProvider()] } });
        const view = makeView('<div id="providerList"></div>');
        renderProviders(view);

        // Simulate an admin editing the visible issuer box without re-running Test Connection.
        // While the field is still marked verified, both PinnedIssuer and Authority are meant to
        // come from the trusted, hidden mirrors (data-verified / prov_discovery_*) - not this
        // box - so an edit here can't silently smuggle an unverified issuer past the save-time
        // guard in the page controller (which blocks the save separately when these diverge).
        view.querySelector('#prov_pinnedissuer_0').value = 'https://attacker.example/';

        const result = collectProviders(view);

        expect(result[0].PinnedIssuer).toBe('https://idp.example.com/realms/x');
        expect(result[0].Authority).toBe('https://idp.example.com/realms/x');
    });

    it('takes Authority straight from the issuer box for a never-pinned (unverified) provider', () => {
        __setTestState({ cfg: { Providers: [makeProvider({ PinnedIssuer: '' })] } });
        const view = makeView('<div id="providerList"></div>');
        renderProviders(view);

        view.querySelector('#prov_pinnedissuer_0').value = 'https://new-idp.example.com/';

        const result = collectProviders(view);

        expect(result[0].PinnedIssuer).toBe('');
        expect(result[0].Authority).toBe('https://new-idp.example.com/');
    });

    it('collects an icon selection of "custom" from the SVG textarea', () => {
        __setTestState({ cfg: { Providers: [makeProvider({ ButtonIcon: '<svg></svg>' })] } });
        const view = makeView('<div id="providerList"></div>');
        renderProviders(view);

        expect(collectIcon(view, 0)).toBe('<svg></svg>');
        expect(collectProviders(view)[0].ButtonIcon).toBe('<svg></svg>');
    });
});

describe('renderRoleMappings / collectRoleMappings', () => {
    it('shows the empty-state message and fills the fallback-role select from cfg when there are no mappings', () => {
        __setTestState({ cfg: { RoleMappings: [], Providers: [], DefaultRoleName: '' } });
        const view = makeView('<div id="roleMappingList"></div><select id="defaultRoleName"></select>');

        renderRoleMappings(view);

        expect(view.querySelector('#roleMappingList .oidc-card')).toBeNull();
        expect(view.querySelector('#roleMappingList .oidc-empty')).not.toBeNull();
    });

    it('renders a role card with a real <summary> (regression guard for the unclosed-summary bug)', () => {
        __setTestState({
            cfg: { RoleMappings: [{ RoleName: 'admins', IsAdmin: true, EnableAllLibraries: true }], Providers: [] }
        });
        const view = makeView('<div id="roleMappingList"></div><select id="defaultRoleName"></select>');

        renderRoleMappings(view);

        const card = view.querySelector('#roleMappingList .oidc-card');
        const summary = card.querySelector('summary');
        expect(summary).not.toBeNull();
        // Everything after the summary must be a *sibling*, not nested inside it - this is
        // exactly what broke before the </summary> close tag was added back.
        expect(summary.querySelector('.oidc-field')).toBeNull();
        expect(card.querySelector(':scope > .oidc-field')).not.toBeNull();
        expect(summary.textContent).toContain('admins');
        expect(summary.querySelector('.oidc-badge').textContent).toBe('Admin');
    });

    it('round-trips an edited role mapping, including added library chips', () => {
        __setTestState({
            cfg: {
                RoleMappings: [{ RoleName: 'viewers', IsAdmin: false, LibraryIds: ['lib1'] }],
                Providers: []
            },
            libs: { lib1: 'Movies', lib2: 'TV Shows' }
        });
        const view = makeView('<div id="roleMappingList"></div><select id="defaultRoleName"></select>');

        renderRoleMappings(view);
        view.querySelector('#role_name_0').value = 'renamed-role';
        view.querySelector('#role_admin_0').checked = true;

        const result = collectRoleMappings(view);

        expect(result).toHaveLength(1);
        expect(result[0].RoleName).toBe('renamed-role');
        expect(result[0].IsAdmin).toBe(true);
        expect(result[0].LibraryIds).toEqual(['lib1']);
    });
});

describe('updateRbacManagementUi', () => {
    function fixture() {
        return makeView([
            '<div id="roleMappingList"></div>',
            '<select id="defaultRoleName"></select>',
            '<button id="btnAddRoleMapping"></button>',
            '<p id="roleMappingsInactiveHint" hidden></p>',
            '<input type="checkbox" id="manageUserPolicy" />'
        ].join(''));
    }

    it('dims the role list/fallback/add button and shows the hint when policy management is off', () => {
        const view = fixture();
        view.querySelector('#manageUserPolicy').checked = false;

        updateRbacManagementUi(view);

        expect(view.querySelector('#roleMappingList').classList.contains('oidc-dimmed')).toBe(true);
        expect(view.querySelector('#defaultRoleName').disabled).toBe(true);
        expect(view.querySelector('#btnAddRoleMapping').disabled).toBe(true);
        expect(view.querySelector('#roleMappingsInactiveHint').hidden).toBe(false);
    });

    it('clears the dimmed state and hint when policy management is on', () => {
        const view = fixture();
        view.querySelector('#manageUserPolicy').checked = true;

        updateRbacManagementUi(view);

        expect(view.querySelector('#roleMappingList').classList.contains('oidc-dimmed')).toBe(false);
        expect(view.querySelector('#defaultRoleName').disabled).toBe(false);
        expect(view.querySelector('#roleMappingsInactiveHint').hidden).toBe(true);
    });
});

describe('updateEmailAllowlistUi', () => {
    function fixture() {
        return makeView([
            '<input type="checkbox" id="requireVerifiedEmail" />',
            '<div id="emailAllowlistFields">',
            '  <div id="emailAllowlistInertWarning" class="oidc-warning" hidden></div>',
            '  <div class="oidc-field"><textarea id="allowedEmailDomains"></textarea></div>',
            '  <div class="oidc-field"><textarea id="allowedEmails"></textarea></div>',
            '</div>'
        ].join(''));
    }

    it('dims the allowlist fields and disables the textareas while verified-email is off', () => {
        const view = fixture();
        view.querySelector('#requireVerifiedEmail').checked = false;

        updateEmailAllowlistUi(view);

        view.querySelectorAll('#emailAllowlistFields .oidc-field').forEach((field) => {
            expect(field.classList.contains('oidc-dimmed')).toBe(true);
        });
        view.querySelectorAll('#emailAllowlistFields textarea').forEach((ta) => {
            expect(ta.disabled).toBe(true);
        });
    });

    it('re-enables the fields once verified-email is on', () => {
        const view = fixture();
        view.querySelector('#requireVerifiedEmail').checked = true;

        updateEmailAllowlistUi(view);

        view.querySelectorAll('#emailAllowlistFields .oidc-field').forEach((field) => {
            expect(field.classList.contains('oidc-dimmed')).toBe(false);
        });
        view.querySelectorAll('#emailAllowlistFields textarea').forEach((ta) => {
            expect(ta.disabled).toBe(false);
        });
    });

    it('warns only when the lists have content while verified-email is off', () => {
        const view = fixture();
        const warn = view.querySelector('#emailAllowlistInertWarning');

        // off + empty → no warning (dimming alone)
        view.querySelector('#requireVerifiedEmail').checked = false;
        updateEmailAllowlistUi(view);
        expect(warn.hidden).toBe(true);

        // off + populated → warning
        view.querySelector('#allowedEmails').value = 'a@example.com';
        updateEmailAllowlistUi(view);
        expect(warn.hidden).toBe(false);

        // on + populated → no warning (lists are enforced)
        view.querySelector('#requireVerifiedEmail').checked = true;
        updateEmailAllowlistUi(view);
        expect(warn.hidden).toBe(true);
    });
});

describe('setDirty', () => {
    it('shows the unsaved-changes indicator and marks the save button dirty', () => {
        const view = makeView('<span id="saveStatus"></span><button id="btnSave"></button>');
        __setTestState({ dirtyView: view });

        setDirty(true);

        expect(view.querySelector('#saveStatus').textContent).toBe('● Unsaved changes');
        expect(view.querySelector('#btnSave').classList.contains('oidc-save-dirty')).toBe(true);
    });

    it('clears the indicator when set back to false', () => {
        const view = makeView('<span id="saveStatus"></span><button id="btnSave"></button>');
        __setTestState({ dirtyView: view });

        setDirty(true);
        setDirty(false);

        expect(view.querySelector('#saveStatus').textContent).toBe('');
        expect(view.querySelector('#btnSave').classList.contains('oidc-save-dirty')).toBe(false);
    });

    it('does nothing when no dirtyView has been wired yet', () => {
        __setTestState({ dirtyView: null });
        expect(() => setDirty(true)).not.toThrow();
    });
});

describe('gval / gchk', () => {
    it('reads an input value and a checkbox state by id, defaulting safely when missing', () => {
        const view = makeView('<input id="a" value="hello" /><input id="b" type="checkbox" checked />');

        expect(gval(view, 'a')).toBe('hello');
        expect(gval(view, 'missing')).toBe('');
        expect(gchk(view, 'b')).toBe(true);
        expect(gchk(view, 'missing')).toBe(false);
    });
});
