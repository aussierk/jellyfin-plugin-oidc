// Integration test for the page controller (index.js's default export) - the one thing the
// per-module unit tests in oidcrbac.test.js can't cover, since index.js only exports default.
// Loads the *real* configPage.html markup into jsdom, mocks Jellyfin's ApiClient/Dashboard
// globals, and drives the actual view lifecycle events (viewshow, clicks) a real page load
// would fire, to catch wiring bugs the module split could introduce (wrong id, an event
// listener attached before its target exists, a state setter never called, etc.).
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import initPage from './index.js';
import { __setTestState } from './state.js';

const __dirname = dirname(fileURLToPath(import.meta.url));
const configPageHtml = readFileSync(join(__dirname, '..', 'configPage.html'), 'utf8');

function makeConfig(overrides) {
    return Object.assign({
        Providers: [],
        RoleMappings: [],
        AutoCreateUsers: true,
        MigrateLocalUsers: false,
        BlockPrivateNetworkAuthorities: false,
        AllowedGroups: [],
        RequireVerifiedEmail: false,
        AllowedEmailDomains: [],
        AllowedEmails: [],
        LinkExistingUsersByEmail: false,
        ManageUserPolicy: true,
        EnableLibraryAccessManagement: true,
        ManageLoginButtonBranding: true,
        HideManualLogin: false,
        LoginTitle: '',
        LoginSubtitle: '',
        DefaultRoleName: ''
    }, overrides);
}

// Minimal stand-ins for the Jellyfin dashboard globals oidcrbac.js calls directly (not passed
// in as parameters) - ApiClient for server calls, Dashboard for the loading spinner/alerts.
function installJellyfinGlobals(config) {
    const alerts = [];
    global.ApiClient = {
        getUrl: (path) => 'https://jellyfin.test/' + path,
        getJSON: vi.fn((url) => {
            if (url.includes('Config/Libraries')) return Promise.resolve({});
            if (url.includes('Config/Ratings')) return Promise.resolve([]);
            if (url.includes('LoginButtonSnippet')) return Promise.resolve({ Html: '', Css: '' });
            return Promise.resolve({});
        }),
        getPluginConfiguration: vi.fn(() => Promise.resolve(config)),
        updatePluginConfiguration: vi.fn(() => Promise.resolve({})),
        getNamedConfiguration: vi.fn(() => Promise.resolve({})),
        updateNamedConfiguration: vi.fn(() => Promise.resolve({})),
        ajax: vi.fn(() => Promise.resolve({ Success: false, Error: 'not used in these tests' })),
        serverAddress: () => 'https://jellyfin.test'
    };
    global.Dashboard = {
        showLoadingMsg: vi.fn(),
        hideLoadingMsg: vi.fn(),
        alert: vi.fn((opts) => alerts.push(opts)),
        processPluginConfigurationUpdateResult: vi.fn()
    };
    // jsdom doesn't implement window.confirm; the "unpinned providers" save warning uses it.
    global.confirm = vi.fn(() => true);
    window.confirm = global.confirm;
    return { alerts };
}

async function flushMicrotasks() {
    // The viewshow handler chains several .then()s; a handful of empty microtask turns is
    // enough for jsdom + real Promises to settle without resorting to fake timers.
    for (let i = 0; i < 10; i++) {
        await Promise.resolve();
    }
}

describe('index.js default export (full page controller)', () => {
    let view;

    beforeEach(() => {
        __setTestState({ cfg: null, libs: {}, ratings: [] });
        document.body.innerHTML = configPageHtml;
        view = document.body;
    });

    afterEach(() => {
        delete global.ApiClient;
        delete global.Dashboard;
        vi.restoreAllMocks();
    });

    it('wires up without throwing against the real configPage.html markup', () => {
        installJellyfinGlobals(makeConfig());
        expect(() => initPage(view)).not.toThrow();
    });

    it('loads config on viewshow and renders providers/roles into the real markup', async () => {
        installJellyfinGlobals(makeConfig({
            Providers: [{ ProviderId: 'keycloak', DisplayName: 'Keycloak', Enabled: true, ClientId: 'jf', Authority: 'https://idp.example.com' }],
            RoleMappings: [{ RoleName: 'admins', IsAdmin: true }],
            LoginTitle: 'Welcome'
        }));
        initPage(view);

        view.dispatchEvent(new Event('viewshow'));
        await flushMicrotasks();

        expect(view.querySelectorAll('#providerList .oidc-item-card')).toHaveLength(1);
        expect(view.querySelector('#providerList h4').textContent).toBe('Keycloak');
        expect(view.querySelectorAll('#roleMappingList .oidc-item-card')).toHaveLength(1);
        expect(view.querySelector('#loginTitle').value).toBe('Welcome');
        expect(Dashboard.showLoadingMsg).toHaveBeenCalled();
        expect(Dashboard.hideLoadingMsg).toHaveBeenCalled();
    });

    it('switches tabs on tabchange, showing the target panel and hiding the others', async () => {
        // emby-tabs is a real Jellyfin custom element that only upgrades in a browser with its
        // webcomponents-lite polyfill loaded (absent in jsdom), so a plain .click() on a tab
        // button here wouldn't fire its internal handling. Dispatch the tabchange event it fires
        // on click instead, to test our own listener - the part this test suite actually owns.
        installJellyfinGlobals(makeConfig());
        initPage(view);
        view.dispatchEvent(new Event('viewshow'));
        await flushMicrotasks();

        view.querySelector('.oidc-tabs').dispatchEvent(new CustomEvent('tabchange', { detail: { selectedTabIndex: 2 } }));

        expect(view.querySelector('#tab-roles').classList.contains('is-active')).toBe(true);
        expect(view.querySelector('#tab-general').classList.contains('is-active')).toBe(false);
        expect(view.querySelector('.emby-tab-button[data-tab="roles"]').getAttribute('aria-selected')).toBe('true');
        expect(view.querySelector('.emby-tab-button[data-tab="general"]').getAttribute('aria-selected')).toBe('false');
    });

    it('adds a new provider card when "+ Add Provider" is clicked', async () => {
        installJellyfinGlobals(makeConfig());
        initPage(view);
        view.dispatchEvent(new Event('viewshow'));
        await flushMicrotasks();

        expect(view.querySelectorAll('#providerList .oidc-item-card')).toHaveLength(0);
        view.querySelector('#btnAddProvider').click();

        expect(view.querySelectorAll('#providerList .oidc-item-card')).toHaveLength(1);
        expect(view.querySelector('#providerList h4').textContent).toBe('New Provider');
    });

    it('adds a new role mapping card when "+ Add Role Mapping" is clicked', async () => {
        installJellyfinGlobals(makeConfig());
        initPage(view);
        view.dispatchEvent(new Event('viewshow'));
        await flushMicrotasks();

        view.querySelector('#btnAddRoleMapping').click();

        expect(view.querySelectorAll('#roleMappingList .oidc-item-card')).toHaveLength(1);
    });

    it('blocks Save and alerts when two providers share a Provider ID', async () => {
        installJellyfinGlobals(makeConfig({
            Providers: [
                { ProviderId: 'dup', DisplayName: 'A', Enabled: true, ClientId: 'a', Authority: 'https://a.example.com' },
                { ProviderId: 'dup', DisplayName: 'B', Enabled: true, ClientId: 'b', Authority: 'https://b.example.com' }
            ]
        }));
        initPage(view);
        view.dispatchEvent(new Event('viewshow'));
        await flushMicrotasks();

        view.querySelector('#btnSave').click();

        expect(Dashboard.alert).toHaveBeenCalledWith(expect.objectContaining({ title: 'Duplicate Provider ID' }));
        expect(ApiClient.updatePluginConfiguration).not.toHaveBeenCalled();
    });

    it('saves successfully when the form is valid', async () => {
        installJellyfinGlobals(makeConfig({
            Providers: [{
                ProviderId: 'keycloak', DisplayName: 'Keycloak', Enabled: true, ClientId: 'jf',
                Authority: 'https://idp.example.com', PinnedIssuer: 'https://idp.example.com'
            }]
        }));
        initPage(view);
        view.dispatchEvent(new Event('viewshow'));
        await flushMicrotasks();

        view.querySelector('#btnSave').click();
        await flushMicrotasks();

        expect(ApiClient.updatePluginConfiguration).toHaveBeenCalledTimes(1);
        const [, savedConfig] = ApiClient.updatePluginConfiguration.mock.calls[0];
        expect(savedConfig.Providers).toHaveLength(1);
        expect(Dashboard.alert).not.toHaveBeenCalled();
    });
});
